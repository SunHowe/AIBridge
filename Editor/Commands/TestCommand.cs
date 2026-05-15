using System;
using UnityEditor.TestTools.TestRunner.Api;

namespace AIBridge.Editor
{
    /// <summary>
    /// Native test command backed by Unity TestRunnerApi.
    /// </summary>
    public class TestCommand : ICommand
    {
        public string Type => "test";
        public bool RequiresRefresh => false;

        public string SkillDescription => @"### `test` - Native Unity Test Runner

```bash
$CLI test run --mode EditMode
$CLI test run --test-name ""MyNamespace.MyFixture.MyTest""
$CLI test status
```";

        public CommandResult Execute(CommandRequest request)
        {
            var action = request.GetParam("action", "status");

            try
            {
                switch (action.ToLower())
                {
                    case "run":
                        return RunTests(request);
                    case "status":
                        return QueryStatus(request);
                    default:
                        return CommandResult.Failure(request.id, $"Unknown action: {action}. Supported: run, status");
                }
            }
            catch (Exception ex)
            {
                return CommandResult.FromException(request.id, ex);
            }
        }

        private CommandResult RunTests(CommandRequest request)
        {
            var modeText = request.GetParam("mode", "EditMode");
            var timeoutMs = request.GetParam("timeout", 120000);
            var testName = request.GetParam<string>("testName", null);
            var groupName = request.GetParam<string>("groupName", null);
            var assemblyName = request.GetParam<string>("assemblyName", null);

            if (!TryParseMode(modeText, out var mode))
            {
                return CommandResult.Failure(request.id, $"Unsupported test mode: {modeText}. Supported: EditMode, PlayMode");
            }

            // First version only guarantees EditMode. Keep the PlayMode API surface but return not supported for now.
            if (mode == TestMode.PlayMode)
            {
                return CommandResult.Failure(request.id, "PlayMode tests are not supported yet. Please use EditMode for now.");
            }

            var startResult = TestRunTracker.StartRun(mode, testName, groupName, assemblyName, timeoutMs);
            var snapshot = startResult.snapshot;

            return CommandResult.Success(request.id, new
            {
                action = "run",
                status = snapshot.status,
                mode = snapshot.mode,
                startedAt = snapshot.startedAt,
                duration = snapshot.duration,
                total = snapshot.total,
                passed = snapshot.passed,
                failed = snapshot.failed,
                skipped = snapshot.skipped,
                inconclusive = snapshot.inconclusive,
                failedTests = snapshot.failedTests,
                startedByInvocation = startResult.startedByInvocation,
                attachedToExistingRun = startResult.attachedToExistingRun
            });
        }

        private CommandResult QueryStatus(CommandRequest request)
        {
            var snapshot = TestRunTracker.GetSnapshot();

            return CommandResult.Success(request.id, new
            {
                action = "status",
                status = snapshot.status,
                mode = snapshot.mode,
                startedAt = snapshot.startedAt,
                duration = snapshot.duration,
                total = snapshot.total,
                passed = snapshot.passed,
                failed = snapshot.failed,
                skipped = snapshot.skipped,
                inconclusive = snapshot.inconclusive,
                failedTests = snapshot.failedTests,
                startedByInvocation = snapshot.startedByInvocation,
                attachedToExistingRun = snapshot.attachedToExistingRun
            });
        }

        private bool TryParseMode(string modeText, out TestMode mode)
        {
            if (string.Equals(modeText, "PlayMode", StringComparison.OrdinalIgnoreCase))
            {
                mode = TestMode.PlayMode;
                return true;
            }

            if (string.Equals(modeText, "EditMode", StringComparison.OrdinalIgnoreCase))
            {
                mode = TestMode.EditMode;
                return true;
            }

            mode = TestMode.EditMode;
            return false;
        }
    }
}
