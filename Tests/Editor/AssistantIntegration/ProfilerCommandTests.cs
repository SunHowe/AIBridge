using System.IO;
using AIBridge.Runtime.Diagnostics;
using NUnit.Framework;

namespace AIBridge.Editor.Tests
{
    public class ProfilerCommandTests
    {
        [Test]
        public void GetStatusReturnsEditorSnapshot()
        {
            var command = new ProfilerCommand();
            var request = new CommandRequest
            {
                id = "profiler-status-test",
                type = "profiler",
                @params = new System.Collections.Generic.Dictionary<string, object>
                {
                    ["action"] = "get_status"
                }
            };

            var result = command.Execute(request);

            Assert.IsTrue(result.success, result.error);
            var snapshot = result.data as ProfilerSnapshot;
            Assert.IsNotNull(snapshot);
            Assert.AreEqual("editor", snapshot.source);
            Assert.IsNotNull(snapshot.stats);
            Assert.IsNotNull(snapshot.stats.status);
            Assert.IsNotNull(snapshot.stats.modules);
        }

        [Test]
        public void CaptureFrameReturnsFrameStats()
        {
            var command = new ProfilerCommand();
            var request = new CommandRequest
            {
                id = "profiler-frame-test",
                type = "profiler",
                @params = new System.Collections.Generic.Dictionary<string, object>
                {
                    ["action"] = "capture_frame"
                }
            };

            var result = command.Execute(request);
            var snapshot = result.data as ProfilerSnapshot;

            Assert.IsTrue(result.success, result.error);
            Assert.IsNotNull(snapshot);
            Assert.IsNotNull(snapshot.stats.frame);
        }

        [Test]
        public void SaveAndLoadDataRoundTripsJsonSnapshot()
        {
            var command = new ProfilerCommand();
            var path = Path.Combine(Path.GetTempPath(), "aibridge-profiler-test-" + System.Guid.NewGuid().ToString("N") + ".json");

            try
            {
                var save = command.Execute(new CommandRequest
                {
                    id = "profiler-save-test",
                    type = "profiler",
                    @params = new System.Collections.Generic.Dictionary<string, object>
                    {
                        ["action"] = "save_data",
                        ["path"] = path
                    }
                });

                Assert.IsTrue(save.success, save.error);
                Assert.IsTrue(File.Exists(path));

                var load = command.Execute(new CommandRequest
                {
                    id = "profiler-load-test",
                    type = "profiler",
                    @params = new System.Collections.Generic.Dictionary<string, object>
                    {
                        ["action"] = "load_data",
                        ["path"] = path
                    }
                });

                Assert.IsTrue(load.success, load.error);
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }
}
