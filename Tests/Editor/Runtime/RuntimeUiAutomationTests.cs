using System.Reflection;
using AIBridge.Runtime;
using NUnit.Framework;

namespace AIBridge.Editor.Tests
{
    public class RuntimeUiAutomationTests
    {
        [Test]
        public void BuiltInActions_ExposeUiSnapshotAndKeyButNotTextInput()
        {
            var field = typeof(AIBridgeRuntime).GetField("BuiltInActions", BindingFlags.NonPublic | BindingFlags.Static);

            Assert.That(field, Is.Not.Null);

            var actions = (string[])field.GetValue(null);
            CollectionAssert.Contains(actions, "runtime.ui.snapshot");
            CollectionAssert.Contains(actions, "runtime.ui.find");
            CollectionAssert.Contains(actions, "runtime.ui.raycast");
            CollectionAssert.Contains(actions, "runtime.ui.click");
            CollectionAssert.Contains(actions, "runtime.input.key");
            CollectionAssert.DoesNotContain(actions, "runtime.input.text");
        }

        [Test]
        public void RuntimeSettings_DefaultToDevelopmentFriendlyAutomation()
        {
            var settings = new AIBridgeRuntimeSettings();

            Assert.That(settings.enableRuntimeBridge, Is.True);
            Assert.That(settings.enableHttpTransport, Is.True);
            Assert.That(settings.enableLanDiscovery, Is.True);
            Assert.That(settings.keepRunningInBackground, Is.True);
            Assert.That(settings.allowInReleaseBuild, Is.False);
        }
    }
}
