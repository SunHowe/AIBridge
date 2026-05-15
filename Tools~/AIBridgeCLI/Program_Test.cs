using System;
using System.Collections.Generic;
using AIBridgeCLI.Core;
using Newtonsoft.Json;

namespace AIBridgeCLI
{
    partial class Program
    {
        /// <summary>
        /// 执行 Unity TestRunner 测试，并轮询状态直到结束或超时。
        /// </summary>
        static int HandleTestRun(ParsedArgs parsed, OutputMode outputMode)
        {
            var testTimeout = parsed.Options.TryGetValue("timeout", out var timeoutValue) && int.TryParse(timeoutValue, out var timeoutMs)
                ? timeoutMs
                : 120000;
            var pollInterval = parsed.Options.TryGetValue("poll-interval", out var pollValue) && int.TryParse(pollValue, out var pollMs)
                ? pollMs
                : 500;
            var commandTimeout = parsed.Options.TryGetValue("transport-timeout", out var transportValue) && int.TryParse(transportValue, out var transportMs)
                ? transportMs
                : GetDefaultUnityCompileTransportTimeout(testTimeout, pollInterval);

            var sender = new CommandSender(commandTimeout);
            var startTime = DateTime.Now;

            var startParams = new Dictionary<string, object>
            {
                { "action", "run" },
                { "mode", parsed.Options.TryGetValue("mode", out var mode) ? mode : "EditMode" },
                { "timeout", testTimeout }
            };

            AddOptionalParam(startParams, "testName", parsed.Options, "test-name");
            AddOptionalParam(startParams, "groupName", parsed.Options, "group-name");
            AddOptionalParam(startParams, "assemblyName", parsed.Options, "assembly-name");

            if (outputMode == OutputMode.Pretty)
            {
                OutputFormatter.PrintInfo($"Starting Unity tests ({startParams["mode"]})...");
            }

            var startResult = sender.SendCommand(new CommandRequest
            {
                id = PathHelper.GenerateCommandId(),
                type = "test",
                @params = startParams
            });

            if (!startResult.success)
            {
                if (!IsTransportTimeoutError(startResult.error))
                {
                    OutputTestResult(outputMode, false, "failed", null, 0, null, 0, 0, 0, 0, 0, new List<object>(), startResult.error, false, false, false);
                    return 1;
                }

                if (outputMode == OutputMode.Pretty)
                {
                    OutputFormatter.PrintInfo("Unity did not acknowledge the test request within the transport timeout. Waiting for test status...");
                }
            }
            else
            {
                var data = startResult.data as Newtonsoft.Json.Linq.JObject;
                var startStatus = (string)data?["status"] ?? "running";
                var attachedToExistingRun = (bool?)data?["attachedToExistingRun"] ?? false;
                var startedByInvocation = (bool?)data?["startedByInvocation"] ?? false;

                if (startStatus == "passed" || startStatus == "failed")
                {
                    return OutputFinalTestStatus(outputMode, data, startedByInvocation, attachedToExistingRun, true, startTime);
                }
            }

            while ((DateTime.Now - startTime).TotalMilliseconds < testTimeout)
            {
                System.Threading.Thread.Sleep(pollInterval);

                var statusResult = sender.SendCommand(new CommandRequest
                {
                    id = PathHelper.GenerateCommandId(),
                    type = "test",
                    @params = new Dictionary<string, object>
                    {
                        { "action", "status" }
                    }
                });

                if (!statusResult.success)
                {
                    continue;
                }

                var statusData = statusResult.data as Newtonsoft.Json.Linq.JObject;
                var status = (string)statusData?["status"] ?? "idle";
                if (status == "running" || status == "idle")
                {
                    continue;
                }

                return OutputFinalTestStatus(outputMode, statusData, false, false, true, startTime);
            }

            OutputTestResult(outputMode, false, "timeout", null, (DateTime.Now - startTime).TotalSeconds, null, 0, 0, 0, 0, 0,
                new List<object>(), $"Test run timed out after {testTimeout}ms. Unity may still be running tests.", false, false, false);
            return 1;
        }

        /// <summary>
        /// 查询最近一次 Unity TestRunner 测试状态。
        /// </summary>
        static int HandleTestStatus(ParsedArgs parsed, OutputMode outputMode)
        {
            var transportTimeout = parsed.Options.TryGetValue("timeout", out var timeoutValue) && int.TryParse(timeoutValue, out var timeoutMs)
                ? timeoutMs
                : DEFAULT_TIMEOUT;

            var sender = new CommandSender(transportTimeout);
            var result = sender.SendCommand(new CommandRequest
            {
                id = PathHelper.GenerateCommandId(),
                type = "test",
                @params = new Dictionary<string, object>
                {
                    { "action", "status" }
                }
            });

            if (!result.success)
            {
                OutputTestResult(outputMode, false, "failed", null, 0, null, 0, 0, 0, 0, 0, new List<object>(), result.error, false, false, false);
                return 1;
            }

            var data = result.data as Newtonsoft.Json.Linq.JObject;
            if (data == null)
            {
                OutputTestResult(outputMode, true, "idle", null, 0, null, 0, 0, 0, 0, 0, new List<object>(), null, false, false, true);
                return 0;
            }

            var status = (string)data["status"] ?? "idle";
            var mode = (string)data["mode"];
            var duration = (double?)data["duration"] ?? 0;
            var startedAt = (string)data["startedAt"];
            var total = (int?)data["total"] ?? 0;
            var passed = (int?)data["passed"] ?? 0;
            var failed = (int?)data["failed"] ?? 0;
            var skipped = (int?)data["skipped"] ?? 0;
            var inconclusive = (int?)data["inconclusive"] ?? 0;
            var failedTests = ConvertFailedTests(data["failedTests"] as Newtonsoft.Json.Linq.JArray);
            var attachedToExistingRun = (bool?)data["attachedToExistingRun"] ?? false;
            var startedByInvocation = (bool?)data["startedByInvocation"] ?? false;
            var success = status == "idle" || status == "running" || status == "passed";

            OutputTestResult(outputMode, success, status, mode, duration, startedAt, total, passed, failed, skipped, inconclusive,
                failedTests, null, startedByInvocation, attachedToExistingRun, true);
            return success ? 0 : 1;
        }

        static int OutputFinalTestStatus(OutputMode outputMode, Newtonsoft.Json.Linq.JObject data,
            bool startedByInvocation, bool attachedToExistingRun, bool statusConfirmed, DateTime startTime)
        {
            var status = (string)data?["status"] ?? "failed";
            var mode = (string)data?["mode"];
            var duration = (double?)data?["duration"] ?? (DateTime.Now - startTime).TotalSeconds;
            var startedAt = (string)data?["startedAt"];
            var total = (int?)data?["total"] ?? 0;
            var passed = (int?)data?["passed"] ?? 0;
            var failed = (int?)data?["failed"] ?? 0;
            var skipped = (int?)data?["skipped"] ?? 0;
            var inconclusive = (int?)data?["inconclusive"] ?? 0;
            var failedTests = ConvertFailedTests(data?["failedTests"] as Newtonsoft.Json.Linq.JArray);
            startedByInvocation = (bool?)data?["startedByInvocation"] ?? startedByInvocation;
            attachedToExistingRun = (bool?)data?["attachedToExistingRun"] ?? attachedToExistingRun;
            var success = status == "passed";

            OutputTestResult(outputMode, success, status, mode, duration, startedAt, total, passed, failed, skipped, inconclusive,
                failedTests, null, startedByInvocation, attachedToExistingRun, statusConfirmed);
            return success ? 0 : 1;
        }

        static List<object> ConvertFailedTests(Newtonsoft.Json.Linq.JArray failedTestsArray)
        {
            var failedTests = new List<object>();
            if (failedTestsArray == null)
            {
                return failedTests;
            }

            foreach (var failedTest in failedTestsArray)
            {
                failedTests.Add(new
                {
                    name = (string)failedTest["name"],
                    message = (string)failedTest["message"],
                    stackTrace = (string)failedTest["stackTrace"]
                });
            }

            return failedTests;
        }

        static void AddOptionalParam(Dictionary<string, object> target, string targetKey, Dictionary<string, string> source, string sourceKey)
        {
            if (source.TryGetValue(sourceKey, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                target[targetKey] = value;
            }
        }

        static void OutputTestResult(OutputMode outputMode, bool success, string status, string mode, double duration, string startedAt,
            int total, int passed, int failed, int skipped, int inconclusive,
            List<object> failedTests, string error,
            bool startedByInvocation, bool attachedToExistingRun, bool statusConfirmed)
        {
            if (outputMode == OutputMode.Raw || outputMode == OutputMode.Quiet)
            {
                var jsonResult = new
                {
                    success = success,
                    status = status,
                    mode = mode,
                    startedAt = startedAt,
                    duration = Math.Round(duration, 2),
                    total = total,
                    passed = passed,
                    failed = failed,
                    skipped = skipped,
                    inconclusive = inconclusive,
                    startedByInvocation = startedByInvocation,
                    attachedToExistingRun = attachedToExistingRun,
                    statusConfirmed = statusConfirmed,
                    failedTests = failedTests,
                    error = error
                };
                Console.WriteLine(JsonConvert.SerializeObject(jsonResult, Formatting.None));
                return;
            }

            if (success && status != "running" && status != "idle")
            {
                OutputFormatter.PrintSuccess($"Unity tests passed in {duration:F1}s");
            }
            else if (status == "running")
            {
                OutputFormatter.PrintInfo("Unity tests are still running.");
            }
            else if (status == "idle")
            {
                OutputFormatter.PrintInfo("No Unity test run is active.");
            }
            else if (!string.IsNullOrEmpty(error))
            {
                OutputFormatter.PrintError(error);
            }
            else if (status == "timeout")
            {
                OutputFormatter.PrintError($"Unity tests timed out after {duration:F1}s");
            }
            else
            {
                OutputFormatter.PrintError($"Unity tests failed in {duration:F1}s");
            }

            Console.WriteLine($"  status: {status}");
            if (!string.IsNullOrEmpty(mode))
            {
                Console.WriteLine($"  mode: {mode}");
            }
            if (!string.IsNullOrEmpty(startedAt))
            {
                Console.WriteLine($"  startedAt: {startedAt}");
            }
            Console.WriteLine($"  duration: {duration:F2}s");
            Console.WriteLine($"  total: {total}");
            Console.WriteLine($"  passed: {passed}");
            Console.WriteLine($"  failed: {failed}");
            Console.WriteLine($"  skipped: {skipped}");
            Console.WriteLine($"  inconclusive: {inconclusive}");

            foreach (var failedTest in failedTests)
            {
                var failedTestObj = failedTest as dynamic;
                Console.WriteLine($"  failedTest: {failedTestObj.name}");
                if (!string.IsNullOrEmpty(failedTestObj.message))
                {
                    Console.WriteLine($"    message: {failedTestObj.message}");
                }
            }
        }
    }
}
