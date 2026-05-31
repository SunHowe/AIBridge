using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace AIBridge.Runtime
{
    [Serializable]
    public class AIBridgeRuntimeLogEntry
    {
        public string type;
        public string message;
        public string stackTrace;
        public long timestamp;
        public int frame;
    }

    public sealed class AIBridgeRuntimeLogBuffer : IDisposable
    {
        private const int UnknownFrame = -1;

        private readonly object _syncRoot = new object();
        private readonly List<AIBridgeRuntimeLogEntry> _entries = new List<AIBridgeRuntimeLogEntry>();
        private int _capacity = 500;
        private bool _initialized;
        private int _mainThreadId;

        public int Count
        {
            get
            {
                lock (_syncRoot)
                {
                    return _entries.Count;
                }
            }
        }

        public void Initialize(int capacity)
        {
            _capacity = Math.Max(1, capacity);
            if (_initialized)
            {
                return;
            }

            _mainThreadId = Environment.CurrentManagedThreadId;
            Application.logMessageReceivedThreaded += OnLogMessageReceived;
            _initialized = true;
        }

        public void Dispose()
        {
            if (!_initialized)
            {
                return;
            }

            Application.logMessageReceivedThreaded -= OnLogMessageReceived;
            _initialized = false;
        }

        public int Clear()
        {
            lock (_syncRoot)
            {
                var count = _entries.Count;
                _entries.Clear();
                return count;
            }
        }

        public AIBridgeRuntimeLogEntry[] GetEntries(int count, string logType, string regexPattern, bool includeStackTrace)
        {
            return GetEntries(count, logType, regexPattern, includeStackTrace, null, null);
        }

        public AIBridgeRuntimeLogEntry[] GetEntries(
            int count,
            string logType,
            string regexPattern,
            bool includeStackTrace,
            int? sinceFrame,
            long? sinceTimestamp)
        {
            count = Math.Max(1, count);
            Regex regex = null;
            if (!string.IsNullOrEmpty(regexPattern))
            {
                regex = new Regex(regexPattern);
            }

            var results = new List<AIBridgeRuntimeLogEntry>();
            lock (_syncRoot)
            {
                for (var i = _entries.Count - 1; i >= 0 && results.Count < count; i--)
                {
                    var entry = _entries[i];
                    if (!MatchesLogType(logType, entry.type))
                    {
                        continue;
                    }

                    if (sinceFrame.HasValue && entry.frame != UnknownFrame && entry.frame < sinceFrame.Value)
                    {
                        continue;
                    }

                    if (sinceTimestamp.HasValue && entry.timestamp < sinceTimestamp.Value)
                    {
                        continue;
                    }

                    if (regex != null && !regex.IsMatch(entry.message ?? string.Empty))
                    {
                        continue;
                    }

                    results.Add(CloneEntry(entry, includeStackTrace));
                }
            }

            results.Reverse();
            return results.ToArray();
        }

        private void OnLogMessageReceived(string condition, string stackTrace, LogType type)
        {
            var entry = new AIBridgeRuntimeLogEntry
            {
                type = NormalizeLogType(type),
                message = Truncate(condition, 4096),
                stackTrace = Truncate(stackTrace, 8192),
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                frame = GetFrameForCurrentThread()
            };

            lock (_syncRoot)
            {
                _entries.Add(entry);
                while (_entries.Count > _capacity)
                {
                    _entries.RemoveAt(0);
                }
            }
        }

        private static AIBridgeRuntimeLogEntry CloneEntry(AIBridgeRuntimeLogEntry entry, bool includeStackTrace)
        {
            return new AIBridgeRuntimeLogEntry
            {
                type = entry.type,
                message = entry.message,
                stackTrace = includeStackTrace ? entry.stackTrace : null,
                timestamp = entry.timestamp,
                frame = entry.frame
            };
        }

        private int GetFrameForCurrentThread()
        {
            // logMessageReceivedThreaded 可能在工作线程触发，非主线程不能访问 UnityEngine.Time。
            return Environment.CurrentManagedThreadId == _mainThreadId ? Time.frameCount : UnknownFrame;
        }

        private static bool MatchesLogType(string requestedType, string entryType)
        {
            if (string.IsNullOrEmpty(requestedType) || string.Equals(requestedType, "all", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(requestedType, entryType, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Error 查询默认覆盖 Unity 的 Exception/Assert，便于一次拿到真实失败日志。
            return string.Equals(requestedType, "Error", StringComparison.OrdinalIgnoreCase)
                && (string.Equals(entryType, "Exception", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(entryType, "Assert", StringComparison.OrdinalIgnoreCase));
        }

        private static string NormalizeLogType(LogType type)
        {
            switch (type)
            {
                case LogType.Warning:
                    return "Warning";
                case LogType.Error:
                    return "Error";
                case LogType.Assert:
                    return "Assert";
                case LogType.Exception:
                    return "Exception";
                default:
                    return "Log";
            }
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            {
                return value;
            }

            return value.Substring(0, maxLength) + "...";
        }
    }
}
