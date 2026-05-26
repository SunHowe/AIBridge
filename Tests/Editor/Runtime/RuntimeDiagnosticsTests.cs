using AIBridge.Runtime;
using AIBridge.Runtime.Diagnostics;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AIBridge.Editor.Tests
{
    public class RuntimeDiagnosticsTests
    {
        [Test]
        public void Percentile_InterpolatesSortedValues()
        {
            var values = new[] { 10d, 20d, 30d, 40d };

            Assert.That(RuntimePercentile.Calculate(values, 50d), Is.EqualTo(25d));
            Assert.That(RuntimePercentile.Calculate(values, 95d), Is.EqualTo(38.5d).Within(0.0001d));
        }

        [Test]
        public void LogBuffer_ClearAndFiltersWork()
        {
            var buffer = new AIBridgeRuntimeLogBuffer();
            buffer.Initialize(10);

            try
            {
                LogAssert.Expect(LogType.Log, "aibridge-runtime-log-buffer-test");
                Debug.Log("aibridge-runtime-log-buffer-test");

                var entries = buffer.GetEntries(10, "Log", "runtime-log-buffer", false, Time.frameCount, null);
                Assert.That(entries.Length, Is.EqualTo(1));
                Assert.That(entries[0].stackTrace, Is.Null);

                Assert.That(buffer.Clear(), Is.EqualTo(1));
                Assert.That(buffer.Count, Is.EqualTo(0));
            }
            finally
            {
                buffer.Dispose();
            }
        }
    }
}
