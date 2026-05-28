using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace AIBridgeCodeIndex
{
    internal sealed class CodeIndexServer
    {
        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore
        };

        private readonly CodeIndexOptions _options;
        private readonly object _statusLock = new object();
        private readonly CodeIndexWorkspace _workspace;
        private TcpListener _listener;
        private CodeIndexStatus _status;
        private Task _warmupTask;
        private bool _shutdownRequested;

        public CodeIndexServer(CodeIndexOptions options)
        {
            _options = options;
            _workspace = new CodeIndexWorkspace(options.ProjectRoot, options.SolutionPath);
        }

        public async Task RunAsync()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();

            var endpoint = "http://127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port;
            _status = CreateInitialStatus(endpoint);
            WriteStatus();

            _warmupTask = WarmupAsync();

            while (!_shutdownRequested)
            {
                TcpClient client = null;
                try
                {
                    client = await _listener.AcceptTcpClientAsync();
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (SocketException)
                {
                    if (_shutdownRequested)
                    {
                        break;
                    }

                    throw;
                }

                _ = Task.Run(() => HandleClientAsync(client));
            }

            if (_warmupTask != null)
            {
                await Task.WhenAny(_warmupTask, Task.Delay(500));
            }
        }

        private async Task WarmupAsync()
        {
            try
            {
                UpdateStatus("loading", null);
                await _workspace.WarmupAsync();

                lock (_statusLock)
                {
                    _status.state = "ready";
                    _status.solution = _workspace.SolutionPath;
                    _status.loadedProjects = _workspace.LoadedProjects;
                    _status.loadedDocuments = _workspace.LoadedDocuments;
                    _status.stale = false;
                    _status.message = null;
                    _status.updatedAt = DateTimeOffset.Now.ToString("o");
                }

                WriteStatus();
            }
            catch (Exception ex)
            {
                lock (_statusLock)
                {
                    _status.state = "failed";
                    _status.solution = _workspace.SolutionPath;
                    _status.loadedProjects = _workspace.LoadedProjects;
                    _status.loadedDocuments = _workspace.LoadedDocuments;
                    _status.stale = true;
                    _status.message = ex.Message;
                    _status.updatedAt = DateTimeOffset.Now.ToString("o");
                }

                WriteStatus();
                Log("Warmup failed: " + ex);
            }
        }

        private async Task HandleClientAsync(TcpClient client)
        {
            using (client)
            {
                var stream = client.GetStream();
                try
                {
                    var request = await ReadRequestAsync(stream);
                    if (request == null)
                    {
                        return;
                    }

                    if (!IsAuthorized(request))
                    {
                        await WriteResponseAsync(stream, 403, new { success = false, error = "Forbidden" });
                        return;
                    }

                    if (request.Method == "GET" && request.Path == "/status")
                    {
                        await WriteResponseAsync(stream, 200, CodeIndexResponse.FromStatus(GetStatusSnapshot()));
                        return;
                    }

                    if (request.Method == "POST" && request.Path == "/query")
                    {
                        var query = JsonConvert.DeserializeObject<CodeIndexRequest>(request.BodyText);
                        var response = await ExecuteQueryAsync(query);
                        await WriteResponseAsync(stream, response.success ? 200 : 409, response);
                        return;
                    }

                    if (request.Method == "POST" && request.Path == "/shutdown")
                    {
                        UpdateStatus("stopping", null);
                        await WriteResponseAsync(stream, 200, CodeIndexResponse.FromStatus(GetStatusSnapshot()));
                        _shutdownRequested = true;
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(100);
                            _listener.Stop();
                        });
                        return;
                    }

                    await WriteResponseAsync(stream, 404, new { success = false, error = "Not found" });
                }
                catch (Exception ex)
                {
                    Log("Request failed: " + ex);
                    await WriteResponseAsync(stream, 500, new { success = false, error = ex.Message });
                }
            }
        }

        private async Task<CodeIndexResponse> ExecuteQueryAsync(CodeIndexRequest query)
        {
            var status = GetStatusSnapshot();
            if (query == null || string.IsNullOrWhiteSpace(query.action))
            {
                return BuildFailure(status, "Missing action.");
            }

            if (!string.Equals(status.state, "ready", StringComparison.OrdinalIgnoreCase))
            {
                return BuildFailure(status, "Roslyn workspace is not ready. Current state: " + status.state);
            }

            var response = await _workspace.QueryAsync(query.action, query.parameters);
            response.success = true;
            response.semantic = true;
            response.source = "roslyn-msbuild";
            response.state = status.state;
            response.stale = status.stale;
            response.projectRoot = status.projectRoot;
            response.solution = _workspace.SolutionPath;
            response.loadedProjects = _workspace.LoadedProjects;
            response.loadedDocuments = _workspace.LoadedDocuments;
            return response;
        }

        private static CodeIndexResponse BuildFailure(CodeIndexStatus status, string error)
        {
            return new CodeIndexResponse
            {
                success = false,
                semantic = false,
                source = "roslyn-msbuild",
                state = status == null ? "unknown" : status.state,
                stale = true,
                projectRoot = status == null ? null : status.projectRoot,
                solution = status == null ? null : status.solution,
                loadedProjects = status == null ? 0 : status.loadedProjects,
                loadedDocuments = status == null ? 0 : status.loadedDocuments,
                error = error
            };
        }

        private CodeIndexStatus CreateInitialStatus(string endpoint)
        {
            var now = DateTimeOffset.Now.ToString("o");
            return new CodeIndexStatus
            {
                projectRoot = _options.ProjectRoot,
                projectHash = ComputeProjectHash(_options.ProjectRoot),
                unityPid = _options.UnityPid,
                daemonPid = Process.GetCurrentProcess().Id,
                endpoint = endpoint,
                token = _options.Token,
                state = "starting",
                stale = true,
                solution = _workspace.SolutionPath,
                startedAt = now,
                updatedAt = now
            };
        }

        private void UpdateStatus(string state, string message)
        {
            lock (_statusLock)
            {
                _status.state = state;
                _status.message = message;
                _status.updatedAt = DateTimeOffset.Now.ToString("o");
            }

            WriteStatus();
        }

        private CodeIndexStatus GetStatusSnapshot()
        {
            lock (_statusLock)
            {
                return new CodeIndexStatus
                {
                    projectRoot = _status.projectRoot,
                    projectHash = _status.projectHash,
                    unityPid = _status.unityPid,
                    daemonPid = _status.daemonPid,
                    endpoint = _status.endpoint,
                    token = _status.token,
                    state = _status.state,
                    stale = _status.stale,
                    solution = _status.solution,
                    loadedProjects = _status.loadedProjects,
                    loadedDocuments = _status.loadedDocuments,
                    startedAt = _status.startedAt,
                    updatedAt = _status.updatedAt,
                    message = _status.message
                };
            }
        }

        private void WriteStatus()
        {
            if (string.IsNullOrWhiteSpace(_options.StatusPath))
            {
                return;
            }

            try
            {
                var directory = Path.GetDirectoryName(_options.StatusPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                    var lockPath = Path.Combine(directory, "lock.json");
                    File.WriteAllText(lockPath, JsonConvert.SerializeObject(GetStatusSnapshot(), Formatting.Indented, JsonSettings), Encoding.UTF8);
                }

                File.WriteAllText(_options.StatusPath, JsonConvert.SerializeObject(GetStatusSnapshot(), Formatting.Indented, JsonSettings), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Log("Failed to write status: " + ex.Message);
            }
        }

        private bool IsAuthorized(HttpRequestData request)
        {
            if (string.IsNullOrEmpty(_options.Token))
            {
                return true;
            }

            return request.Headers.TryGetValue("X-AIBridge-CodeIndex-Token", out var token)
                && string.Equals(token, _options.Token, StringComparison.Ordinal);
        }

        private async Task<HttpRequestData> ReadRequestAsync(NetworkStream stream)
        {
            var buffer = new byte[4096];
            var memory = new MemoryStream();
            var headerEnd = -1;

            while (headerEnd < 0)
            {
                var read = await stream.ReadAsync(buffer, 0, buffer.Length);
                if (read <= 0)
                {
                    return null;
                }

                memory.Write(buffer, 0, read);
                if (memory.Length > 65536)
                {
                    throw new InvalidOperationException("HTTP header is too large.");
                }

                headerEnd = FindHeaderEnd(memory.GetBuffer(), (int)memory.Length);
            }

            var bytes = memory.ToArray();
            var headerText = Encoding.ASCII.GetString(bytes, 0, headerEnd);
            var lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None);
            if (lines.Length == 0)
            {
                return null;
            }

            var requestLine = lines[0].Split(' ');
            if (requestLine.Length < 2)
            {
                return null;
            }

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 1; i < lines.Length; i++)
            {
                var line = lines[i];
                var colon = line.IndexOf(':');
                if (colon <= 0)
                {
                    continue;
                }

                headers[line.Substring(0, colon).Trim()] = line.Substring(colon + 1).Trim();
            }

            var contentLength = 0;
            if (headers.TryGetValue("Content-Length", out var contentLengthText))
            {
                int.TryParse(contentLengthText, out contentLength);
            }

            var bodyOffset = headerEnd + 4;
            var body = new MemoryStream();
            if (bytes.Length > bodyOffset)
            {
                body.Write(bytes, bodyOffset, bytes.Length - bodyOffset);
            }

            while (body.Length < contentLength)
            {
                var remaining = Math.Min(buffer.Length, contentLength - (int)body.Length);
                var read = await stream.ReadAsync(buffer, 0, remaining);
                if (read <= 0)
                {
                    break;
                }

                body.Write(buffer, 0, read);
            }

            return new HttpRequestData
            {
                Method = requestLine[0].ToUpperInvariant(),
                Path = requestLine[1],
                Headers = headers,
                BodyText = Encoding.UTF8.GetString(body.ToArray())
            };
        }

        private static int FindHeaderEnd(byte[] bytes, int length)
        {
            for (var i = 3; i < length; i++)
            {
                if (bytes[i - 3] == '\r'
                    && bytes[i - 2] == '\n'
                    && bytes[i - 1] == '\r'
                    && bytes[i] == '\n')
                {
                    return i - 3;
                }
            }

            return -1;
        }

        private static async Task WriteResponseAsync(NetworkStream stream, int statusCode, object body)
        {
            var statusText = statusCode == 200 ? "OK" : statusCode == 403 ? "Forbidden" : statusCode == 404 ? "Not Found" : "Error";
            var json = JsonConvert.SerializeObject(body, Formatting.None, JsonSettings);
            var bodyBytes = Encoding.UTF8.GetBytes(json);
            var header = "HTTP/1.1 " + statusCode + " " + statusText + "\r\n"
                         + "Content-Type: application/json; charset=utf-8\r\n"
                         + "Content-Length: " + bodyBytes.Length + "\r\n"
                         + "Connection: close\r\n\r\n";
            var headerBytes = Encoding.ASCII.GetBytes(header);
            await stream.WriteAsync(headerBytes, 0, headerBytes.Length);
            await stream.WriteAsync(bodyBytes, 0, bodyBytes.Length);
        }

        private void Log(string message)
        {
            try
            {
                var statusDirectory = string.IsNullOrEmpty(_options.StatusPath) ? null : Path.GetDirectoryName(_options.StatusPath);
                if (string.IsNullOrEmpty(statusDirectory))
                {
                    return;
                }

                var logDirectory = Path.Combine(statusDirectory, "logs");
                Directory.CreateDirectory(logDirectory);
                File.AppendAllText(Path.Combine(logDirectory, "daemon.log"), DateTimeOffset.Now.ToString("o") + " " + message + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
            }
        }

        private static string ComputeProjectHash(string projectRoot)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(projectRoot.ToLowerInvariant()));
                return BitConverter.ToString(bytes, 0, 4).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private sealed class HttpRequestData
        {
            public string Method { get; set; }
            public string Path { get; set; }
            public Dictionary<string, string> Headers { get; set; }
            public string BodyText { get; set; }
        }
    }
}
