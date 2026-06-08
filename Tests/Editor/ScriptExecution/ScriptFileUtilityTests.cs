using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AIBridge.Editor.ScriptExecution;
using NUnit.Framework;
using UnityEngine;

namespace AIBridge.Editor.Tests
{
    public class ScriptFileUtilityTests
    {
        [Test]
        public void FindScripts_UsesConfiguredDirectoryForBatchAndCodeScripts()
        {
            var batchRoot = Path.Combine(Path.GetTempPath(), "aibridge_scripts_" + Guid.NewGuid().ToString("N"));
            var subdir = Path.Combine(batchRoot, "subdir");
            var directCsx = Path.Combine(batchRoot, "asset.csx");
            var directCs = Path.Combine(batchRoot, "asset.cs");
            var nestedCsx = Path.Combine(subdir, "nested.csx");
            var batchScript = Path.Combine(batchRoot, "batch.txt");
            var outsideCsx = Path.Combine(Path.GetTempPath(), "aibridge_outside_" + Guid.NewGuid().ToString("N") + ".csx");

            try
            {
                Directory.CreateDirectory(batchRoot);
                Directory.CreateDirectory(subdir);
                File.WriteAllText(batchScript, "log \"hello\"");
                File.WriteAllText(directCsx, "return 1;");
                File.WriteAllText(directCs, "return 2;");
                File.WriteAllText(nestedCsx, "return 3;");
                File.WriteAllText(outsideCsx, "return 4;");

                var entries = ScriptFileUtility.FindScripts(batchRoot);
                var paths = entries.Select(entry => Path.GetFullPath(entry.Path)).ToList();

                Assert.That(paths, Does.Contain(Path.GetFullPath(batchScript)));
                Assert.That(paths, Does.Contain(Path.GetFullPath(directCsx)));
                Assert.That(paths, Does.Contain(Path.GetFullPath(directCs)));
                Assert.That(paths, Does.Contain(Path.GetFullPath(nestedCsx)));
                Assert.That(paths, Does.Not.Contain(Path.GetFullPath(outsideCsx)));
                Assert.That(entries.First(entry => Path.GetFullPath(entry.Path) == Path.GetFullPath(batchScript)).Kind, Is.EqualTo(ScriptFileKind.Batch));
                Assert.That(entries.First(entry => Path.GetFullPath(entry.Path) == Path.GetFullPath(directCsx)).Kind, Is.EqualTo(ScriptFileKind.CSharp));
            }
            finally
            {
                if (Directory.Exists(batchRoot))
                {
                    Directory.Delete(batchRoot, true);
                }

                DeleteFileIfExists(outsideCsx);
            }
        }

        [Test]
        public void BuildCodeExecuteRequest_CopiesProjectScriptToCodeExecutionCache()
        {
            var projectScript = Path.Combine(Path.GetTempPath(), "aibridge_script_" + Guid.NewGuid().ToString("N") + ".csx");
            File.WriteAllText(projectScript, "return 42;");

            var request = ScriptFileUtility.BuildCodeExecuteRequest(projectScript, 0);
            var executionFile = request.@params["file"].ToString();

            try
            {
                Assert.That(request.type, Is.EqualTo("code"));
                Assert.That(request.@params["action"], Is.EqualTo("execute"));
                Assert.That(executionFile, Does.StartWith(ScriptFileUtility.GetCodeExecutionCacheDirectoryFullPath()));
                Assert.That(File.ReadAllText(executionFile), Is.EqualTo("return 42;"));
                Assert.That(request.@params["timeout"], Is.EqualTo(ScriptFileUtility.DefaultCodeTimeoutMs));
            }
            finally
            {
                DeleteFileIfExists(projectScript);
                DeleteFileIfExists(executionFile);
            }
        }

        [Test]
        public void FormatCodeResult_IncludesStructuredCodeFields()
        {
            var result = CommandResult.Success("code-test", new Dictionary<string, object>
            {
                { "status", "completed" },
                { "source", "file" },
                { "elapsedMs", 12 },
                { "returnValue", "ok" },
                { "logs", new List<object> { "hello" } },
                { "compileErrors", new List<object> { "CS1002" } },
                { "diagnostics", new List<object> { new Dictionary<string, object> { { "code", "CS1002" } } } }
            });

            var text = ScriptFileUtility.FormatCodeResult(result);

            StringAssert.Contains("success: True", text);
            StringAssert.Contains("status: completed", text);
            StringAssert.Contains("returnValue: ok", text);
            StringAssert.Contains("logs:", text);
            StringAssert.Contains("compileErrors:", text);
            StringAssert.Contains("diagnostics:", text);
        }

        private static void DeleteFileIfExists(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
