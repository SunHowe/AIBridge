using System.IO;
using NUnit.Framework;
using UnityEditor.PackageManager;

namespace AIBridge.Editor.Tests
{
    public class ProfilerCommandBuilderTests
    {
        [Test]
        public void CliSourceRegistersProfilerBuilder()
        {
            var source = ReadCliSource("Commands/CommandRegistry.cs");

            StringAssert.Contains("Register(new ProfilerCommandBuilder())", source);
        }

        [Test]
        public void ProfilerBuilderDocumentsActions()
        {
            var source = ReadCliSource("Commands/ProfilerCommandBuilder.cs");

            StringAssert.Contains("get_memory_stats", source);
            StringAssert.Contains("capture_frame", source);
            StringAssert.Contains("save_data", source);
            StringAssert.Contains("load_data", source);
        }

        [Test]
        public void ProfilerBuilderDocumentsUnknownActionFallback()
        {
            var source = ReadCliSource("Commands/ProfilerCommandBuilder.cs");

            StringAssert.Contains("Unknown profiler action", source);
            StringAssert.Contains("public override string GetHelp(string action = null)", source);
            StringAssert.Contains("return base.GetHelp(null)", source);
        }

        [Test]
        public void ProfilerBuilderDoesNotDocumentDashAliases()
        {
            var source = ReadCliSource("Commands/ProfilerCommandBuilder.cs");

            Assert.IsFalse(source.Contains("profiler-start"));
        }

        private static string ReadCliSource(string relativePath)
        {
            var packageInfo = PackageInfo.FindForAssembly(typeof(AIBridgeProjectSettings).Assembly);
            var packageRoot = packageInfo != null && !string.IsNullOrWhiteSpace(packageInfo.resolvedPath)
                ? packageInfo.resolvedPath
                : Directory.GetCurrentDirectory();
            return File.ReadAllText(Path.Combine(packageRoot, "Tools~", "AIBridgeCLI", relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }
    }
}
