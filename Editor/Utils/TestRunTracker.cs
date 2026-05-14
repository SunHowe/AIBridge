using System;
using System.Collections.Generic;
using NUnit.Framework.Interfaces;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace AIBridge.Editor
{
    /// <summary>
    /// Tracks the latest native Unity test run for AIBridge test run/status commands.
    /// </summary>
    [InitializeOnLoad]
    public static class TestRunTracker
    {
        public enum TestRunStatus
        {
            Idle,
            Running,
            Passed,
            Failed,
            Timeout
        }

        [Serializable]
        public class FailedTestInfo
        {
            public string name;
            public string message;
            public string stackTrace;
        }

        private class TestRunState
        {
            public TestRunStatus status;
            public string mode;
            public DateTime startTime;
            public DateTime? endTime;
            public int timeoutMs;
            public int total;
            public int passed;
            public int failed;
            public int skipped;
            public int inconclusive;
            public bool startedByInvocation;
            public bool attachedToExistingRun;
            public bool isRunning;
            public readonly List<FailedTestInfo> failedTests = new List<FailedTestInfo>();
        }

        private sealed class TestCallbacks : IErrorCallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun)
            {
                if (_currentState == null)
                {
                    return;
                }

                _currentState.total = testsToRun != null ? testsToRun.TestCaseCount : 0;
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                if (_currentState == null)
                {
                    return;
                }

                _currentState.isRunning = false;
                _currentState.endTime = DateTime.Now;
                _currentState.total = result.PassCount + result.FailCount + result.SkipCount + result.InconclusiveCount;
                _currentState.passed = result.PassCount;
                _currentState.failed = result.FailCount;
                _currentState.skipped = result.SkipCount;
                _currentState.inconclusive = result.InconclusiveCount;
                _currentState.status = result.FailCount > 0 ? TestRunStatus.Failed : TestRunStatus.Passed;

                LogSummary(_currentState.status);
            }

            public void TestStarted(ITestAdaptor test)
            {
            }

            public void TestFinished(ITestResultAdaptor result)
            {
                if (_currentState == null || result == null || result.Test == null || result.Test.IsSuite)
                {
                    return;
                }

                if (result.TestStatus != UnityEditor.TestTools.TestRunner.Api.TestStatus.Failed)
                {
                    return;
                }

                _currentState.failedTests.Add(new FailedTestInfo
                {
                    name = result.FullName,
                    message = result.Message,
                    stackTrace = result.StackTrace
                });
            }

            public void OnError(string message)
            {
                if (_currentState == null)
                {
                    return;
                }

                _currentState.isRunning = false;
                _currentState.endTime = DateTime.Now;
                _currentState.status = TestRunStatus.Failed;

                if (!string.IsNullOrEmpty(message))
                {
                    _currentState.failedTests.Add(new FailedTestInfo
                    {
                        name = "TestRunError",
                        message = message,
                        stackTrace = string.Empty
                    });
                }

                LogSummary(_currentState.status);
            }
        }

        private static readonly TestCallbacks Callbacks = new TestCallbacks();
        private static TestRunnerApi _testRunnerApi;
        private static TestRunState _currentState;
        private static bool _initialized;

        static TestRunTracker()
        {
            Initialize();
        }

        public static void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            _testRunnerApi = ScriptableObject.CreateInstance<TestRunnerApi>();
            _testRunnerApi.RegisterCallbacks(Callbacks);
            _currentState = new TestRunState
            {
                status = TestRunStatus.Idle
            };

            _initialized = true;
            AIBridgeLogger.LogDebug("TestRunTracker initialized");
        }

        /// <summary>
        /// Start a new test run, or attach to the current run if one is already active.
        /// </summary>
        public static StartRunResult StartRun(TestMode mode, string testName, string groupName, string assemblyName, int timeoutMs)
        {
            Initialize();

            if (_currentState != null && _currentState.isRunning)
            {
                return new StartRunResult
                {
                    startedByInvocation = false,
                    attachedToExistingRun = true,
                    snapshot = GetSnapshot()
                };
            }

            var filter = new Filter
            {
                testMode = mode
            };

            AssignFilterValue(testName, value => filter.testNames = new[] { value });
            AssignFilterValue(groupName, value => filter.groupNames = new[] { value });
            AssignFilterValue(assemblyName, value => filter.assemblyNames = new[] { value });

            _currentState = new TestRunState
            {
                status = TestRunStatus.Running,
                mode = ModeToString(mode),
                startTime = DateTime.Now,
                timeoutMs = timeoutMs,
                startedByInvocation = true,
                attachedToExistingRun = false,
                isRunning = true
            };

            var executionSettings = new ExecutionSettings(filter)
            {
                runSynchronously = false
            };

            _testRunnerApi.Execute(executionSettings);

            return new StartRunResult
            {
                startedByInvocation = true,
                attachedToExistingRun = false,
                snapshot = GetSnapshot()
            };
        }

        public static TestRunSnapshot GetSnapshot()
        {
            Initialize();

            var state = _currentState ?? new TestRunState
            {
                status = TestRunStatus.Idle
            };

            // Timeout only changes the reported status. It does not interrupt Unity's real test runner.
            if (state.isRunning && state.timeoutMs > 0 && (DateTime.Now - state.startTime).TotalMilliseconds > state.timeoutMs)
            {
                state.status = TestRunStatus.Timeout;
            }

            var endTime = state.endTime ?? DateTime.Now;
            var duration = state.startTime == default ? 0 : (endTime - state.startTime).TotalSeconds;

            return new TestRunSnapshot
            {
                status = StatusToString(state.status),
                mode = state.mode,
                startedAt = state.startTime == default ? null : state.startTime.ToString("o"),
                duration = Math.Round(duration, 2),
                total = state.total,
                passed = state.passed,
                failed = state.failed,
                skipped = state.skipped,
                inconclusive = state.inconclusive,
                failedTests = new List<FailedTestInfo>(state.failedTests),
                startedByInvocation = state.startedByInvocation,
                attachedToExistingRun = state.attachedToExistingRun
            };
        }

        private static void AssignFilterValue(string value, Action<string> assignAction)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            assignAction(value);
        }

        private static string ModeToString(TestMode mode)
        {
            return mode == TestMode.PlayMode ? "PlayMode" : "EditMode";
        }

        private static string StatusToString(TestRunStatus status)
        {
            switch (status)
            {
                case TestRunStatus.Running:
                    return "running";
                case TestRunStatus.Passed:
                    return "passed";
                case TestRunStatus.Failed:
                    return "failed";
                case TestRunStatus.Timeout:
                    return "timeout";
                default:
                    return "idle";
            }
        }

        private static void LogSummary(TestRunStatus status)
        {
            var snapshot = GetSnapshot();
            AIBridgeLogger.LogInfo(
                $"Test run {StatusToString(status)}. mode={snapshot.mode}, total={snapshot.total}, passed={snapshot.passed}, failed={snapshot.failed}, skipped={snapshot.skipped}, inconclusive={snapshot.inconclusive}, duration={snapshot.duration:F2}s");
        }
    }

    [Serializable]
    public class StartRunResult
    {
        public bool startedByInvocation;
        public bool attachedToExistingRun;
        public TestRunSnapshot snapshot;
    }

    [Serializable]
    public class TestRunSnapshot
    {
        public string status;
        public string mode;
        public string startedAt;
        public double duration;
        public int total;
        public int passed;
        public int failed;
        public int skipped;
        public int inconclusive;
        public List<TestRunTracker.FailedTestInfo> failedTests;
        public bool startedByInvocation;
        public bool attachedToExistingRun;
    }
}
