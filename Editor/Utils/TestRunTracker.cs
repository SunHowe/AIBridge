using System;
using System.Collections.Generic;
using NUnit.Framework.Interfaces;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace AIBridge.Editor
{
    /// <summary>
    /// Tracks native Unity test runs for AIBridge test run/status commands.
    /// </summary>
    [InitializeOnLoad]
    public static class TestRunTracker
    {
        private const int MaxKnownRunCount = 64;

        public enum TestRunStatus
        {
            Idle,
            Queued,
            Running,
            Passed,
            Failed,
            Timeout,
            Unknown
        }

        [Serializable]
        public class FailedTestInfo
        {
            public string name;
            public string message;
            public string stackTrace;
        }

        [Serializable]
        public class TestRunFilterInfo
        {
            public string testName;
            public string groupName;
            public string assemblyName;
        }

        private class TestRunState
        {
            public string runId;
            public TestRunStatus status;
            public TestMode modeValue;
            public string mode;
            public string testName;
            public string groupName;
            public string assemblyName;
            public DateTime queuedTime;
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
            public string error;
            public TestRunFilterInfo requestedFilter;
            public TestRunFilterInfo executedFilter;
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
                EnsureQueueUpdate();
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
                    _currentState.error = message;
                    _currentState.failedTests.Add(new FailedTestInfo
                    {
                        name = "TestRunError",
                        message = message,
                        stackTrace = string.Empty
                    });
                }

                LogSummary(_currentState.status);
                EnsureQueueUpdate();
            }
        }

        private static readonly TestCallbacks Callbacks = new TestCallbacks();
        private static readonly Queue<TestRunState> PendingRuns = new Queue<TestRunState>();
        private static readonly Dictionary<string, TestRunState> KnownRuns = new Dictionary<string, TestRunState>(StringComparer.Ordinal);
        private static readonly List<string> KnownRunOrder = new List<string>();
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
        /// Start a new test run. If Unity TestRunner is busy, queue this run instead of reusing unrelated results.
        /// </summary>
        public static StartRunResult StartRun(string runId, TestMode mode, string testName, string groupName, string assemblyName, int timeoutMs)
        {
            Initialize();

            var state = CreateState(runId, mode, testName, groupName, assemblyName, timeoutMs);
            AddKnownRun(state);

            if (IsNativeRunActive() || PendingRuns.Count > 0)
            {
                PendingRuns.Enqueue(state);
                EnsureQueueUpdate();

                return new StartRunResult
                {
                    runId = state.runId,
                    startedByInvocation = false,
                    attachedToExistingRun = false,
                    queuedByInvocation = true,
                    snapshot = BuildSnapshot(state)
                };
            }

            StartNativeRun(state);

            return new StartRunResult
            {
                runId = state.runId,
                startedByInvocation = state.startedByInvocation,
                attachedToExistingRun = false,
                queuedByInvocation = false,
                snapshot = BuildSnapshot(state)
            };
        }

        public static TestRunSnapshot GetSnapshot(string runId = null)
        {
            Initialize();

            if (!string.IsNullOrWhiteSpace(runId))
            {
                if (KnownRuns.TryGetValue(runId, out var knownState))
                {
                    return BuildSnapshot(knownState);
                }

                return BuildUnknownSnapshot(runId);
            }

            var state = _currentState ?? new TestRunState
            {
                status = TestRunStatus.Idle
            };

            return BuildSnapshot(state);
        }

        private static TestRunState CreateState(string runId, TestMode mode, string testName, string groupName, string assemblyName, int timeoutMs)
        {
            var resolvedRunId = string.IsNullOrWhiteSpace(runId)
                ? Guid.NewGuid().ToString("N")
                : runId;

            return new TestRunState
            {
                runId = resolvedRunId,
                status = TestRunStatus.Queued,
                modeValue = mode,
                mode = ModeToString(mode),
                testName = NormalizeFilterValue(testName),
                groupName = NormalizeFilterValue(groupName),
                assemblyName = NormalizeFilterValue(assemblyName),
                queuedTime = DateTime.Now,
                timeoutMs = timeoutMs,
                startedByInvocation = false,
                attachedToExistingRun = false,
                requestedFilter = CreateFilterInfo(testName, groupName, assemblyName)
            };
        }

        private static void StartNativeRun(TestRunState state)
        {
            if (state == null)
            {
                return;
            }

            if (state.status == TestRunStatus.Timeout)
            {
                return;
            }

            if (IsQueuedRunExpired(state))
            {
                MarkTimedOutBeforeStart(state);
                return;
            }

            var filter = new Filter
            {
                testMode = state.modeValue
            };

            AssignFilterValue(state.testName, value => filter.testNames = new[] { value });
            AssignFilterValue(state.groupName, value => filter.groupNames = new[] { value });
            AssignFilterValue(state.assemblyName, value => filter.assemblyNames = new[] { value });

            state.status = TestRunStatus.Running;
            state.startTime = DateTime.Now;
            state.startedByInvocation = true;
            state.attachedToExistingRun = false;
            state.isRunning = true;
            state.executedFilter = CreateFilterInfo(state.testName, state.groupName, state.assemblyName);
            _currentState = state;

            var executionSettings = new ExecutionSettings(filter)
            {
                runSynchronously = false
            };

            try
            {
                _testRunnerApi.Execute(executionSettings);
            }
            catch (Exception ex)
            {
                state.isRunning = false;
                state.endTime = DateTime.Now;
                state.status = TestRunStatus.Failed;
                state.error = ex.Message;
                state.failedTests.Add(new FailedTestInfo
                {
                    name = "TestRunStartError",
                    message = ex.Message,
                    stackTrace = ex.StackTrace
                });
                EnsureQueueUpdate();
            }
        }

        private static void EnsureQueueUpdate()
        {
            EditorApplication.update -= OnQueueUpdate;
            EditorApplication.update += OnQueueUpdate;
        }

        private static void OnQueueUpdate()
        {
            if (IsNativeRunActive())
            {
                return;
            }

            while (PendingRuns.Count > 0)
            {
                var next = PendingRuns.Dequeue();
                if (next.status == TestRunStatus.Timeout)
                {
                    continue;
                }

                if (IsQueuedRunExpired(next))
                {
                    MarkTimedOutBeforeStart(next);
                    continue;
                }

                // Unity TestRunner 是 Editor 级单例；这里只在上一轮完成后启动下一轮，避免不同 filter 互相串结果。
                StartNativeRun(next);
                if (next.isRunning)
                {
                    return;
                }
            }

            EditorApplication.update -= OnQueueUpdate;
        }

        private static bool IsNativeRunActive()
        {
            return _currentState != null && _currentState.isRunning;
        }

        private static bool IsQueuedRunExpired(TestRunState state)
        {
            return state != null
                   && state.status == TestRunStatus.Queued
                   && state.timeoutMs > 0
                   && (DateTime.Now - state.queuedTime).TotalMilliseconds > state.timeoutMs;
        }

        private static void MarkTimedOutBeforeStart(TestRunState state)
        {
            if (state == null)
            {
                return;
            }

            state.status = TestRunStatus.Timeout;
            state.endTime = DateTime.Now;
            state.error = "Test run timed out while waiting in the Unity TestRunner queue.";
        }

        private static TestRunSnapshot BuildSnapshot(TestRunState state)
        {
            if (state == null)
            {
                return new TestRunSnapshot
                {
                    status = StatusToString(TestRunStatus.Idle),
                    queuePosition = -1
                };
            }

            UpdateTimeoutStatus(state);

            var endTime = state.endTime ?? DateTime.Now;
            var durationStart = state.startTime == default ? state.queuedTime : state.startTime;
            var duration = durationStart == default ? 0 : (endTime - durationStart).TotalSeconds;

            return new TestRunSnapshot
            {
                runId = state.runId,
                status = StatusToString(state.status),
                mode = state.mode,
                queuedAt = state.queuedTime == default ? null : state.queuedTime.ToString("o"),
                startedAt = state.startTime == default ? null : state.startTime.ToString("o"),
                duration = Math.Round(duration, 2),
                total = state.total,
                passed = state.passed,
                failed = state.failed,
                skipped = state.skipped,
                inconclusive = state.inconclusive,
                failedTests = new List<FailedTestInfo>(state.failedTests),
                startedByInvocation = state.startedByInvocation,
                attachedToExistingRun = state.attachedToExistingRun,
                queuePosition = GetQueuePosition(state),
                requestedFilter = state.requestedFilter,
                executedFilter = state.executedFilter,
                error = state.error
            };
        }

        private static TestRunSnapshot BuildUnknownSnapshot(string runId)
        {
            return new TestRunSnapshot
            {
                runId = runId,
                status = StatusToString(TestRunStatus.Unknown),
                queuePosition = -1,
                failedTests = new List<FailedTestInfo>(),
                error = "No Unity test run is known for the requested runId yet."
            };
        }

        private static void UpdateTimeoutStatus(TestRunState state)
        {
            if (state == null)
            {
                return;
            }

            if (state.isRunning && state.timeoutMs > 0 && (DateTime.Now - state.startTime).TotalMilliseconds > state.timeoutMs)
            {
                state.status = TestRunStatus.Timeout;
                state.error = "Test run timed out. Unity may still be running tests.";
            }
            else if (IsQueuedRunExpired(state))
            {
                MarkTimedOutBeforeStart(state);
            }
        }

        private static int GetQueuePosition(TestRunState state)
        {
            if (state == null)
            {
                return -1;
            }

            if (state.isRunning)
            {
                return 0;
            }

            var position = 1;
            foreach (var pending in PendingRuns)
            {
                if (ReferenceEquals(pending, state))
                {
                    return position;
                }

                position++;
            }

            return -1;
        }

        private static void AddKnownRun(TestRunState state)
        {
            if (state == null || string.IsNullOrEmpty(state.runId))
            {
                return;
            }

            KnownRuns[state.runId] = state;
            KnownRunOrder.Add(state.runId);
            TrimKnownRuns();
        }

        private static void TrimKnownRuns()
        {
            while (KnownRunOrder.Count > MaxKnownRunCount)
            {
                var oldest = KnownRunOrder[0];
                KnownRunOrder.RemoveAt(0);

                if (_currentState != null && string.Equals(_currentState.runId, oldest, StringComparison.Ordinal))
                {
                    continue;
                }

                var stillPending = false;
                foreach (var pending in PendingRuns)
                {
                    if (string.Equals(pending.runId, oldest, StringComparison.Ordinal))
                    {
                        stillPending = true;
                        break;
                    }
                }

                if (!stillPending)
                {
                    KnownRuns.Remove(oldest);
                }
            }
        }

        private static string NormalizeFilterValue(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        private static TestRunFilterInfo CreateFilterInfo(string testName, string groupName, string assemblyName)
        {
            return new TestRunFilterInfo
            {
                testName = NormalizeFilterValue(testName),
                groupName = NormalizeFilterValue(groupName),
                assemblyName = NormalizeFilterValue(assemblyName)
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
                case TestRunStatus.Queued:
                    return "queued";
                case TestRunStatus.Running:
                    return "running";
                case TestRunStatus.Passed:
                    return "passed";
                case TestRunStatus.Failed:
                    return "failed";
                case TestRunStatus.Timeout:
                    return "timeout";
                case TestRunStatus.Unknown:
                    return "unknown";
                default:
                    return "idle";
            }
        }

        private static void LogSummary(TestRunStatus status)
        {
            var snapshot = GetSnapshot();
            AIBridgeLogger.LogInfo(
                $"Test run {StatusToString(status)}. runId={snapshot.runId}, mode={snapshot.mode}, total={snapshot.total}, passed={snapshot.passed}, failed={snapshot.failed}, skipped={snapshot.skipped}, inconclusive={snapshot.inconclusive}, duration={snapshot.duration:F2}s");
        }
    }

    [Serializable]
    public class StartRunResult
    {
        public string runId;
        public bool startedByInvocation;
        public bool attachedToExistingRun;
        public bool queuedByInvocation;
        public TestRunSnapshot snapshot;
    }

    [Serializable]
    public class TestRunSnapshot
    {
        public string runId;
        public string status;
        public string mode;
        public string queuedAt;
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
        public int queuePosition;
        public TestRunTracker.TestRunFilterInfo requestedFilter;
        public TestRunTracker.TestRunFilterInfo executedFilter;
        public string error;
    }
}
