using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AIBridgeCLI.Core
{
    public sealed class AdbRuntimeTransportClient : IRuntimeTransportClient
    {
        private const string TransportName = "adb";
        private const string CheckPassed = "passed";
        private const string CheckFailed = "failed";
        private const string CheckSkipped = "skipped";
        private const string ExistsMarker = "__aibridge_exists__";
        private const int ArtifactPullTimeoutMs = 30000;

        private static readonly TimeSpan StaleHeartbeatTimeout = TimeSpan.FromSeconds(15);

        private readonly RuntimeTransportOptions _options;
        private readonly Dictionary<string, PendingRequest> _pendingRequests = new Dictionary<string, PendingRequest>(StringComparer.OrdinalIgnoreCase);

        public AdbRuntimeTransportClient(RuntimeTransportOptions options)
        {
            _options = options;
        }

        public RuntimeTransportKind Kind => RuntimeTransportKind.Adb;

        public IReadOnlyList<RuntimeTargetInfo> ListTargets()
        {
            var preparation = PrepareState(null);
            if (!preparation.Success)
            {
                return new List<RuntimeTargetInfo>();
            }

            return ListTargets(preparation.State);
        }

        public RuntimeTargetInfo ResolveTarget(string target)
        {
            var preparation = PrepareState(null);
            if (!preparation.Success)
            {
                return CreateUnavailableTarget(target);
            }

            return ResolveTarget(preparation.State, target);
        }

        public RuntimeSendResult Send(RuntimeTargetInfo target, CommandRequest request)
        {
            var preparation = PrepareState(null);
            if (!preparation.Success)
            {
                return new RuntimeSendResult
                {
                    Success = false,
                    Error = preparation.Error
                };
            }

            if (target == null || string.IsNullOrWhiteSpace(target.commandsPath) || string.IsNullOrWhiteSpace(target.resultsPath))
            {
                return new RuntimeSendResult
                {
                    Success = false,
                    Error = "target_not_found: ADB runtime target was not found."
                };
            }

            try
            {
                var state = preparation.State;
                var mkdirResult = state.Client.Shell(
                    "mkdir -p " + AdbClient.ShellQuote(target.commandsPath) + " " + AdbClient.ShellQuote(target.resultsPath),
                    GetOperationTimeout(_options.TimeoutMs));
                if (!mkdirResult.Success)
                {
                    return new RuntimeSendResult
                    {
                        Success = false,
                        Error = "path_not_writable: Failed to create adb runtime directories: " + mkdirResult.GetErrorText()
                    };
                }

                var commandFile = CombineDevicePath(target.commandsPath, request.id + ".json");
                var tempCommandFile = commandFile + ".tmp";
                var localCommandFile = Path.Combine(GetCacheDirectory(state.DeviceSerial, target.targetId, "commands"), request.id + ".json");
                Directory.CreateDirectory(Path.GetDirectoryName(localCommandFile));

                var json = JsonConvert.SerializeObject(request, Formatting.None, new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore
                });
                File.WriteAllText(localCommandFile, json, new UTF8Encoding(false));

                var pushResult = state.Client.Push(localCommandFile, tempCommandFile, GetOperationTimeout(_options.TimeoutMs));
                if (!pushResult.Success)
                {
                    return new RuntimeSendResult
                    {
                        Success = false,
                        Error = "transport_unavailable: adb push failed: " + pushResult.GetErrorText()
                    };
                }

                // Android 侧也采用临时文件再改名，避免 Runtime 读到半截命令。
                var moveResult = state.Client.Shell(
                    "mv -f " + AdbClient.ShellQuote(tempCommandFile) + " " + AdbClient.ShellQuote(commandFile),
                    GetOperationTimeout(_options.TimeoutMs));
                if (!moveResult.Success)
                {
                    return new RuntimeSendResult
                    {
                        Success = false,
                        Error = "path_not_writable: Failed to publish adb command file: " + moveResult.GetErrorText()
                    };
                }

                _pendingRequests[request.id] = new PendingRequest
                {
                    Action = RuntimePathHelper.GetRuntimeAction(request),
                    OutputPath = TryGetRequestParam(request, "output"),
                    TargetId = target.targetId,
                    DeviceSerial = state.DeviceSerial
                };

                return new RuntimeSendResult
                {
                    Success = true,
                    CommandPath = commandFile
                };
            }
            catch (Exception ex)
            {
                return new RuntimeSendResult
                {
                    Success = false,
                    Error = "transport_unavailable: " + ex.Message
                };
            }
        }

        public RuntimeReceiveResult WaitResult(RuntimeTargetInfo target, string commandId, int timeoutMs, int pollIntervalMs)
        {
            var preparation = PrepareState(null);
            if (!preparation.Success)
            {
                return new RuntimeReceiveResult
                {
                    Success = false,
                    Error = preparation.Error
                };
            }

            var state = preparation.State;
            var resultFile = CombineDevicePath(target.resultsPath, commandId + ".json");
            var startTime = DateTime.UtcNow;
            while ((DateTime.UtcNow - startTime).TotalMilliseconds < timeoutMs)
            {
                var existsResult = state.Client.Shell(
                    "if [ -f " + AdbClient.ShellQuote(resultFile) + " ]; then echo " + ExistsMarker + "; fi",
                    GetOperationTimeout(pollIntervalMs * 4));
                if (!existsResult.Success)
                {
                    return new RuntimeReceiveResult
                    {
                        Success = false,
                        Error = "transport_unavailable: adb result polling failed: " + existsResult.GetErrorText()
                    };
                }

                if (ContainsLine(existsResult.StandardOutput, ExistsMarker))
                {
                    var localResultFile = Path.Combine(GetCacheDirectory(state.DeviceSerial, target.targetId, "results"), commandId + ".json");
                    var pullResult = state.Client.Pull(resultFile, localResultFile, GetOperationTimeout(timeoutMs));
                    if (!pullResult.Success)
                    {
                        return new RuntimeReceiveResult
                        {
                            Success = false,
                            Error = "transport_unavailable: adb result pull failed: " + pullResult.GetErrorText()
                        };
                    }

                    try
                    {
                        var resultJson = File.ReadAllText(localResultFile, Encoding.UTF8);
                        var result = RuntimeResultParser.Parse(commandId, resultJson);
                        state.Client.RemoveFile(resultFile);
                        TryPullArtifacts(state, target, commandId, result);
                        _pendingRequests.Remove(commandId);
                        return new RuntimeReceiveResult
                        {
                            Success = true,
                            Result = result
                        };
                    }
                    catch (Exception ex)
                    {
                        return new RuntimeReceiveResult
                        {
                            Success = false,
                            Error = "Failed to read adb runtime result: " + ex.Message
                        };
                    }
                }

                Thread.Sleep(pollIntervalMs);
            }

            return new RuntimeReceiveResult
            {
                Success = false,
                TimedOut = true,
                Error = "Timeout waiting for adb runtime result."
            };
        }

        public void CleanupCommand(RuntimeTargetInfo target, string commandId)
        {
            if (target == null || string.IsNullOrWhiteSpace(commandId) || string.IsNullOrWhiteSpace(target.commandsPath))
            {
                return;
            }

            var preparation = PrepareState(null);
            if (!preparation.Success)
            {
                return;
            }

            var commandFile = CombineDevicePath(target.commandsPath, commandId + ".json");
            preparation.State.Client.Shell(
                "rm -f " + AdbClient.ShellQuote(commandFile) + " " + AdbClient.ShellQuote(commandFile + ".tmp"),
                GetOperationTimeout(_options.TimeoutMs));
            _pendingRequests.Remove(commandId);
        }

        public RuntimeDiagnosticReport Diagnose(string target, RuntimeCommandTrace commandTrace = null)
        {
            var targetName = string.IsNullOrWhiteSpace(target) ? RuntimeTransportOptions.DefaultTarget : target;
            var report = new RuntimeDiagnosticReport
            {
                transport = TransportName,
                runtimeDirectory = _options.DevicePath,
                targetId = targetName
            };

            var preparation = PrepareState(report);
            if (!preparation.Success)
            {
                AddAdbSuggestions(report);
                FinalizeReport(report, "Runtime adb transport diagnostics passed.");
                return report;
            }

            var state = preparation.State;
            report.runtimeDirectory = state.RuntimeDirectory;

            AddRemoteDirectoryReadCheck(
                report,
                state.Client,
                "runtimeDirectory",
                state.RuntimeDirectory,
                "Verify --package matches the installed Android package, or pass --device-path to the runtime root.");

            var targetsDirectory = CombineDevicePath(state.RuntimeDirectory, RuntimePathHelper.TargetsDirectoryName);
            AddRemoteDirectoryReadCheck(
                report,
                state.Client,
                "targetsDirectory",
                targetsDirectory,
                "Verify AIBridgeRuntime has started and can write Application.persistentDataPath/.aibridge/runtime.");

            var targetInfo = ResolveTarget(state, targetName);
            if (targetInfo == null)
            {
                report.checks.Add(new RuntimeDiagnosticCheck
                {
                    name = "targetExists",
                    status = CheckFailed,
                    detail = "target_not_found: Runtime target was not found on adb device. Target: " + targetName,
                    fix = "Start the Android Development Build, keep it foregrounded, then run runtime list_targets with the same --transport/--device/--package options."
                });
                AddAdbSuggestions(report);
                FinalizeReport(report, "Runtime adb transport diagnostics passed.");
                return report;
            }

            report.targetId = targetInfo.targetId;
            report.checks.Add(new RuntimeDiagnosticCheck
            {
                name = "targetExists",
                status = CheckPassed,
                detail = "Target path: " + targetInfo.path
            });

            AddHeartbeatChecks(report, state.Client, targetInfo);
            AddDirectoryChecks(report, state.Client, targetInfo);
            AddCommandTraceChecks(report, state.Client, commandTrace);
            AddAdbSuggestions(report, targetInfo);
            FinalizeReport(report, "Runtime adb transport diagnostics passed.");
            return report;
        }

        private List<RuntimeTargetInfo> ListTargets(AdbRuntimeState state)
        {
            var targets = new List<RuntimeTargetInfo>();
            var targetsDirectory = CombineDevicePath(state.RuntimeDirectory, RuntimePathHelper.TargetsDirectoryName);
            var listResult = state.Client.Shell(
                "if [ -d " + AdbClient.ShellQuote(targetsDirectory) + " ]; then for d in " + AdbClient.ShellQuote(targetsDirectory) + "/*; do if [ -d \"$d\" ]; then basename \"$d\"; fi; done; fi",
                GetOperationTimeout(_options.TimeoutMs));
            if (!listResult.Success)
            {
                return targets;
            }

            foreach (var line in SplitLines(listResult.StandardOutput))
            {
                var targetDirectoryName = line.Trim();
                if (string.IsNullOrEmpty(targetDirectoryName))
                {
                    continue;
                }

                var targetInfo = ReadTargetInfo(state, targetDirectoryName);
                if (targetInfo != null)
                {
                    targets.Add(targetInfo);
                }
            }

            targets.Sort(CompareTargets);
            return targets;
        }

        private RuntimeTargetInfo ResolveTarget(AdbRuntimeState state, string target)
        {
            var targets = ListTargets(state);
            if (targets.Count == 0)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(target) || string.Equals(target, RuntimeTransportOptions.DefaultTarget, StringComparison.OrdinalIgnoreCase))
            {
                return targets[0];
            }

            for (var i = 0; i < targets.Count; i++)
            {
                if (string.Equals(targets[i].targetId, target, StringComparison.OrdinalIgnoreCase))
                {
                    return targets[i];
                }
            }

            return null;
        }

        private RuntimeTargetInfo ReadTargetInfo(AdbRuntimeState state, string targetDirectoryName)
        {
            var targetPath = CombineDevicePath(state.RuntimeDirectory, RuntimePathHelper.TargetsDirectoryName, targetDirectoryName);
            var heartbeatPath = CombineDevicePath(targetPath, RuntimePathHelper.HeartbeatFileName);
            var heartbeat = ReadRemoteJson(state.Client, heartbeatPath);
            var targetId = ReadString(heartbeat, "targetId");
            if (string.IsNullOrWhiteSpace(targetId))
            {
                targetId = targetDirectoryName;
            }

            var lastHeartbeat = ParseHeartbeatTime(heartbeat);
            var ageSeconds = lastHeartbeat.HasValue
                ? (double?)(DateTime.UtcNow - lastHeartbeat.Value).TotalSeconds
                : null;

            return new RuntimeTargetInfo
            {
                targetId = targetId,
                path = targetPath,
                heartbeatPath = heartbeatPath,
                commandsPath = GetHeartbeatPathOrDefault(heartbeat, "commandsPath", targetPath, RuntimePathHelper.CommandsDirectoryName),
                resultsPath = GetHeartbeatPathOrDefault(heartbeat, "resultsPath", targetPath, RuntimePathHelper.ResultsDirectoryName),
                screenshotsPath = GetHeartbeatPathOrDefault(heartbeat, "screenshotsPath", targetPath, RuntimePathHelper.ScreenshotsDirectoryName),
                stale = !lastHeartbeat.HasValue || DateTime.UtcNow - lastHeartbeat.Value > StaleHeartbeatTimeout,
                ageSeconds = ageSeconds,
                lastHeartbeatUtc = lastHeartbeat.HasValue ? lastHeartbeat.Value.ToString("o") : null,
                heartbeat = heartbeat
            };
        }

        private AdbPreparation PrepareState(RuntimeDiagnosticReport report)
        {
            var adb = new AdbClient(_options.AdbPath);
            var devices = adb.ListDevices(out var devicesResult);
            if (!devicesResult.Success)
            {
                var detail = "transport_unavailable: adb is not available. " + devicesResult.GetErrorText();
                AddCheck(report, "adbAvailable", CheckFailed, detail, "Install Android platform-tools, add adb to PATH, or pass --adb <path>.");
                return AdbPreparation.Fail(detail);
            }

            AddCheck(report, "adbAvailable", CheckPassed, "adb path: " + adb.AdbPath, null);

            if (!TryResolveDevice(devices, out var deviceSerial, out var deviceError, out var deviceFix))
            {
                AddCheck(report, "deviceSelected", CheckFailed, deviceError, deviceFix);
                return AdbPreparation.Fail(deviceError);
            }

            AddCheck(report, "deviceSelected", CheckPassed, "ADB device: " + deviceSerial, null);

            if (string.IsNullOrWhiteSpace(_options.AndroidPackageName) && string.IsNullOrWhiteSpace(_options.DevicePath))
            {
                var detail = "transport_unavailable: --package or --device-path is required for adb runtime transport.";
                AddCheck(report, "devicePathConfigured", CheckFailed, detail, "Pass --package <packageName>, or pass --device-path <runtimeRoot>.");
                return AdbPreparation.Fail(detail);
            }

            var runtimeDirectory = ResolveDeviceRuntimeDirectory();
            AddCheck(report, "devicePathConfigured", CheckPassed, "Device runtime root: " + runtimeDirectory, null);

            return AdbPreparation.Ok(new AdbRuntimeState
            {
                Client = new AdbClient(_options.AdbPath, deviceSerial),
                DeviceSerial = deviceSerial,
                RuntimeDirectory = runtimeDirectory
            });
        }

        private bool TryResolveDevice(
            IReadOnlyList<AdbDeviceInfo> devices,
            out string deviceSerial,
            out string error,
            out string fix)
        {
            deviceSerial = null;
            error = null;
            fix = null;

            if (!string.IsNullOrWhiteSpace(_options.DeviceSerial))
            {
                for (var i = 0; i < devices.Count; i++)
                {
                    if (!string.Equals(devices[i].Serial, _options.DeviceSerial, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (string.Equals(devices[i].State, "device", StringComparison.OrdinalIgnoreCase))
                    {
                        deviceSerial = devices[i].Serial;
                        return true;
                    }

                    error = "transport_unavailable: ADB device '" + _options.DeviceSerial + "' is in state '" + devices[i].State + "'.";
                    fix = "Authorize the device for USB debugging, reconnect it, then retry.";
                    return false;
                }

                error = "transport_unavailable: ADB device '" + _options.DeviceSerial + "' was not found. Connected devices: " + FormatDeviceList(devices);
                fix = "Run adb devices and pass a listed serial with --device.";
                return false;
            }

            var activeCount = 0;
            string activeSerial = null;
            for (var i = 0; i < devices.Count; i++)
            {
                if (!string.Equals(devices[i].State, "device", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                activeCount++;
                activeSerial = devices[i].Serial;
            }

            if (activeCount == 0)
            {
                error = "transport_unavailable: No ADB device in 'device' state. Connected devices: " + FormatDeviceList(devices);
                fix = "Connect one Android device with USB debugging enabled, or pass --device after it appears in adb devices.";
                return false;
            }

            if (activeCount > 1)
            {
                error = "transport_unavailable: Multiple ADB devices are connected. Connected devices: " + FormatDeviceList(devices);
                fix = "Pass --device <serial> to select the Android device.";
                return false;
            }

            deviceSerial = activeSerial;
            return true;
        }

        private string ResolveDeviceRuntimeDirectory()
        {
            if (!string.IsNullOrWhiteSpace(_options.DevicePath))
            {
                return NormalizeDevicePath(_options.DevicePath);
            }

            return "/sdcard/Android/data/" + _options.AndroidPackageName + "/files/.aibridge/runtime";
        }

        private void TryPullArtifacts(AdbRuntimeState state, RuntimeTargetInfo target, string commandId, CommandResult result)
        {
            if (result == null || result.data == null)
            {
                return;
            }

            var data = result.data as JObject;
            if (data == null)
            {
                return;
            }

            var imagePath = ReadString(data, "imagePath");
            var devicePath = ReadString(data, "devicePath");
            if (string.IsNullOrWhiteSpace(devicePath))
            {
                devicePath = imagePath;
            }

            if (string.IsNullOrWhiteSpace(devicePath))
            {
                return;
            }

            _pendingRequests.TryGetValue(commandId, out var pending);
            var outputPath = pending == null ? null : pending.OutputPath;
            var localPath = ResolveArtifactLocalPath(state, target, commandId, data, devicePath, outputPath);
            var pullResult = state.Client.Pull(devicePath, localPath, ArtifactPullTimeoutMs);
            if (!pullResult.Success)
            {
                result.success = false;
                result.error = "artifact_pull_failed: adb pull failed: " + pullResult.GetErrorText();
                data["devicePath"] = devicePath;
                return;
            }

            data["devicePath"] = devicePath;
            data["pcPath"] = localPath;
            data["sha256"] = ComputeSha256(localPath);
            data["pcFileSize"] = new FileInfo(localPath).Length;

            if (string.IsNullOrWhiteSpace(outputPath))
            {
                data["imagePath"] = localPath;
                data["pulledToCache"] = true;
            }
            else
            {
                // RuntimeCommandSender 会再次处理 imagePath；adb 已完成拉取时移除它，避免同一路径二次复制失败。
                data.Remove("imagePath");
                data["output"] = localPath;
                data["copiedToOutput"] = true;
            }
        }

        private string ResolveArtifactLocalPath(
            AdbRuntimeState state,
            RuntimeTargetInfo target,
            string commandId,
            JObject data,
            string devicePath,
            string outputPath)
        {
            if (!string.IsNullOrWhiteSpace(outputPath))
            {
                var fullOutputPath = Path.GetFullPath(outputPath);
                var outputDirectory = Path.GetDirectoryName(fullOutputPath);
                if (!string.IsNullOrEmpty(outputDirectory))
                {
                    Directory.CreateDirectory(outputDirectory);
                }

                return fullOutputPath;
            }

            var fileName = ReadString(data, "filename");
            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = GetDeviceFileName(devicePath);
            }

            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = commandId + ".bin";
            }

            return Path.Combine(GetCacheDirectory(state.DeviceSerial, target.targetId, "artifacts"), fileName);
        }

        private static void AddHeartbeatChecks(RuntimeDiagnosticReport report, AdbClient client, RuntimeTargetInfo targetInfo)
        {
            AddRemoteFileReadCheck(report, client, "heartbeatFile", targetInfo.heartbeatPath, "Verify the runtime is still running and can write heartbeat.json.");

            if (targetInfo.heartbeat == null)
            {
                report.checks.Add(new RuntimeDiagnosticCheck
                {
                    name = "heartbeatReadable",
                    status = CheckFailed,
                    detail = "heartbeat.json is missing or invalid.",
                    fix = "Restart the Android Development Build, or verify the runtime root path."
                });
                return;
            }

            report.checks.Add(new RuntimeDiagnosticCheck
            {
                name = "heartbeatReadable",
                status = CheckPassed,
                detail = "Last heartbeat UTC: " + (targetInfo.lastHeartbeatUtc ?? "unknown")
            });

            var age = targetInfo.ageSeconds.HasValue ? targetInfo.ageSeconds.Value.ToString("0.0", CultureInfo.InvariantCulture) + "s" : "unknown";
            if (targetInfo.stale)
            {
                report.checks.Add(new RuntimeDiagnosticCheck
                {
                    name = "heartbeatFresh",
                    status = CheckFailed,
                    detail = "Last heartbeat age: " + age,
                    fix = "Bring the Android app to foreground, verify runInBackground/platform background limits, or restart the Development Build."
                });
                return;
            }

            report.checks.Add(new RuntimeDiagnosticCheck
            {
                name = "heartbeatFresh",
                status = CheckPassed,
                detail = "Last heartbeat age: " + age
            });
        }

        private static void AddDirectoryChecks(RuntimeDiagnosticReport report, AdbClient client, RuntimeTargetInfo targetInfo)
        {
            AddRemoteDirectoryReadCheck(report, client, "targetDirectory", targetInfo.path, "Verify the target directory exists and is readable via adb.");
            AddRemoteDirectoryReadWriteCheck(report, client, "commandsDirectory", targetInfo.commandsPath, "Verify adb shell can write commands and the Runtime can read/delete them.");
            AddRemoteDirectoryReadWriteCheck(report, client, "resultsDirectory", targetInfo.resultsPath, "Verify adb shell can read/delete results and the Runtime can write them.");
            AddRemoteDirectoryReadWriteCheck(report, client, "screenshotsDirectory", targetInfo.screenshotsPath, "Verify adb shell can pull screenshot artifacts.");
        }

        private static void AddCommandTraceChecks(RuntimeDiagnosticReport report, AdbClient client, RuntimeCommandTrace commandTrace)
        {
            if (commandTrace == null || string.IsNullOrEmpty(commandTrace.CommandId))
            {
                report.checks.Add(new RuntimeDiagnosticCheck
                {
                    name = "lastCommand",
                    status = CheckSkipped,
                    detail = "No command trace was provided."
                });
                return;
            }

            var commandExists = RemoteFileExists(client, commandTrace.CommandPath);
            var resultExists = RemoteFileExists(client, commandTrace.ResultPath);

            if (commandExists)
            {
                report.checks.Add(new RuntimeDiagnosticCheck
                {
                    name = "lastCommandConsumed",
                    status = CheckFailed,
                    detail = "Command file still exists on device: " + commandTrace.CommandPath,
                    fix = "The Android Runtime did not consume the command. Check heartbeat, foreground/background state, and runtime command scanning."
                });

                report.checks.Add(new RuntimeDiagnosticCheck
                {
                    name = "lastResultExists",
                    status = CheckSkipped,
                    detail = "Result is not expected until the command is consumed."
                });
                return;
            }

            report.checks.Add(new RuntimeDiagnosticCheck
            {
                name = "lastCommandConsumed",
                status = CheckPassed,
                detail = "Command file was consumed or cleaned: " + commandTrace.CommandId
            });

            report.checks.Add(new RuntimeDiagnosticCheck
            {
                name = "lastResultExists",
                status = resultExists ? CheckPassed : CheckFailed,
                detail = resultExists ? "Result file exists on device but was not pulled before timeout." : "No result file for command: " + commandTrace.CommandId,
                fix = resultExists
                    ? "Check whether adb pull failed or the result JSON is invalid."
                    : "The command was consumed but no result was produced. Check stuck handlers, async callbacks, token/action validation, or result write failures."
            });
        }

        private static void AddAdbSuggestions(RuntimeDiagnosticReport report, RuntimeTargetInfo targetInfo = null)
        {
            report.suggestions.Add("Run: adb devices");
            report.suggestions.Add("Run: $CLI runtime list_targets --transport adb --device <serial> --package <packageName>");
            if (targetInfo != null)
            {
                report.suggestions.Add("Run: $CLI runtime status --transport adb --target " + targetInfo.targetId + " --device <serial> --package <packageName>");
            }
        }

        private static void AddRemoteDirectoryReadCheck(RuntimeDiagnosticReport report, AdbClient client, string name, string path, string fix)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                report.checks.Add(new RuntimeDiagnosticCheck
                {
                    name = name,
                    status = CheckFailed,
                    detail = "Path is empty.",
                    fix = fix
                });
                return;
            }

            var result = client.Shell("if [ -d " + AdbClient.ShellQuote(path) + " ]; then echo " + ExistsMarker + "; fi");
            report.checks.Add(new RuntimeDiagnosticCheck
            {
                name = name,
                status = result.Success && ContainsLine(result.StandardOutput, ExistsMarker) ? CheckPassed : CheckFailed,
                detail = result.Success && ContainsLine(result.StandardOutput, ExistsMarker)
                    ? "Device directory exists: " + path
                    : "Device directory is missing or unreadable: " + path + (result.Success ? string.Empty : " (" + result.GetErrorText() + ")"),
                fix = fix
            });
        }

        private static void AddRemoteFileReadCheck(RuntimeDiagnosticReport report, AdbClient client, string name, string path, string fix)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                report.checks.Add(new RuntimeDiagnosticCheck
                {
                    name = name,
                    status = CheckFailed,
                    detail = "Path is empty.",
                    fix = fix
                });
                return;
            }

            var exists = RemoteFileExists(client, path);
            report.checks.Add(new RuntimeDiagnosticCheck
            {
                name = name,
                status = exists ? CheckPassed : CheckFailed,
                detail = exists ? "Device file exists: " + path : "Device file is missing or unreadable: " + path,
                fix = fix
            });
        }

        private static void AddRemoteDirectoryReadWriteCheck(RuntimeDiagnosticReport report, AdbClient client, string name, string path, string fix)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                report.checks.Add(new RuntimeDiagnosticCheck
                {
                    name = name,
                    status = CheckFailed,
                    detail = "Path is empty.",
                    fix = fix
                });
                return;
            }

            var probePath = CombineDevicePath(path, ".aibridge_cli_probe_" + Guid.NewGuid().ToString("N") + ".tmp");
            var command = "if [ ! -d " + AdbClient.ShellQuote(path) + " ]; then exit 2; fi; "
                + "printf probe > " + AdbClient.ShellQuote(probePath)
                + " && cat " + AdbClient.ShellQuote(probePath) + " >/dev/null"
                + " && rm -f " + AdbClient.ShellQuote(probePath);
            var result = client.Shell(command);
            if (result.Success)
            {
                report.checks.Add(new RuntimeDiagnosticCheck
                {
                    name = name,
                    status = CheckPassed,
                    detail = "Device directory is readable and writable: " + path
                });
                return;
            }

            client.RemoveFile(probePath);
            report.checks.Add(new RuntimeDiagnosticCheck
            {
                name = name,
                status = CheckFailed,
                detail = "Device directory read/write check failed: " + path + " (" + result.GetErrorText() + ")",
                fix = fix
            });
        }

        private static bool RemoteFileExists(AdbClient client, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var result = client.Shell("if [ -f " + AdbClient.ShellQuote(path) + " ]; then echo " + ExistsMarker + "; fi");
            return result.Success && ContainsLine(result.StandardOutput, ExistsMarker);
        }

        private static JObject ReadRemoteJson(AdbClient client, string path)
        {
            var result = client.Shell("if [ -f " + AdbClient.ShellQuote(path) + " ]; then cat " + AdbClient.ShellQuote(path) + "; fi");
            if (!result.Success || string.IsNullOrWhiteSpace(result.StandardOutput))
            {
                return null;
            }

            try
            {
                return JObject.Parse(result.StandardOutput);
            }
            catch
            {
                return null;
            }
        }

        private static void AddCheck(RuntimeDiagnosticReport report, string name, string status, string detail, string fix)
        {
            if (report == null)
            {
                return;
            }

            report.checks.Add(new RuntimeDiagnosticCheck
            {
                name = name,
                status = status,
                detail = detail,
                fix = fix
            });
        }

        private static void FinalizeReport(RuntimeDiagnosticReport report, string successSummary)
        {
            RuntimeDiagnosticCheck failed = null;
            for (var i = 0; i < report.checks.Count; i++)
            {
                if (string.Equals(report.checks[i].status, CheckFailed, StringComparison.OrdinalIgnoreCase))
                {
                    failed = report.checks[i];
                    break;
                }
            }

            report.success = failed == null;
            report.summary = report.success ? successSummary : failed.detail;
        }

        private static RuntimeTargetInfo CreateUnavailableTarget(string target)
        {
            return new RuntimeTargetInfo
            {
                targetId = string.IsNullOrWhiteSpace(target) ? RuntimeTransportOptions.DefaultTarget : target
            };
        }

        private static int CompareTargets(RuntimeTargetInfo a, RuntimeTargetInfo b)
        {
            var staleCompare = a.stale.CompareTo(b.stale);
            if (staleCompare != 0)
            {
                return staleCompare;
            }

            var heartbeatCompare = string.Compare(b.lastHeartbeatUtc, a.lastHeartbeatUtc, StringComparison.OrdinalIgnoreCase);
            if (heartbeatCompare != 0)
            {
                return heartbeatCompare;
            }

            return string.Compare(a.targetId, b.targetId, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetHeartbeatPathOrDefault(JObject heartbeat, string heartbeatKey, string targetPath, string directoryName)
        {
            var value = ReadString(heartbeat, heartbeatKey);
            return string.IsNullOrWhiteSpace(value) ? CombineDevicePath(targetPath, directoryName) : NormalizeDevicePath(value);
        }

        private static DateTime? ParseHeartbeatTime(JObject heartbeat)
        {
            var value = ReadString(heartbeat, "lastHeartbeatUtc");
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            if (DateTimeOffset.TryParse(value, out var parsedOffset))
            {
                return parsedOffset.UtcDateTime;
            }

            return null;
        }

        private static string ReadString(JObject token, string name)
        {
            if (token == null)
            {
                return null;
            }

            return token.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out var value) ? value.Value<string>() : null;
        }

        private static string TryGetRequestParam(CommandRequest request, string key)
        {
            if (request == null || request.@params == null)
            {
                return null;
            }

            foreach (var pair in request.@params)
            {
                if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    return Convert.ToString(pair.Value, CultureInfo.InvariantCulture);
                }
            }

            return null;
        }

        private static string GetCacheDirectory(string deviceSerial, string targetId, string kind)
        {
            return Path.Combine(
                PathHelper.GetExchangeDirectory(),
                "runtime-cache",
                "adb",
                SanitizePathSegment(deviceSerial),
                SanitizePathSegment(targetId),
                kind);
        }

        private static string ComputeSha256(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(stream);
                var builder = new StringBuilder(hash.Length * 2);
                for (var i = 0; i < hash.Length; i++)
                {
                    builder.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                }

                return builder.ToString();
            }
        }

        private static string FormatDeviceList(IReadOnlyList<AdbDeviceInfo> devices)
        {
            if (devices == null || devices.Count == 0)
            {
                return "none";
            }

            var parts = new List<string>();
            for (var i = 0; i < devices.Count; i++)
            {
                parts.Add(devices[i].Serial + "(" + devices[i].State + ")");
            }

            return string.Join(", ", parts);
        }

        private static bool ContainsLine(string value, string expected)
        {
            foreach (var line in SplitLines(value))
            {
                if (string.Equals(line.Trim(), expected, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static IEnumerable<string> SplitLines(string value)
        {
            return (value ?? string.Empty).Replace("\r\n", "\n").Split('\n');
        }

        private static string CombineDevicePath(params string[] parts)
        {
            var builder = new StringBuilder();
            for (var i = 0; i < parts.Length; i++)
            {
                var part = NormalizeDevicePath(parts[i]);
                if (string.IsNullOrWhiteSpace(part))
                {
                    continue;
                }

                if (builder.Length == 0)
                {
                    builder.Append(part.TrimEnd('/'));
                    continue;
                }

                builder.Append('/');
                builder.Append(part.Trim('/'));
            }

            return builder.ToString();
        }

        private static string NormalizeDevicePath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            var normalized = value.Replace('\\', '/');
            while (normalized.Length > 1 && normalized.EndsWith("/", StringComparison.Ordinal))
            {
                normalized = normalized.Substring(0, normalized.Length - 1);
            }

            return normalized;
        }

        private static string GetDeviceFileName(string devicePath)
        {
            var normalized = NormalizeDevicePath(devicePath);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return null;
            }

            var slashIndex = normalized.LastIndexOf('/');
            return slashIndex >= 0 && slashIndex + 1 < normalized.Length
                ? normalized.Substring(slashIndex + 1)
                : normalized;
        }

        private static string SanitizePathSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "unknown";
            }

            var builder = new StringBuilder(value.Length);
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.')
                {
                    builder.Append(c);
                }
                else
                {
                    builder.Append('_');
                }
            }

            return builder.ToString();
        }

        private static int GetOperationTimeout(int timeoutMs)
        {
            if (timeoutMs <= 0)
            {
                return AdbClient.DefaultTimeoutMs;
            }

            return Math.Max(1000, Math.Min(AdbClient.DefaultTimeoutMs, timeoutMs));
        }

        private sealed class AdbRuntimeState
        {
            public AdbClient Client { get; set; }
            public string DeviceSerial { get; set; }
            public string RuntimeDirectory { get; set; }
        }

        private sealed class AdbPreparation
        {
            public bool Success { get; set; }
            public string Error { get; set; }
            public AdbRuntimeState State { get; set; }

            public static AdbPreparation Ok(AdbRuntimeState state)
            {
                return new AdbPreparation
                {
                    Success = true,
                    State = state
                };
            }

            public static AdbPreparation Fail(string error)
            {
                return new AdbPreparation
                {
                    Success = false,
                    Error = error
                };
            }
        }

        private sealed class PendingRequest
        {
            public string Action { get; set; }
            public string OutputPath { get; set; }
            public string TargetId { get; set; }
            public string DeviceSerial { get; set; }
        }
    }
}
