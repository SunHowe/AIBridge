using System;
using System.IO;
using System.Text;
using System.Threading;
using AIBridgeCLI.Commands;
using Newtonsoft.Json;

namespace AIBridgeCLI.Core
{
    /// <summary>
    /// Handles sending commands and receiving results
    /// </summary>
    public class CommandSender
    {
        private readonly string _commandsDir;
        private readonly string _resultsDir;
        private readonly int _timeout;
        private readonly int _pollInterval;
        private readonly string _onDialog;

        /// <summary>
        /// Create a new CommandSender
        /// </summary>
        /// <param name="timeout">Timeout in milliseconds (default: 5000)</param>
        /// <param name="pollInterval">Poll interval in milliseconds (default: 50)</param>
        public CommandSender(int timeout = 5000, string onDialog = null, int pollInterval = 50)
        {
            _commandsDir = PathHelper.GetCommandsDirectory();
            _resultsDir = PathHelper.GetResultsDirectory();
            _timeout = timeout;
            _onDialog = onDialog;
            _pollInterval = pollInterval;

            PathHelper.EnsureDirectoriesExist();
        }

        /// <summary>
        /// Send a command and wait for result
        /// </summary>
        public CommandResult SendCommand(CommandRequest request)
        {
            if (string.IsNullOrEmpty(request.id))
            {
                request.id = PathHelper.GenerateCommandId();
            }

            var preflightDialogDiagnostic = HandleBlockingDialog(isPreflight: true);
            if (preflightDialogDiagnostic != null)
            {
                return new CommandResult
                {
                    id = request.id,
                    success = false,
                    error = preflightDialogDiagnostic.Error,
                    data = preflightDialogDiagnostic.Data
                };
            }

            var commandFile = Path.Combine(_commandsDir, $"{request.id}.json");
            var resultFile = Path.Combine(_resultsDir, $"{request.id}.json");

            // Write command file with UTF-8 encoding (no BOM)
            var json = JsonConvert.SerializeObject(request, Formatting.None);
            File.WriteAllText(commandFile, json, new UTF8Encoding(false));

            // Wait for result
            var startTime = DateTime.Now;
            while ((DateTime.Now - startTime).TotalMilliseconds < _timeout)
            {
                if (File.Exists(resultFile))
                {
                    // Small delay to ensure file is fully written
                    Thread.Sleep(10);

                    try
                    {
                        var resultJson = File.ReadAllText(resultFile, Encoding.UTF8);
                        var result = JsonConvert.DeserializeObject<CommandResult>(resultJson);

                        // Clean up result file
                        try { File.Delete(resultFile); } catch { }

                        return result;
                    }
                    catch (IOException)
                    {
                        // File might still be locked, retry
                        Thread.Sleep(_pollInterval);
                        continue;
                    }
                }

                Thread.Sleep(_pollInterval);
            }

            // Timeout - clean up command file if still exists
            var dialogDiagnostic = HandleBlockingDialog(isPreflight: false);
            if (dialogDiagnostic != null)
            {
                return new CommandResult
                {
                    id = request.id,
                    success = false,
                    error = dialogDiagnostic.Error,
                    data = dialogDiagnostic.Data
                };
            }

            try { File.Delete(commandFile); } catch { }

            return new CommandResult
            {
                id = request.id,
                success = false,
                error = $"Timeout waiting for result after {_timeout}ms. Make sure Unity Editor is running and AIBridge is active."
            };
        }

        /// <summary>
        /// Send a command without waiting for result
        /// </summary>
        public string SendCommandNoWait(CommandRequest request)
        {
            return TrySendCommandNoWait(request).id;
        }

        public CommandResult TrySendCommandNoWait(CommandRequest request)
        {
            if (string.IsNullOrEmpty(request.id))
            {
                request.id = PathHelper.GenerateCommandId();
            }

            var preflightDialogDiagnostic = HandleBlockingDialog(isPreflight: true);
            if (preflightDialogDiagnostic != null)
            {
                return new CommandResult
                {
                    id = request.id,
                    success = false,
                    error = preflightDialogDiagnostic.Error,
                    data = preflightDialogDiagnostic.Data
                };
            }

            var commandFile = Path.Combine(_commandsDir, $"{request.id}.json");

            // Write command file with UTF-8 encoding (no BOM)
            var json = JsonConvert.SerializeObject(request, Formatting.None);
            File.WriteAllText(commandFile, json, new UTF8Encoding(false));

            return new CommandResult
            {
                id = request.id,
                success = true,
                data = new
                {
                    id = request.id,
                    status = "sent"
                }
            };
        }

        /// <summary>
        /// Check if a result is available for a given command ID
        /// </summary>
        public CommandResult TryGetResult(string commandId)
        {
            var resultFile = Path.Combine(_resultsDir, $"{commandId}.json");

            if (!File.Exists(resultFile))
            {
                return null;
            }

            try
            {
                var resultJson = File.ReadAllText(resultFile, Encoding.UTF8);
                var result = JsonConvert.DeserializeObject<CommandResult>(resultJson);

                // Clean up result file
                try { File.Delete(resultFile); } catch { }

                return result;
            }
            catch
            {
                return null;
            }
        }

        private BlockingDialogDiagnostic HandleBlockingDialog(bool isPreflight)
        {
            var status = DialogService.GetStatus();
            if (status == null)
            {
                return null;
            }

            if (!status.success)
            {
                if (isPreflight)
                {
                    return null;
                }

                if (string.Equals(status.errorCode, "macos_accessibility_permission_required", StringComparison.OrdinalIgnoreCase))
                {
                    // macOS 没有辅助功能权限时，先保留命令文件，避免用户授权后原请求丢失。
                    return new BlockingDialogDiagnostic
                    {
                        Error = "Unity did not respond, and dialog inspection requires macOS Accessibility permission.",
                        Data = status
                    };
                }

                return null;
            }

            if (!DialogService.HasBlockingDialog(status))
            {
                return null;
            }

            // 检测到模态弹窗时，不删除原始命令文件，避免关闭弹窗后请求丢失。
            var normalizedAction = DialogService.NormalizeChoice(_onDialog);
            if (string.IsNullOrWhiteSpace(normalizedAction) || normalizedAction == "none")
            {
                return new BlockingDialogDiagnostic
                {
                    Error = "Unity is blocked by a modal dialog.",
                    Data = status
                };
            }

            if (normalizedAction == "wait")
            {
                return new BlockingDialogDiagnostic
                {
                    Error = "Unity is blocked by a modal dialog.",
                    Data = new
                    {
                        dialog = status,
                        wait = DialogService.Wait(_timeout)
                    }
                };
            }

            var click = DialogService.Click(normalizedAction, null, null);
            return new BlockingDialogDiagnostic
            {
                Error = "Unity is blocked by a modal dialog.",
                Data = new
                {
                    dialog = status,
                    click = click
                }
            };
        }

        private class BlockingDialogDiagnostic
        {
            public string Error { get; set; }
            public object Data { get; set; }
        }
    }
}
