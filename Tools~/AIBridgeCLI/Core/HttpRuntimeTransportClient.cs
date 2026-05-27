using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AIBridgeCLI.Core
{
    public sealed class HttpRuntimeTransportClient : IRuntimeTransportClient
    {
        private const string TransportName = "http";
        private const string CheckPassed = "passed";
        private const string CheckFailed = "failed";
        private const int HealthProbeTimeoutMs = 100;
        private const int LocalPortScanIdleMissLimit = 8;

        private readonly RuntimeTransportOptions _options;
        private readonly HttpClient _httpClient;
        private readonly Dictionary<string, CommandResult> _syncResults = new Dictionary<string, CommandResult>();

        public HttpRuntimeTransportClient(RuntimeTransportOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromMilliseconds(Math.Max(1000, _options.TimeoutMs + 5000))
            };
        }

        public RuntimeTransportKind Kind => RuntimeTransportKind.Http;

        public IReadOnlyList<RuntimeTargetInfo> ListTargets()
        {
            var targets = new List<RuntimeTargetInfo>();
            AddTargetFromHealth(targets, _options.HttpUrl, TryGetHealth(_options.HttpUrl));

            var cached = RuntimeDiscoveryClient.ReadFreshCache(RuntimeDiscoveryClient.DefaultCacheSeconds);
            for (var i = 0; i < cached.Count; i++)
            {
                var target = cached[i];
                if (string.IsNullOrWhiteSpace(target.url)
                    || ContainsTarget(targets, target.targetId, target.url))
                {
                    continue;
                }

                targets.Add(new RuntimeTargetInfo
                {
                    targetId = target.targetId,
                    path = target.url,
                    heartbeatPath = target.url.TrimEnd('/') + "/aibridge/health",
                    commandsPath = target.url.TrimEnd('/') + "/aibridge/commands",
                    resultsPath = target.url.TrimEnd('/') + "/aibridge/results",
                    screenshotsPath = target.url,
                    stale = false,
                    ageSeconds = 0,
                    lastHeartbeatUtc = target.lastSeenUtc,
                    heartbeat = JObject.FromObject(target)
                });
            }

            if (!_options.HttpUrlExplicit)
            {
                AddLocalPortScanTargets(targets);
            }

            targets.Sort(CompareTargets);
            return targets;
        }

        public RuntimeTargetInfo ResolveTarget(string target)
        {
            var targets = ListTargets();
            if (targets.Count == 0)
            {
                return null;
            }

            var resolvedTarget = string.IsNullOrWhiteSpace(target) ? RuntimeTransportOptions.DefaultTarget : target;
            if (string.Equals(resolvedTarget, RuntimeTransportOptions.DefaultTarget, StringComparison.OrdinalIgnoreCase)
                || string.Equals(resolvedTarget, targets[0].targetId, StringComparison.OrdinalIgnoreCase))
            {
                return targets[0];
            }

            for (var i = 0; i < targets.Count; i++)
            {
                if (string.Equals(resolvedTarget, targets[i].targetId, StringComparison.OrdinalIgnoreCase))
                {
                    return targets[i];
                }
            }

            return null;
        }

        private void AddLocalPortScanTargets(List<RuntimeTargetInfo> targets)
        {
            var startPort = RuntimeDiscoveryClient.DefaultHttpPort;
            var endPort = Math.Min(65535, startPort + RuntimeDiscoveryClient.DefaultPortScanCount - 1);
            var foundAny = targets.Count > 0;
            var missesAfterHit = 0;
            for (var port = startPort; port <= endPort; port++)
            {
                var url = "http://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture);
                if (targets.Exists(existing => string.Equals(existing.path, url, StringComparison.OrdinalIgnoreCase)))
                {
                    foundAny = true;
                    missesAfterHit = 0;
                    continue;
                }

                var beforeCount = targets.Count;
                AddTargetFromHealth(targets, url, TryGetHealth(url));
                if (targets.Count > beforeCount)
                {
                    foundAny = true;
                    missesAfterHit = 0;
                    continue;
                }

                if (!foundAny)
                {
                    continue;
                }

                missesAfterHit++;
                if (missesAfterHit >= LocalPortScanIdleMissLimit)
                {
                    break;
                }
            }
        }

        private static void AddTargetFromHealth(List<RuntimeTargetInfo> targets, string baseUrl, JObject health)
        {
            if (targets == null || health == null || string.IsNullOrWhiteSpace(baseUrl))
            {
                return;
            }

            var targetId = ReadString(health, "targetId");
            if (string.IsNullOrWhiteSpace(targetId))
            {
                targetId = "http";
            }

            if (ContainsTarget(targets, targetId, baseUrl))
            {
                return;
            }

            var url = baseUrl.TrimEnd('/');
            targets.Add(new RuntimeTargetInfo
            {
                targetId = targetId,
                path = url,
                heartbeatPath = url + "/aibridge/health",
                commandsPath = url + "/aibridge/commands",
                resultsPath = url + "/aibridge/results",
                screenshotsPath = url,
                stale = false,
                ageSeconds = 0,
                lastHeartbeatUtc = ReadString(health, "lastHeartbeatUtc"),
                heartbeat = health
            });
        }

        private static bool ContainsTarget(List<RuntimeTargetInfo> targets, string targetId, string url)
        {
            return targets.Exists(existing =>
                (!string.IsNullOrWhiteSpace(targetId)
                    && string.Equals(existing.targetId, targetId, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(url)
                    && string.Equals(existing.path, url.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)));
        }

        private static int CompareTargets(RuntimeTargetInfo left, RuntimeTargetInfo right)
        {
            var uptimeCompare = ReadUptimeSeconds(left).CompareTo(ReadUptimeSeconds(right));
            if (uptimeCompare != 0)
            {
                return uptimeCompare;
            }

            return string.Compare(left == null ? null : left.targetId, right == null ? null : right.targetId, StringComparison.OrdinalIgnoreCase);
        }

        private static double ReadUptimeSeconds(RuntimeTargetInfo target)
        {
            if (target == null || target.heartbeat == null)
            {
                return double.MaxValue;
            }

            var token = target.heartbeat["uptimeSeconds"];
            if (token == null)
            {
                return double.MaxValue;
            }

            return token.Type == JTokenType.Float || token.Type == JTokenType.Integer
                ? token.Value<double>()
                : double.MaxValue;
        }

        public RuntimeSendResult Send(RuntimeTargetInfo target, CommandRequest request)
        {
            if (request == null)
            {
                return new RuntimeSendResult
                {
                    Success = false,
                    Error = "Runtime HTTP request is null."
                };
            }

            try
            {
                var url = BuildUrl(target, "/aibridge/commands?timeoutMs=" + _options.TimeoutMs.ToString(CultureInfo.InvariantCulture));
                var json = JsonConvert.SerializeObject(request, Formatting.None, new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore
                });
                var responseJson = SendJson(url, HttpMethod.Post, json, ResolveToken(request));
                var result = RuntimeResultParser.Parse(request.id, responseJson);
                TryPrepareScreenshotArtifact(result, request, target);
                lock (_syncResults)
                {
                    _syncResults[request.id] = result;
                }

                return new RuntimeSendResult
                {
                    Success = true,
                    CommandPath = url
                };
            }
            catch (Exception ex)
            {
                return new RuntimeSendResult
                {
                    Success = false,
                    Error = "HTTP runtime transport failed: " + ex.Message
                };
            }
        }

        public RuntimeReceiveResult WaitResult(RuntimeTargetInfo target, string commandId, int timeoutMs, int pollIntervalMs)
        {
            var startTime = DateTime.UtcNow;
            while ((DateTime.UtcNow - startTime).TotalMilliseconds < timeoutMs)
            {
                lock (_syncResults)
                {
                    if (_syncResults.TryGetValue(commandId, out var cachedResult))
                    {
                        _syncResults.Remove(commandId);
                        return new RuntimeReceiveResult
                        {
                            Success = true,
                            Result = cachedResult
                        };
                    }
                }

                var result = TryPollResult(target, commandId);
                if (result != null)
                {
                    return new RuntimeReceiveResult
                    {
                        Success = true,
                        Result = result
                    };
                }

                Thread.Sleep(Math.Max(10, pollIntervalMs));
            }

            return new RuntimeReceiveResult
            {
                Success = false,
                TimedOut = true,
                Error = "Timeout waiting for HTTP runtime result."
            };
        }

        public void CleanupCommand(RuntimeTargetInfo target, string commandId)
        {
        }

        public RuntimeDiagnosticReport Diagnose(string target, RuntimeCommandTrace commandTrace = null)
        {
            var report = new RuntimeDiagnosticReport
            {
                transport = TransportName,
                runtimeDirectory = null,
                targetId = target
            };

            try
            {
                var targetInfo = ResolveTarget(target);
                var diagnosticUrl = targetInfo == null || string.IsNullOrWhiteSpace(targetInfo.path)
                    ? _options.HttpUrl
                    : targetInfo.path;
                var health = TryGetHealth(diagnosticUrl, _options.TimeoutMs);
                if (health == null)
                {
                    report.checks.Add(new RuntimeDiagnosticCheck
                    {
                        name = "httpEndpoint",
                        status = CheckFailed,
                        detail = "HTTP endpoint did not return a valid health payload: " + diagnosticUrl,
                        fix = "Verify runtimeSettings.enableHttpTransport, bind/port, firewall, and --url."
                    });
                    report.summary = "HTTP endpoint health check failed.";
                    report.success = false;
                    return report;
                }

                report.targetId = ReadString(health, "targetId");
                report.checks.Add(new RuntimeDiagnosticCheck
                {
                    name = "httpEndpoint",
                    status = CheckPassed,
                    detail = "HTTP health endpoint is reachable: " + BuildUrl(diagnosticUrl, "/aibridge/health")
                });
                report.checks.Add(new RuntimeDiagnosticCheck
                {
                    name = "authHeader",
                    status = string.IsNullOrEmpty(_options.Token) ? "warning" : CheckPassed,
                    detail = string.IsNullOrEmpty(_options.Token)
                        ? "No --token was provided. This is valid only when runtimeSettings.authToken is empty."
                        : "Authorization bearer token will be sent."
                });
                report.suggestions.Add("Run: $CLI runtime status --transport http --url " + diagnosticUrl);
                report.success = true;
                report.summary = "Runtime HTTP transport diagnostics passed.";
                return report;
            }
            catch (Exception ex)
            {
                report.checks.Add(new RuntimeDiagnosticCheck
                {
                    name = "httpEndpoint",
                    status = CheckFailed,
                    detail = ex.Message,
                    fix = "Verify the Player is running, HTTP transport is enabled, and the URL is reachable."
                });
                report.summary = ex.Message;
                report.success = false;
                return report;
            }
        }

        private JObject TryGetHealth(string baseUrl)
        {
            return TryGetHealth(baseUrl, HealthProbeTimeoutMs);
        }

        private JObject TryGetHealth(string baseUrl, int timeoutMs)
        {
            try
            {
                var json = SendJson(BuildUrl(baseUrl, "/aibridge/health"), HttpMethod.Get, null, _options.Token, timeoutMs);
                return JObject.Parse(json);
            }
            catch
            {
                return null;
            }
        }

        private CommandResult TryPollResult(RuntimeTargetInfo target, string commandId)
        {
            try
            {
                var json = SendJson(BuildUrl(target, "/aibridge/results/" + Uri.EscapeDataString(commandId)), HttpMethod.Get, null, _options.Token);
                return RuntimeResultParser.Parse(commandId, json);
            }
            catch
            {
                return null;
            }
        }

        private string SendJson(string url, HttpMethod method, string json, string token, int? timeoutMs = null)
        {
            using (var message = new HttpRequestMessage(method, url))
            {
                if (!string.IsNullOrWhiteSpace(token))
                {
                    message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                }

                if (json != null)
                {
                    message.Content = new StringContent(json, Encoding.UTF8, "application/json");
                }

                CancellationTokenSource cancellation = null;
                try
                {
                    if (timeoutMs.HasValue)
                    {
                        cancellation = new CancellationTokenSource(Math.Max(100, timeoutMs.Value));
                    }

                    var responseTask = cancellation == null
                        ? _httpClient.SendAsync(message)
                        : _httpClient.SendAsync(message, cancellation.Token);
                    using (var response = responseTask.GetAwaiter().GetResult())
                    {
                        var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                        if (!response.IsSuccessStatusCode && string.IsNullOrWhiteSpace(body))
                        {
                            throw new InvalidOperationException("HTTP " + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture) + " " + response.ReasonPhrase);
                        }

                        return body;
                    }
                }
                finally
                {
                    if (cancellation != null)
                    {
                        cancellation.Dispose();
                    }
                }
            }
        }

        private byte[] SendBytes(string url, string token)
        {
            using (var message = new HttpRequestMessage(HttpMethod.Get, url))
            {
                if (!string.IsNullOrWhiteSpace(token))
                {
                    message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                }

                using (var response = _httpClient.SendAsync(message).GetAwaiter().GetResult())
                {
                    var bytes = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new InvalidOperationException("HTTP " + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture) + " " + response.ReasonPhrase);
                    }

                    return bytes;
                }
            }
        }

        private void TryPrepareScreenshotArtifact(CommandResult result, CommandRequest request, RuntimeTargetInfo target)
        {
            if (result == null
                || !result.success
                || request == null
                || !string.Equals(RuntimePathHelper.GetRuntimeAction(request), "runtime.screenshot", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var data = result.data as JObject;
            if (data == null)
            {
                return;
            }

            var filename = ReadString(data, "filename");
            if (string.IsNullOrWhiteSpace(filename))
            {
                return;
            }

            try
            {
                var cachePath = BuildArtifactCachePath(filename, target);
                Directory.CreateDirectory(Path.GetDirectoryName(cachePath));
                var bytes = SendBytes(BuildUrl(target, "/aibridge/artifacts/" + Uri.EscapeDataString(filename)), ResolveToken(request));
                File.WriteAllBytes(cachePath, bytes);
                data["devicePath"] = ReadString(data, "imagePath");
                data["imagePath"] = cachePath;
                data["pcPath"] = cachePath;
                data["artifactDownloaded"] = true;
            }
            catch (Exception ex)
            {
                if (TryGetRequestParam(request, "output", out var output) && !string.IsNullOrWhiteSpace(output))
                {
                    result.success = false;
                    result.error = "artifact_pull_failed: " + ex.Message;
                }
            }
        }

        private string BuildArtifactCachePath(string filename, RuntimeTargetInfo target)
        {
            var safeName = Path.GetFileName(filename);
            var baseUrl = target == null || string.IsNullOrWhiteSpace(target.path) ? _options.HttpUrl : target.path;
            var cacheRoot = Path.Combine(PathHelper.GetExchangeDirectory(), "runtime-cache", "http", SanitizePathPart(baseUrl));
            return Path.Combine(cacheRoot, safeName);
        }

        private static string SanitizePathPart(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "default";
            }

            var invalid = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(value.Length);
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                builder.Append(Array.IndexOf(invalid, c) >= 0 || c == ':' || c == '/' || c == '\\' ? '_' : c);
            }

            return builder.ToString();
        }

        private string BuildUrl(string path)
        {
            return BuildUrl(_options.HttpUrl, path);
        }

        private static string BuildUrl(string baseUrl, string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return baseUrl;
            }

            return (baseUrl ?? string.Empty).TrimEnd('/') + "/" + path.TrimStart('/');
        }

        private string BuildUrl(RuntimeTargetInfo target, string path)
        {
            var baseUrl = target == null || string.IsNullOrWhiteSpace(target.path) ? _options.HttpUrl : target.path;
            return BuildUrl(baseUrl, path);
        }

        private string ResolveToken(CommandRequest request)
        {
            if (!string.IsNullOrWhiteSpace(_options.Token))
            {
                return _options.Token;
            }

            if (request == null || request.@params == null)
            {
                return null;
            }

            foreach (var pair in request.@params)
            {
                if (string.Equals(pair.Key, "token", StringComparison.OrdinalIgnoreCase))
                {
                    return Convert.ToString(pair.Value, CultureInfo.InvariantCulture);
                }
            }

            return null;
        }

        private static bool TryGetRequestParam(CommandRequest request, string key, out string value)
        {
            value = null;
            if (request == null || request.@params == null)
            {
                return false;
            }

            foreach (var pair in request.@params)
            {
                if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    value = Convert.ToString(pair.Value, CultureInfo.InvariantCulture);
                    return true;
                }
            }

            return false;
        }

        private static string ReadString(JObject obj, string key)
        {
            return obj != null && obj.TryGetValue(key, StringComparison.OrdinalIgnoreCase, out var value)
                ? value.Value<string>()
                : null;
        }
    }
}
