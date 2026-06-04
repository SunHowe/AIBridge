using System.IO;
using System.Linq;
using NUnit.Framework;

namespace AIBridge.Editor.Tests
{
    public class SkillInstallerWorkflowOrchestrationTests : AssistantIntegrationTestFixture
    {
        [Test]
        public void WorkflowOrchestrationSkillInstallsWithReferencesByDefault()
        {
            var target = AssistantIntegrationRegistry.GetTargets().First(item => item.Id == "codex");
            AIBridgeProjectSettings.Instance.CodeIndex.EnableCodeIndex = false;

            var results = SkillInstaller.InstallAssistantIntegrations(ProjectRoot, new[] { target });

            var skillDirectory = Path.Combine(ProjectRoot, ".codex", "skills", "aibridge-workflow-orchestration");
            Assert.IsTrue(File.Exists(Path.Combine(skillDirectory, "SKILL.md")));
            Assert.IsTrue(File.Exists(Path.Combine(skillDirectory, "references", "orchestration-patterns.md")));
            Assert.IsTrue(File.Exists(Path.Combine(skillDirectory, "references", "recipe-schema.md")));
            Assert.IsTrue(File.Exists(Path.Combine(skillDirectory, "references", "builtin-recipes.md")));
            Assert.IsFalse(Directory.GetFiles(skillDirectory, "*.meta", SearchOption.AllDirectories).Any());
            Assert.IsTrue(results.Single().AdditionalSkillFilePaths.Any(path => path.Replace('\\', '/').EndsWith("/aibridge-workflow-orchestration/SKILL.md")));
        }

        [Test]
        public void DevelopmentWorkflowRoutesToWorkflowOrchestrationSkill()
        {
            var target = AssistantIntegrationRegistry.GetTargets().First(item => item.Id == "codex");

            SkillInstaller.InstallAssistantIntegrations(ProjectRoot, new[] { target });

            var workflowSkillPath = Path.Combine(ProjectRoot, ".codex", "skills", "aibridge-development-workflow", "SKILL.md");
            var workflowSkill = File.ReadAllText(workflowSkillPath);
            StringAssert.Contains("aibridge-workflow-orchestration", workflowSkill);
            StringAssert.Contains("Workflow recipe", workflowSkill);
            StringAssert.Contains("多 Agent 编排前", workflowSkill);
        }

        [Test]
        public void WorkflowInstallsHarnessReadinessReference()
        {
            var target = AssistantIntegrationRegistry.GetTargets().First(item => item.Id == "codex");

            SkillInstaller.InstallAssistantIntegrations(ProjectRoot, new[] { target });

            var readinessPath = Path.Combine(ProjectRoot, ".codex", "skills", "aibridge-development-workflow", "references", "harness-readiness.md");
            Assert.IsTrue(File.Exists(readinessPath));

            var readiness = File.ReadAllText(readinessPath);
            StringAssert.Contains("Fallback 规则", readiness);
            StringAssert.Contains("Resume 规则", readiness);
            StringAssert.Contains("EvidenceRef", readiness);
            StringAssert.Contains("CommandEvidence", readiness);
        }
    }
}
