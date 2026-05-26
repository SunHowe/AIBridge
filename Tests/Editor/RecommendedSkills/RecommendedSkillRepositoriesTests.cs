using System.Linq;
using NUnit.Framework;

namespace AIBridge.Editor.Tests
{
    public class RecommendedSkillRepositoriesTests : RecommendedSkillsTestFixture
    {
        [Test]
        public void DefaultRecommendedSkillRepositoriesIncludeAnthropicSkills()
        {
            var repositories = RecommendedSkillRepositories.GetDefaultRepositories();

            Assert.IsTrue(repositories.Any(repository => repository.Id == "anthropic-skills"
                && repository.RepositoryUrl == "https://github.com/anthropics/skills.git"
                && repository.ManifestRelativePath == ".claude-plugin/marketplace.json"));
        }

        [Test]
        public void DefaultRecommendedSkillRepositoriesIncludeSuperpowersSkills()
        {
            var repositories = RecommendedSkillRepositories.GetDefaultRepositories();

            Assert.IsTrue(repositories.Any(repository => repository.Id == "obra-superpowers"
                && repository.RepositoryUrl == "https://github.com/obra/superpowers.git"
                && repository.ManifestRelativePath == ".claude-plugin/plugin.json"
                && repository.ScanRootRelativePath == "skills"));
        }

        [Test]
        public void DefaultRecommendedSkillRepositoriesUseSuperpowersFirst()
        {
            var repositories = RecommendedSkillRepositories.GetDefaultRepositories();

            Assert.AreEqual("obra-superpowers", repositories.First().Id);
        }

        [Test]
        public void RepositoryWebUrlRemovesGitSuffix()
        {
            var url = AIBridgeSettingsWindow.GetRepositoryWebUrl("https://github.com/obra/superpowers.git");

            Assert.AreEqual("https://github.com/obra/superpowers", url);
        }

        [Test]
        public void DefaultRecommendedSkillRepositoriesUseLocalizedDescriptions()
        {
            AIBridgeProjectSettings.Instance.EditorLanguage = AIBridgeEditorLanguage.English;
            var englishRepositories = RecommendedSkillRepositories.GetDefaultRepositories();

            StringAssert.Contains("workflow Skills", englishRepositories.First().Description);

            AIBridgeProjectSettings.Instance.EditorLanguage = AIBridgeEditorLanguage.SimplifiedChinese;
            var simplifiedChineseRepositories = RecommendedSkillRepositories.GetDefaultRepositories();

            StringAssert.Contains("工作流 Skills", simplifiedChineseRepositories.First().Description);
        }
    }
}
