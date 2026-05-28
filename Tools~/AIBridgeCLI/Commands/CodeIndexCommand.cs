using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using AIBridgeCLI.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AIBridgeCLI.Commands
{
    public static class CodeIndexCommand
    {
        private const string IndexDirectoryName = "code-index";
        private const string DaemonDirectoryName = "CodeIndex";
        private const string DaemonAssemblyName = "AIBridgeCodeIndex";
        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore
        };

        public static int Execute(string action, Dictionary<string, string> options, int timeout, bool noWait, OutputMode outputMode)
        {
            var stopwatch = Stopwatch.StartNew();
            JObject result;
            try
            {
                result = ExecuteAsync(action, options, Math.Max(1000, timeout), noWait).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                result = BuildFailure(null, "code_index failed: " + ex.Message);
            }

            result["executionTime"] = stopwatch.ElapsedMilliseconds;
            Print(result, outputMode);
            return result.Value<bool>("success") ? 0 : 1;
        }

        private static async Task<JObject> ExecuteAsync(string action, Dictionary<string, string> options, int timeout, bool noWait)
        {
            var normalizedAction = string.IsNullOrWhiteSpace(action) ? "status" : action.Trim().ToLowerInvariant();
            var context = CodeIndexContext.Resolve(options);

            switch (normalizedAction)
            {
                case "status":
                    return await BuildStatusAsync(context, timeout);
                case "doctor":
                    return await BuildDoctorAsync(context, timeout);
                case "warmup":
                    return await WarmupAsync(context, timeout, noWait);
                case "reset":
                    return await ResetAsync(context, timeout);
                case "symbol":
                case "definition":
                case "references":
                    return await QueryAsync(context, normalizedAction, options, timeout);
                default:
                    return BuildFailure(context, "Unsupported code_index action: " + action);
            }
        }

        private static async Task<JObject> QueryAsync(CodeIndexContext context, string action, Dictionary<string, string> options, int timeout)
        {
            ValidateQueryArguments(action, options);

            var status = await EnsureDaemonAsync(context, timeout, false);
            if (IsReady(status))
            {
                var response = await PostJsonAsync(
                    status.Value<string>("endpoint"),
                    "query",
                    status.Value<string>("token"),
                    new JObject
                    {
                        ["action"] = action,
                        ["parameters"] = JObject.FromObject(BuildQueryParameters(action, options))
                    },
                    timeout);

                if (response.Value<bool>("success"))
                {
                    return response;
                }

                if (!IsFallbackEnabled(options))
                {
                    return response;
                }

                return BuildFallback(context, action, options, response.Value<string>("error"));
            }

            if (!IsFallbackEnabled(options))
            {
                return BuildFailure(context, "Roslyn workspace is not ready.");
            }

            return BuildFallback(context, action, options, "Roslyn workspace is not ready. Returned text-search candidates only.");
        }

        private static async Task<JObject> WarmupAsync(CodeIndexContext context, int timeout, bool noWait)
        {
            var status = await EnsureDaemonAsync(context, timeout, noWait);
            if (status != null && status.Value<bool?>("success") == false)
            {
                return status;
            }

            var result = BuildStatusResult(context, status, status != null && status.Value<bool>("reachable"));
            result["success"] = true;
            result["semantic"] = IsReady(status);
            result["source"] = "roslyn-msbuild";
            if (status != null && string.Equals(status.Value<string>("state"), "failed", StringComparison.OrdinalIgnoreCase))
            {
                result["success"] = false;
                result["error"] = status.Value<string>("message") ?? "Code index warmup failed.";
            }

            return result;
        }

        private static async Task<JObject> ResetAsync(CodeIndexContext context, int timeout)
        {
            var status = ReadStatusFile(context);
            var daemonPid = status == null ? 0 : status.Value<int?>("daemonPid") ?? 0;
            if (status != null && !string.IsNullOrWhiteSpace(status.Value<string>("endpoint")))
            {
                try
                {
                    await PostJsonAsync(status.Value<string>("endpoint"), "shutdown", status.Value<string>("token"), new JObject(), timeout);
                }
                catch
                {
                }
            }

            await EnsureDaemonStoppedAsync(daemonPid);

            if (Directory.Exists(context.IndexDirectory))
            {
                Directory.Delete(context.IndexDirectory, true);
            }

            return new JObject
            {
                ["success"] = true,
                ["semantic"] = false,
                ["source"] = "reset",
                ["state"] = "reset",
                ["stale"] = true,
                ["projectRoot"] = context.ProjectRoot,
                ["solution"] = context.SolutionPath,
                ["message"] = "code_index cache and daemon state were reset."
            };
        }

        private static async Task EnsureDaemonStoppedAsync(int daemonPid)
        {
            if (daemonPid <= 0)
            {
                return;
            }

            for (var i = 0; i < 20; i++)
            {
                if (!TryGetCodeIndexProcess(daemonPid, out var process))
                {
                    return;
                }

                process.Dispose();
                await Task.Delay(100);
            }

            if (!TryGetCodeIndexProcess(daemonPid, out var remaining))
            {
                return;
            }

            using (remaining)
            {
                remaining.Kill();
                remaining.WaitForExit(2000);
            }
        }

        private static bool TryGetCodeIndexProcess(int processId, out Process process)
        {
            process = null;
            try
            {
                var candidate = Process.GetProcessById(processId);
                if (candidate.HasExited
                    || candidate.ProcessName.IndexOf(DaemonAssemblyName, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    candidate.Dispose();
                    return false;
                }

                process = candidate;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static async Task<JObject> BuildStatusAsync(CodeIndexContext context, int timeout)
        {
            var status = ReadStatusFile(context);
            var reachable = false;
            if (status != null)
            {
                var remote = await TryGetRemoteStatusAsync(status, timeout);
                if (remote != null)
                {
                    reachable = true;
                    CopyStatusRuntimeFields(status, remote);
                }
            }

            return BuildStatusResult(context, status, reachable);
        }

        private static async Task<JObject> BuildDoctorAsync(CodeIndexContext context, int timeout)
        {
            var issues = new JArray();
            var suggestions = new JArray();

            if (!context.HasUnityProjectMarkers)
            {
                issues.Add("Project root does not look like a Unity project. Assets/ and ProjectSettings/ProjectSettings.asset were not found.");
                suggestions.Add("Run from a Unity project root or pass --project-root.");
            }

            if (string.IsNullOrWhiteSpace(context.SolutionPath) || !File.Exists(context.SolutionPath))
            {
                issues.Add("No .sln file was found at the project root.");
                suggestions.Add("Open the Unity project once or regenerate C# project files from Unity External Tools.");
            }

            var daemon = CodeIndexDaemonExecutable.Resolve();
            if (!daemon.CanStart)
            {
                issues.Add("AIBridgeCodeIndex daemon executable or project was not found.");
                suggestions.Add("Build the CLI package so Tools~/CLI/<rid>/CodeIndex is available.");
            }

            var status = ReadStatusFile(context);
            var remote = status == null ? null : await TryGetRemoteStatusAsync(status, timeout);
            var reachable = remote != null;
            if (status == null)
            {
                suggestions.Add("Run code_index warmup to start the daemon.");
            }
            else if (!reachable)
            {
                issues.Add("status.json exists but daemon endpoint is not reachable.");
                suggestions.Add("Run code_index reset, then code_index warmup.");
            }

            return new JObject
            {
                ["success"] = true,
                ["healthy"] = issues.Count == 0,
                ["semantic"] = reachable && IsReady(remote ?? status),
                ["source"] = "doctor",
                ["state"] = remote == null ? (status == null ? "missing" : status.Value<string>("state")) : remote.Value<string>("state"),
                ["stale"] = !reachable || !IsReady(remote ?? status),
                ["projectRoot"] = context.ProjectRoot,
                ["solution"] = context.SolutionPath,
                ["statusPath"] = context.StatusPath,
                ["daemon"] = daemon.DisplayPath,
                ["issues"] = issues,
                ["suggestions"] = suggestions
            };
        }

        private static async Task<JObject> EnsureDaemonAsync(CodeIndexContext context, int timeout, bool noWait)
        {
            var status = ReadStatusFile(context);
            var remote = status == null ? null : await TryGetRemoteStatusAsync(status, timeout);
            if (remote != null)
            {
                CopyStatusRuntimeFields(status, remote);
                if (noWait || IsReady(remote))
                {
                    remote["reachable"] = true;
                    return remote;
                }

                return await WaitUntilReadyAsync(status, timeout);
            }

            if (string.IsNullOrWhiteSpace(context.SolutionPath) || !File.Exists(context.SolutionPath))
            {
                return BuildFailure(context, "No .sln file was found at the project root.");
            }

            StartDaemon(context);
            var startedStatus = await WaitForStatusFileAsync(context, timeout);
            if (startedStatus == null)
            {
                return BuildFailure(context, "AIBridgeCodeIndex daemon did not write status.json before timeout.");
            }

            if (noWait)
            {
                startedStatus["reachable"] = false;
                return startedStatus;
            }

            return await WaitUntilReadyAsync(startedStatus, timeout);
        }

        private static void StartDaemon(CodeIndexContext context)
        {
            Directory.CreateDirectory(context.IndexDirectory);
            var daemon = CodeIndexDaemonExecutable.Resolve();
            if (!daemon.CanStart)
            {
                throw new FileNotFoundException("AIBridgeCodeIndex daemon was not found.", daemon.DisplayPath);
            }

            var token = Guid.NewGuid().ToString("N");
            var startInfo = daemon.CreateStartInfo();
            startInfo.WorkingDirectory = context.ProjectRoot;
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            startInfo.WindowStyle = ProcessWindowStyle.Hidden;
            daemon.AddLaunchArguments(startInfo, context, token);

            var process = Process.Start(startInfo);
            if (process == null)
            {
                throw new InvalidOperationException("Failed to start AIBridgeCodeIndex daemon.");
            }
        }

        private static async Task<JObject> WaitForStatusFileAsync(CodeIndexContext context, int timeout)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeout);
            while (DateTime.UtcNow < deadline)
            {
                var status = ReadStatusFile(context);
                if (status != null)
                {
                    return status;
                }

                await Task.Delay(100);
            }

            return null;
        }

        private static async Task<JObject> WaitUntilReadyAsync(JObject status, int timeout)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeout);
            JObject last = status;
            while (DateTime.UtcNow < deadline)
            {
                var remote = await TryGetRemoteStatusAsync(status, Math.Min(1000, timeout));
                if (remote != null)
                {
                    CopyStatusRuntimeFields(status, remote);
                    remote["reachable"] = true;
                    last = remote;
                    if (IsReady(remote) || string.Equals(remote.Value<string>("state"), "failed", StringComparison.OrdinalIgnoreCase))
                    {
                        return remote;
                    }
                }

                await Task.Delay(250);
            }

            if (last != null)
            {
                last["reachable"] = true;
            }

            return last;
        }

        private static JObject BuildStatusResult(CodeIndexContext context, JObject status, bool reachable)
        {
            if (status == null)
            {
                return new JObject
                {
                    ["success"] = true,
                    ["semantic"] = false,
                    ["source"] = "status-file",
                    ["state"] = "missing",
                    ["stale"] = true,
                    ["projectRoot"] = context.ProjectRoot,
                    ["solution"] = context.SolutionPath,
                    ["statusPath"] = context.StatusPath,
                    ["reachable"] = false
                };
            }

            return new JObject
            {
                ["success"] = true,
                ["semantic"] = IsReady(status),
                ["source"] = "status-file",
                ["state"] = status.Value<string>("state"),
                ["stale"] = !reachable || status.Value<bool?>("stale") == true,
                ["projectRoot"] = status.Value<string>("projectRoot") ?? context.ProjectRoot,
                ["solution"] = status.Value<string>("solution") ?? context.SolutionPath,
                ["loadedProjects"] = status.Value<int?>("loadedProjects") ?? 0,
                ["loadedDocuments"] = status.Value<int?>("loadedDocuments") ?? 0,
                ["endpoint"] = status.Value<string>("endpoint"),
                ["daemonPid"] = status.Value<int?>("daemonPid") ?? 0,
                ["statusPath"] = context.StatusPath,
                ["reachable"] = reachable,
                ["message"] = status.Value<string>("message")
            };
        }

        private static JObject BuildFallback(CodeIndexContext context, string action, Dictionary<string, string> options, string warning)
        {
            var query = ResolveFallbackQuery(context, action, options);
            if (string.IsNullOrWhiteSpace(query))
            {
                return BuildFailure(context, "Unable to determine fallback text query.");
            }

            string source;
            var items = TryRunRg(context.ProjectRoot, query, out source);
            if (items == null)
            {
                source = "text-fallback";
                items = RunTextFallback(context.ProjectRoot, query);
            }

            return new JObject
            {
                ["success"] = true,
                ["semantic"] = false,
                ["source"] = source,
                ["state"] = "fallback",
                ["stale"] = true,
                ["projectRoot"] = context.ProjectRoot,
                ["solution"] = context.SolutionPath,
                ["warning"] = string.IsNullOrWhiteSpace(warning) ? "Roslyn workspace is not ready. Returned text-search candidates only." : warning,
                ["items"] = JArray.FromObject(items, JsonSerializer.Create(JsonSettings))
            };
        }

        private static List<CodeIndexTextItem> TryRunRg(string projectRoot, string query, out string source)
        {
            source = "rg-fallback";
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "rg",
                    WorkingDirectory = projectRoot,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                startInfo.ArgumentList.Add("--line-number");
                startInfo.ArgumentList.Add("--column");
                startInfo.ArgumentList.Add("--glob");
                startInfo.ArgumentList.Add("*.cs");
                startInfo.ArgumentList.Add("--fixed-strings");
                startInfo.ArgumentList.Add("--");
                startInfo.ArgumentList.Add(query);
                startInfo.ArgumentList.Add(".");

                var process = Process.Start(startInfo);
                if (process == null)
                {
                    return null;
                }

                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(5000);
                if (process.ExitCode != 0 && string.IsNullOrWhiteSpace(output))
                {
                    return new List<CodeIndexTextItem>();
                }

                return ParseRgOutput(output, query);
            }
            catch
            {
                source = "text-fallback";
                return null;
            }
        }

        private static List<CodeIndexTextItem> ParseRgOutput(string output, string query)
        {
            var items = new List<CodeIndexTextItem>();
            if (string.IsNullOrEmpty(output))
            {
                return items;
            }

            var lines = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                if (items.Count >= 100)
                {
                    break;
                }

                var first = line.IndexOf(':');
                var second = first < 0 ? -1 : line.IndexOf(':', first + 1);
                var third = second < 0 ? -1 : line.IndexOf(':', second + 1);
                if (first <= 0 || second <= first || third <= second)
                {
                    continue;
                }

                int.TryParse(line.Substring(first + 1, second - first - 1), out var row);
                int.TryParse(line.Substring(second + 1, third - second - 1), out var column);
                items.Add(new CodeIndexTextItem
                {
                    kind = "text",
                    name = query,
                    file = NormalizePath(line.Substring(0, first)),
                    line = row,
                    column = column,
                    preview = TrimPreview(line.Substring(third + 1))
                });
            }

            return items;
        }

        private static List<CodeIndexTextItem> RunTextFallback(string projectRoot, string query)
        {
            var items = new List<CodeIndexTextItem>();
            foreach (var file in Directory.EnumerateFiles(projectRoot, "*.cs", SearchOption.AllDirectories))
            {
                if (items.Count >= 100)
                {
                    break;
                }

                if (ShouldSkipFallbackFile(projectRoot, file))
                {
                    continue;
                }

                var lineNumber = 0;
                foreach (var line in File.ReadLines(file))
                {
                    lineNumber++;
                    var column = line.IndexOf(query, StringComparison.OrdinalIgnoreCase);
                    if (column < 0)
                    {
                        continue;
                    }

                    items.Add(new CodeIndexTextItem
                    {
                        kind = "text",
                        name = query,
                        file = NormalizePath(MakeRelativePath(projectRoot, file)),
                        line = lineNumber,
                        column = column + 1,
                        preview = TrimPreview(line)
                    });

                    if (items.Count >= 100)
                    {
                        break;
                    }
                }
            }

            return items;
        }

        private static bool ShouldSkipFallbackFile(string projectRoot, string file)
        {
            var relative = MakeRelativePath(projectRoot, file).Replace('\\', '/');
            return relative.StartsWith(".git/", StringComparison.OrdinalIgnoreCase)
                || relative.StartsWith("Library/", StringComparison.OrdinalIgnoreCase)
                || relative.StartsWith("Temp/", StringComparison.OrdinalIgnoreCase)
                || relative.StartsWith("obj/", StringComparison.OrdinalIgnoreCase)
                || relative.StartsWith("bin/", StringComparison.OrdinalIgnoreCase)
                || relative.StartsWith(".aibridge/code-index/", StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveFallbackQuery(CodeIndexContext context, string action, Dictionary<string, string> options)
        {
            if (string.Equals(action, "symbol", StringComparison.OrdinalIgnoreCase)
                && options.TryGetValue("query", out var query))
            {
                return query;
            }

            if (!options.TryGetValue("file", out var file)
                || !options.TryGetValue("line", out var lineText)
                || !options.TryGetValue("column", out var columnText)
                || !int.TryParse(lineText, out var line)
                || !int.TryParse(columnText, out var column))
            {
                return null;
            }

            var fullPath = Path.IsPathRooted(file) ? file : Path.Combine(context.ProjectRoot, file);
            if (!File.Exists(fullPath))
            {
                return null;
            }

            var sourceLine = File.ReadLines(fullPath).Skip(Math.Max(0, line - 1)).FirstOrDefault();
            if (sourceLine == null)
            {
                return null;
            }

            var index = Math.Max(0, Math.Min(column - 1, Math.Max(0, sourceLine.Length - 1)));
            if (!IsIdentifierChar(sourceLine[index]))
            {
                return null;
            }

            var start = index;
            while (start > 0 && IsIdentifierChar(sourceLine[start - 1]))
            {
                start--;
            }

            var end = index;
            while (end + 1 < sourceLine.Length && IsIdentifierChar(sourceLine[end + 1]))
            {
                end++;
            }

            return sourceLine.Substring(start, end - start + 1);
        }

        private static bool IsIdentifierChar(char value)
        {
            return char.IsLetterOrDigit(value) || value == '_';
        }

        private static async Task<JObject> TryGetRemoteStatusAsync(JObject status, int timeout)
        {
            if (status == null)
            {
                return null;
            }

            var endpoint = status.Value<string>("endpoint");
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                return null;
            }

            try
            {
                return await GetJsonAsync(endpoint, "status", status.Value<string>("token"), Math.Min(timeout, 1500));
            }
            catch
            {
                return null;
            }
        }

        private static async Task<JObject> GetJsonAsync(string endpoint, string path, string token, int timeout)
        {
            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromMilliseconds(Math.Max(500, timeout));
                var request = new HttpRequestMessage(HttpMethod.Get, CombineUrl(endpoint, path));
                AddToken(request, token);
                var response = await client.SendAsync(request);
                var body = await response.Content.ReadAsStringAsync();
                return string.IsNullOrWhiteSpace(body) ? new JObject() : JObject.Parse(body);
            }
        }

        private static async Task<JObject> PostJsonAsync(string endpoint, string path, string token, JObject payload, int timeout)
        {
            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromMilliseconds(Math.Max(500, timeout));
                var request = new HttpRequestMessage(HttpMethod.Post, CombineUrl(endpoint, path));
                AddToken(request, token);
                request.Content = new StringContent(payload.ToString(Formatting.None), Encoding.UTF8, "application/json");
                var response = await client.SendAsync(request);
                var body = await response.Content.ReadAsStringAsync();
                return string.IsNullOrWhiteSpace(body) ? new JObject() : JObject.Parse(body);
            }
        }

        private static void AddToken(HttpRequestMessage request, string token)
        {
            if (!string.IsNullOrEmpty(token))
            {
                request.Headers.TryAddWithoutValidation("X-AIBridge-CodeIndex-Token", token);
            }
        }

        private static string CombineUrl(string endpoint, string path)
        {
            return endpoint.TrimEnd('/') + "/" + path.TrimStart('/');
        }

        private static JObject ReadStatusFile(CodeIndexContext context)
        {
            if (!File.Exists(context.StatusPath))
            {
                return null;
            }

            try
            {
                return JObject.Parse(File.ReadAllText(context.StatusPath, Encoding.UTF8));
            }
            catch
            {
                return null;
            }
        }

        private static void CopyStatusRuntimeFields(JObject status, JObject remote)
        {
            if (status == null || remote == null)
            {
                return;
            }

            remote["endpoint"] = status.Value<string>("endpoint");
            remote["token"] = status.Value<string>("token");
            remote["daemonPid"] = status.Value<int?>("daemonPid") ?? 0;
        }

        private static bool IsReady(JObject status)
        {
            return status != null && string.Equals(status.Value<string>("state"), "ready", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsFallbackEnabled(Dictionary<string, string> options)
        {
            return !options.TryGetValue("fallback", out var value)
                   || !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
        }

        private static Dictionary<string, object> BuildQueryParameters(string action, Dictionary<string, string> options)
        {
            var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            if (string.Equals(action, "symbol", StringComparison.OrdinalIgnoreCase))
            {
                result["query"] = options["query"];
                return result;
            }

            result["file"] = options["file"];
            result["line"] = int.Parse(options["line"]);
            result["column"] = int.Parse(options["column"]);
            return result;
        }

        private static void ValidateQueryArguments(string action, Dictionary<string, string> options)
        {
            if (string.Equals(action, "symbol", StringComparison.OrdinalIgnoreCase))
            {
                if (!options.TryGetValue("query", out var query) || string.IsNullOrWhiteSpace(query))
                {
                    throw new ArgumentException("Missing required parameter: --query");
                }

                return;
            }

            foreach (var key in new[] { "file", "line", "column" })
            {
                if (!options.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
                {
                    throw new ArgumentException("Missing required parameter: --" + key);
                }
            }
        }

        private static JObject BuildFailure(CodeIndexContext context, string error)
        {
            return new JObject
            {
                ["success"] = false,
                ["semantic"] = false,
                ["source"] = "code_index",
                ["state"] = "failed",
                ["stale"] = true,
                ["projectRoot"] = context == null ? null : context.ProjectRoot,
                ["solution"] = context == null ? null : context.SolutionPath,
                ["error"] = error
            };
        }

        private static void Print(JObject result, OutputMode outputMode)
        {
            if (outputMode == OutputMode.Quiet && !result.Value<bool>("success"))
            {
                Console.Error.WriteLine(result.Value<string>("error") ?? "code_index failed");
                return;
            }

            var formatting = outputMode == OutputMode.Pretty ? Formatting.Indented : Formatting.None;
            Console.WriteLine(JsonConvert.SerializeObject(result, formatting, JsonSettings));
        }

        private static string MakeRelativePath(string root, string path)
        {
            var rootUri = new Uri(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar);
            var fileUri = new Uri(Path.GetFullPath(path));
            return Uri.UnescapeDataString(rootUri.MakeRelativeUri(fileUri).ToString()).Replace('/', Path.DirectorySeparatorChar);
        }

        private static string NormalizePath(string path)
        {
            return (path ?? string.Empty).Replace('\\', '/').TrimStart('.', '/');
        }

        private static string TrimPreview(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return string.Empty;
            }

            var trimmed = line.Trim();
            return trimmed.Length <= 300 ? trimmed : trimmed.Substring(0, 300);
        }

        private sealed class CodeIndexContext
        {
            public string ProjectRoot { get; private set; }
            public string SolutionPath { get; private set; }
            public string IndexDirectory { get; private set; }
            public string StatusPath { get; private set; }
            public bool HasUnityProjectMarkers { get; private set; }

            public static CodeIndexContext Resolve(Dictionary<string, string> options)
            {
                var projectRoot = ResolveProjectRoot(options);
                var solutionPath = ResolveSolutionPath(projectRoot, options);
                var indexDirectory = Path.Combine(projectRoot, ".aibridge", IndexDirectoryName);
                return new CodeIndexContext
                {
                    ProjectRoot = projectRoot,
                    SolutionPath = solutionPath,
                    IndexDirectory = indexDirectory,
                    StatusPath = Path.Combine(indexDirectory, "status.json"),
                    HasUnityProjectMarkers = Directory.Exists(Path.Combine(projectRoot, "Assets"))
                                             && File.Exists(Path.Combine(projectRoot, "ProjectSettings", "ProjectSettings.asset"))
                };
            }

            private static string ResolveProjectRoot(Dictionary<string, string> options)
            {
                if (options.TryGetValue("project-root", out var explicitRoot) && !string.IsNullOrWhiteSpace(explicitRoot))
                {
                    return Path.GetFullPath(explicitRoot);
                }

                var environmentRoot = Environment.GetEnvironmentVariable("UNITY_PROJECT_ROOT");
                if (!string.IsNullOrWhiteSpace(environmentRoot))
                {
                    return Path.GetFullPath(environmentRoot);
                }

                var unityRoot = FindUnityProjectRoot(Directory.GetCurrentDirectory());
                return string.IsNullOrWhiteSpace(unityRoot)
                    ? Directory.GetCurrentDirectory()
                    : unityRoot;
            }

            private static string ResolveSolutionPath(string projectRoot, Dictionary<string, string> options)
            {
                if (options.TryGetValue("solution", out var explicitSolution) && !string.IsNullOrWhiteSpace(explicitSolution))
                {
                    return Path.IsPathRooted(explicitSolution)
                        ? Path.GetFullPath(explicitSolution)
                        : Path.GetFullPath(Path.Combine(projectRoot, explicitSolution));
                }

                var solutions = Directory.GetFiles(projectRoot, "*.sln", SearchOption.TopDirectoryOnly);
                if (solutions.Length == 0)
                {
                    return null;
                }

                var projectName = new DirectoryInfo(projectRoot).Name;
                var matching = solutions.FirstOrDefault(path => string.Equals(Path.GetFileNameWithoutExtension(path), projectName, StringComparison.OrdinalIgnoreCase));
                return matching ?? solutions.OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
            }

            private static string FindUnityProjectRoot(string startDirectory)
            {
                var directory = startDirectory;
                while (!string.IsNullOrEmpty(directory))
                {
                    if (Directory.Exists(Path.Combine(directory, "Assets"))
                        && File.Exists(Path.Combine(directory, "ProjectSettings", "ProjectSettings.asset")))
                    {
                        return directory;
                    }

                    directory = Path.GetDirectoryName(directory);
                }

                return null;
            }
        }

        private sealed class CodeIndexDaemonExecutable
        {
            private enum DaemonLaunchMode
            {
                Executable,
                Dll,
                SourceProject
            }

            private DaemonLaunchMode _mode;
            private string _path;

            public bool CanStart { get { return !string.IsNullOrEmpty(_path); } }
            public string DisplayPath { get { return _path; } }

            public static CodeIndexDaemonExecutable Resolve()
            {
                var executableName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? DaemonAssemblyName + ".exe"
                    : DaemonAssemblyName;
                var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
                var executablePath = Path.Combine(baseDirectory, DaemonDirectoryName, executableName);
                if (File.Exists(executablePath))
                {
                    return new CodeIndexDaemonExecutable { _mode = DaemonLaunchMode.Executable, _path = executablePath };
                }

                var dllPath = Path.Combine(baseDirectory, DaemonDirectoryName, DaemonAssemblyName + ".dll");
                if (File.Exists(dllPath))
                {
                    return new CodeIndexDaemonExecutable { _mode = DaemonLaunchMode.Dll, _path = dllPath };
                }

                var sourceProject = FindSourceProject(baseDirectory) ?? FindSourceProject(Directory.GetCurrentDirectory());
                return new CodeIndexDaemonExecutable { _mode = DaemonLaunchMode.SourceProject, _path = sourceProject };
            }

            public ProcessStartInfo CreateStartInfo()
            {
                if (_mode == DaemonLaunchMode.Executable)
                {
                    return new ProcessStartInfo { FileName = _path };
                }

                var startInfo = new ProcessStartInfo { FileName = "dotnet" };
                if (_mode == DaemonLaunchMode.Dll)
                {
                    startInfo.ArgumentList.Add(_path);
                }
                else
                {
                    startInfo.ArgumentList.Add("run");
                    startInfo.ArgumentList.Add("--project");
                    startInfo.ArgumentList.Add(_path);
                    startInfo.ArgumentList.Add("--no-launch-profile");
                    startInfo.ArgumentList.Add("--");
                }

                return startInfo;
            }

            public void AddLaunchArguments(ProcessStartInfo startInfo, CodeIndexContext context, string token)
            {
                startInfo.ArgumentList.Add("--project-root");
                startInfo.ArgumentList.Add(context.ProjectRoot);
                startInfo.ArgumentList.Add("--status-path");
                startInfo.ArgumentList.Add(context.StatusPath);
                startInfo.ArgumentList.Add("--token");
                startInfo.ArgumentList.Add(token);
                if (!string.IsNullOrWhiteSpace(context.SolutionPath))
                {
                    startInfo.ArgumentList.Add("--solution");
                    startInfo.ArgumentList.Add(context.SolutionPath);
                }
            }

            private static string FindSourceProject(string startDirectory)
            {
                var directory = startDirectory;
                while (!string.IsNullOrEmpty(directory))
                {
                    var candidate = Path.Combine(directory, "Tools~", "AIBridgeCodeIndex", "AIBridgeCodeIndex.csproj");
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }

                    if (string.Equals(Path.GetFileName(directory), "Tools~", StringComparison.OrdinalIgnoreCase))
                    {
                        candidate = Path.Combine(directory, "AIBridgeCodeIndex", "AIBridgeCodeIndex.csproj");
                        if (File.Exists(candidate))
                        {
                            return candidate;
                        }
                    }

                    directory = Path.GetDirectoryName(directory);
                }

                return null;
            }
        }

        private sealed class CodeIndexTextItem
        {
            public string kind { get; set; }
            public string name { get; set; }
            public string file { get; set; }
            public int line { get; set; }
            public int column { get; set; }
            public string preview { get; set; }
        }
    }
}
