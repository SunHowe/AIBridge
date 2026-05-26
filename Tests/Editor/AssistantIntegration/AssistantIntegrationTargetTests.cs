using System.IO;
using NUnit.Framework;

namespace AIBridge.Editor.Tests
{
    public class AssistantIntegrationTargetTests : AssistantIntegrationTestFixture
    {
        [Test]
        public void CodexSkillRootUsesToolDefaultWhenAgentsDirectoryExists()
        {
            Directory.CreateDirectory(Path.Combine(ProjectRoot, ".agents"));
            var target = CreateTarget("codex", ".codex/skills/aibridge");

            Assert.AreEqual(".codex/skills", target.GetResolvedSkillRootDirectoryRelativePath(ProjectRoot));
            Assert.AreEqual(".codex/skills/aibridge", target.GetResolvedSkillDirectoryRelativePath(ProjectRoot));
            Assert.AreEqual(".codex/skills/aibridge/SKILL.md", target.GetResolvedSkillFileRelativePath(ProjectRoot));
            Assert.AreEqual(".codex/skills/aibridge-prefab-patch/SKILL.md", target.GetResolvedSiblingSkillFileRelativePath(ProjectRoot, "aibridge-prefab-patch"));
        }

        [Test]
        public void CodexSkillRootUsesToolDefaultWhenAgentsDirectoryIsMissing()
        {
            var target = CreateTarget("codex", ".codex/skills/aibridge");

            Assert.AreEqual(".codex/skills", target.GetResolvedSkillRootDirectoryRelativePath(ProjectRoot));
            Assert.AreEqual(".codex/skills/aibridge", target.GetResolvedSkillDirectoryRelativePath(ProjectRoot));
        }

        [Test]
        public void LegacyPerAssistantSkillRootDoesNotOverrideToolDefaultDirectory()
        {
            Directory.CreateDirectory(Path.Combine(ProjectRoot, ".agents"));
            AIBridgeProjectSettings.Instance.SetAssistantSkillRootDirectory("codex", ".legacy-skills");
            var target = CreateTarget("codex", ".codex/skills/aibridge");

            Assert.AreEqual(".codex/skills", target.GetResolvedSkillRootDirectoryRelativePath(ProjectRoot));
            Assert.AreEqual(".codex/skills/aibridge", target.GetResolvedSkillDirectoryRelativePath(ProjectRoot));
            Assert.AreEqual(".codex/skills/aibridge-prefab-patch/SKILL.md", target.GetResolvedSiblingSkillFileRelativePath(ProjectRoot, "aibridge-prefab-patch"));
        }

        [Test]
        public void ProjectSkillRootDirectoryCanUseCustomDirectory()
        {
            AIBridgeProjectSettings.Instance.SkillRootDirectory = ".skill";
            var target = CreateTarget("claude", ".claude/skills/aibridge");

            Assert.AreEqual(".skill", target.GetResolvedSkillRootDirectoryRelativePath(ProjectRoot));
            Assert.AreEqual(".skill/aibridge", target.GetResolvedSkillDirectoryRelativePath(ProjectRoot));
            Assert.AreEqual(".skill/aibridge-prefab-patch/SKILL.md", target.GetResolvedSiblingSkillFileRelativePath(ProjectRoot, "aibridge-prefab-patch"));
        }

        [Test]
        public void NonCodexSkillRootUsesToolDefaultDirectory()
        {
            Directory.CreateDirectory(Path.Combine(ProjectRoot, ".agents"));
            var target = CreateTarget("claude", ".claude/skills/aibridge");

            Assert.AreEqual(".claude/skills", target.GetResolvedSkillRootDirectoryRelativePath(ProjectRoot));
            Assert.AreEqual(".claude/skills/aibridge", target.GetResolvedSkillDirectoryRelativePath(ProjectRoot));
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
