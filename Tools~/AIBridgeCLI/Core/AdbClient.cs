using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace AIBridgeCLI.Core
{
    public sealed class AdbClient
    {
        public const int DefaultTimeoutMs = 10000;

        private readonly string _adbPath;
        private readonly string _deviceSerial;

        public AdbClient(string adbPath, string deviceSerial = null)
        {
            _adbPath = string.IsNullOrWhiteSpace(adbPath) ? "adb" : adbPath;
            _deviceSerial = deviceSerial;
        }

        public string AdbPath => _adbPath;
        public string DeviceSerial => _deviceSerial;

        public IReadOnlyList<AdbDeviceInfo> ListDevices(out AdbExecutionResult executionResult)
        {
            executionResult = Execute(new[] { "devices" }, DefaultTimeoutMs, useDevice: false);
            var devices = new List<AdbDeviceInfo>();
            if (!executionResult.Success)
            {
                return devices;
            }

            var lines = SplitLines(executionResult.StandardOutput);
            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("List of devices", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                {
                    continue;
                }

                devices.Add(new AdbDeviceInfo
                {
                    Serial = parts[0],
                    State = parts[1]
                });
            }

            return devices;
        }

        public AdbExecutionResult Shell(string command, int timeoutMs = DefaultTimeoutMs)
        {
            return Execute(new[] { "shell", "sh", "-c", command }, timeoutMs, useDevice: true);
        }

        public AdbExecutionResult Push(string localPath, string devicePath, int timeoutMs = DefaultTimeoutMs)
        {
            return Execute(new[] { "push", localPath, devicePath }, timeoutMs, useDevice: true);
        }

        public AdbExecutionResult Pull(string devicePath, string localPath, int timeoutMs = DefaultTimeoutMs)
        {
            var directory = System.IO.Path.GetDirectoryName(localPath);
            if (!string.IsNullOrEmpty(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
            }

            return Execute(new[] { "pull", devicePath, localPath }, timeoutMs, useDevice: true);
        }

        public AdbExecutionResult RemoveFile(string devicePath, int timeoutMs = DefaultTimeoutMs)
        {
            return Shell("rm -f " + ShellQuote(devicePath), timeoutMs);
        }

        public static string ShellQuote(string value)
        {
            if (value == null)
            {
                return "''";
            }

            return "'" + value.Replace("'", "'\"'\"'") + "'";
        }

        private AdbExecutionResult Execute(IReadOnlyList<string> commandArgs, int timeoutMs, bool useDevice)
        {
            var args = new List<string>();
            if (useDevice && !string.IsNullOrWhiteSpace(_deviceSerial))
            {
                args.Add("-s");
                args.Add(_deviceSerial);
            }

            for (var i = 0; i < commandArgs.Count; i++)
            {
                args.Add(commandArgs[i]);
            }

            return ExecuteRaw(args, timeoutMs <= 0 ? DefaultTimeoutMs : timeoutMs);
        }

        private AdbExecutionResult ExecuteRaw(IReadOnlyList<string> args, int timeoutMs)
        {
            try
            {
                using (var process = new Process())
                {
                    process.StartInfo = new ProcessStartInfo
                    {
                        FileName = _adbPath,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8
                    };

                    for (var i = 0; i < args.Count; i++)
                    {
                        process.StartInfo.ArgumentList.Add(args[i]);
                    }

                    process.Start();
                    var stdoutTask = process.StandardOutput.ReadToEndAsync();
                    var stderrTask = process.StandardError.ReadToEndAsync();

                    if (!process.WaitForExit(timeoutMs))
                    {
                        try { process.Kill(entireProcessTree: true); } catch { }
                        return new AdbExecutionResult
                        {
                            ExitCode = -1,
                            TimedOut = true,
                            Error = "adb command timed out after " + timeoutMs + "ms.",
                            StandardOutput = stdoutTask.IsCompleted ? stdoutTask.Result : string.Empty,
                            StandardError = stderrTask.IsCompleted ? stderrTask.Result : string.Empty
                        };
                    }

                    process.WaitForExit();
                    return new AdbExecutionResult
                    {
                        ExitCode = process.ExitCode,
                        StandardOutput = stdoutTask.Result,
                        StandardError = stderrTask.Result
                    };
                }
            }
            catch (Win32Exception ex)
            {
                return new AdbExecutionResult
                {
                    ExitCode = -1,
                    StartFailed = true,
                    Error = ex.Message
                };
            }
            catch (Exception ex)
            {
                return new AdbExecutionResult
                {
                    ExitCode = -1,
                    Error = ex.Message
                };
            }
        }

        private static IEnumerable<string> SplitLines(string value)
        {
            return (value ?? string.Empty).Replace("\r\n", "\n").Split('\n');
        }
    }

    public sealed class AdbDeviceInfo
    {
        public string Serial { get; set; }
        public string State { get; set; }
    }

    public sealed class AdbExecutionResult
    {
        public int ExitCode { get; set; }
        public string StandardOutput { get; set; }
        public string StandardError { get; set; }
        public string Error { get; set; }
        public bool TimedOut { get; set; }
        public bool StartFailed { get; set; }

        public bool Success
        {
            get { return !TimedOut && !StartFailed && ExitCode == 0; }
        }

        public string GetErrorText()
        {
            if (!string.IsNullOrWhiteSpace(Error))
            {
                return Error;
            }

            if (!string.IsNullOrWhiteSpace(StandardError))
            {
                return StandardError.Trim();
            }

            if (!string.IsNullOrWhiteSpace(StandardOutput))
            {
                return StandardOutput.Trim();
            }

            return "adb exited with code " + ExitCode + ".";
        }
    }
}
