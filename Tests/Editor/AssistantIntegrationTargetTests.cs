using System.IO;
using NUnit.Framework;

namespace AIBridge.Editor.Tests
{
    public class AssistantIntegrationTargetTests
    {
        private string _projectRoot;

        [SetUp]
        public void SetUp()
        {
            _projectRoot = Path.Combine(Path.GetTempPath(), "AIBridgeTargetTests_" + Path.GetRandomFileName());
            Directory.CreateDirectory(_projectRoot);
            AIBridgeProjectSettings.Instance.ClearAssistantSkillRootDirectory("codex");
            AIBridgeProjectSettings.Instance.ClearAssistantSkillRootDirectory("claude");
        }

        [TearDown]
        public void TearDown()
        {
            AIBridgeProjectSettings.Instance.ClearAssistantSkillRootDirectory("codex");
            AIBridgeProjectSettings.Instance.ClearAssistantSkillRootDirectory("claude");

            if (Directory.Exists(_projectRoot))
            {
                Directory.Delete(_projectRoot, true);
            }
        }

        [Test]
        public void CodexSkillRootUsesAgentsWhenAgentsDirectoryExists()
        {
            Directory.CreateDirectory(Path.Combine(_projectRoot, ".agents"));
            var target = CreateTarget("codex", ".codex/skills/aibridge");

            Assert.AreEqual(".agents/skills", target.GetResolvedSkillRootDirectoryRelativePath(_projectRoot));
            Assert.AreEqual(".agents/skills/aibridge", target.GetResolvedSkillDirectoryRelativePath(_projectRoot));
            Assert.AreEqual(".agents/skills/aibridge/SKILL.md", target.GetResolvedSkillFileRelativePath(_projectRoot));
            Assert.AreEqual(".agents/skills/aibridge-prefab-patch/SKILL.md", target.GetResolvedSiblingSkillFileRelativePath(_projectRoot, "aibridge-prefab-patch"));
        }

        [Test]
        public void CodexSkillRootKeepsCodexPathWhenAgentsDirectoryIsMissing()
        {
            var target = CreateTarget("codex", ".codex/skills/aibridge");

            Assert.AreEqual(".codex/skills", target.GetResolvedSkillRootDirectoryRelativePath(_projectRoot));
            Assert.AreEqual(".codex/skills/aibridge", target.GetResolvedSkillDirectoryRelativePath(_projectRoot));
        }

        [Test]
        public void CustomSkillRootOverridesDetectedAgentsDirectory()
        {
            Directory.CreateDirectory(Path.Combine(_projectRoot, ".agents"));
            AIBridgeProjectSettings.Instance.SetAssistantSkillRootDirectory("codex", "skills");
            var target = CreateTarget("codex", ".codex/skills/aibridge");

            Assert.AreEqual("skills", target.GetResolvedSkillRootDirectoryRelativePath(_projectRoot));
            Assert.AreEqual("skills/aibridge", target.GetResolvedSkillDirectoryRelativePath(_projectRoot));
            Assert.AreEqual("skills/aibridge-prefab-patch/SKILL.md", target.GetResolvedSiblingSkillFileRelativePath(_projectRoot, "aibridge-prefab-patch"));
        }

        [Test]
        public void NonCodexSkillRootDoesNotUseAgentsDirectory()
        {
            Directory.CreateDirectory(Path.Combine(_projectRoot, ".agents"));
            var target = CreateTarget("claude", ".claude/skills/aibridge");

            Assert.AreEqual(".claude/skills", target.GetResolvedSkillRootDirectoryRelativePath(_projectRoot));
            Assert.AreEqual(".claude/skills/aibridge", target.GetResolvedSkillDirectoryRelativePath(_projectRoot));
        }

        private static AssistantIntegrationTarget CreateTarget(string id, string skillDirectoryRelativePath)
        {
            return new AssistantIntegrationTarget
            {
                Id = id,
                SupportsSkillDirectory = true,
                SkillDirectoryRelativePath = skillDirectoryRelativePath,
                SkillFileName = "SKILL.md"
            };
        }
    }
}
