using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace AIBridge.Editor.Tests
{
    public class CodeCommandTests
    {
        [Test]
        public void Execute_WhenDisabled_ReturnsSettingsFailure()
        {
            var settings = AIBridgeProjectSettings.Instance;
            var previousEnabled = settings.EnableCodeExecution;
            var previousAccepted = settings.CodeExecutionRiskAccepted;
            settings.EnableCodeExecution = false;
            settings.CodeExecutionRiskAccepted = false;

            try
            {
                var result = new CodeCommand().Execute(new CommandRequest
                {
                    id = "code-disabled-test",
                    type = "code",
                    @params = new Dictionary<string, object>
                    {
                        { "action", "execute" },
                        { "code", "return 1;" },
                        { "allowExperimental", true }
                    }
                });

                Assert.That(result.success, Is.False);
                Assert.That(result.error, Does.Contain("disabled"));
                Assert.That(result.data, Is.Not.Null);
            }
            finally
            {
                settings.EnableCodeExecution = previousEnabled;
                settings.CodeExecutionRiskAccepted = previousAccepted;
            }
        }

        [Test]
        public void SkillDescriptionDocumentsSafetyGates()
        {
            var description = new CodeCommand().SkillDescription;

            Assert.That(description, Does.Contain("disabled by default"));
            Assert.That(description, Does.Contain("--allow-experimental true"));
            Assert.That(description, Does.Contain(".aibridge/code"));
        }

        [Test]
        public void Execute_WhenRiskNotAccepted_ReturnsSettingsFailureEvenWithCliAllow()
        {
            var settings = AIBridgeProjectSettings.Instance;
            var previousEnabled = settings.EnableCodeExecution;
            var previousAccepted = settings.CodeExecutionRiskAccepted;
            settings.EnableCodeExecution = true;
            settings.CodeExecutionRiskAccepted = false;

            try
            {
                var result = ExecuteInline("return 1;", true);

                Assert.That(result.success, Is.False);
                Assert.That(result.error, Does.Contain("disabled"));
                Assert.That(result.data, Is.Not.Null);
            }
            finally
            {
                settings.EnableCodeExecution = previousEnabled;
                settings.CodeExecutionRiskAccepted = previousAccepted;
            }
        }

        [Test]
        public void Execute_WhenAllowExperimentalMissing_ReturnsFailureBeforeCompilation()
        {
            var settings = AIBridgeProjectSettings.Instance;
            var previousEnabled = settings.EnableCodeExecution;
            var previousAccepted = settings.CodeExecutionRiskAccepted;
            settings.EnableCodeExecution = true;
            settings.CodeExecutionRiskAccepted = true;

            try
            {
                var result = ExecuteInline("return 1;", false);

                Assert.That(result.success, Is.False);
                Assert.That(result.error, Does.Contain("--allow-experimental true"));
            }
            finally
            {
                settings.EnableCodeExecution = previousEnabled;
                settings.CodeExecutionRiskAccepted = previousAccepted;
            }
        }

        [Test]
        public void Execute_WhenFileOutsideCodeDirectory_ReturnsSourceFailure()
        {
            var settings = AIBridgeProjectSettings.Instance;
            var previousEnabled = settings.EnableCodeExecution;
            var previousAccepted = settings.CodeExecutionRiskAccepted;
            settings.EnableCodeExecution = true;
            settings.CodeExecutionRiskAccepted = true;

            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            var outsideFile = Path.Combine(projectRoot, ".aibridge", "outside.csx");

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outsideFile));
                File.WriteAllText(outsideFile, "return 1;");

                var result = new CodeCommand().Execute(new CommandRequest
                {
                    id = "code-outside-file-test",
                    type = "code",
                    @params = new Dictionary<string, object>
                    {
                        { "action", "execute" },
                        { "file", outsideFile },
                        { "allowExperimental", true }
                    }
                });

                Assert.That(result.success, Is.False);
                Assert.That(result.error, Does.Contain(".aibridge/code"));
            }
            finally
            {
                if (File.Exists(outsideFile))
                {
                    File.Delete(outsideFile);
                }

                settings.EnableCodeExecution = previousEnabled;
                settings.CodeExecutionRiskAccepted = previousAccepted;
            }
        }

        [TestCase("2019.4.40f1", "7.3")]
        [TestCase("2020.1.17f1", "7.3")]
        [TestCase("2020.2.7f1", "8.0")]
        [TestCase("2020.3.48f1", "8.0")]
        [TestCase("2021.1.28f1", "8.0")]
        [TestCase("2021.2.0f1", "9.0")]
        [TestCase("2022.3.20f1", "9.0")]
        [TestCase("6000.3.0f1", "9.0")]
        public void GetSupportedCSharpLanguageVersion_MatchesUnityCompilerVersion(string unityVersion, string expected)
        {
            Assert.That(CodeCommand.GetSupportedCSharpLanguageVersion(unityVersion), Is.EqualTo(expected));
        }

        private static CommandResult ExecuteInline(string code, bool allowExperimental)
        {
            var request = new CommandRequest
            {
                id = "code-inline-test",
                type = "code",
                @params = new Dictionary<string, object>
                {
                    { "action", "execute" },
                    { "code", code }
                }
            };

            if (allowExperimental)
            {
                request.@params["allowExperimental"] = true;
            }

            return new CodeCommand().Execute(request);
        }
    }
}
