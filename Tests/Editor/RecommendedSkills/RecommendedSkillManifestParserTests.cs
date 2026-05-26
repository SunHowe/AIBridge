using System.IO;
using System.Linq;
using NUnit.Framework;

namespace AIBridge.Editor.Tests
{
    public class RecommendedSkillManifestParserTests : RecommendedSkillsTestFixture
    {
        [Test]
        public void ReadsPluginSkillList()
        {
            var repositoryRoot = Path.Combine(ProjectRoot, "repo");
            var skillDirectory = Path.Combine(repositoryRoot, "skills", "engineering", "tdd");
            var manifestDirectory = Path.Combine(repositoryRoot, ".claude-plugin");
            Directory.CreateDirectory(skillDirectory);
            Directory.CreateDirectory(manifestDirectory);
            File.WriteAllText(Path.Combine(skillDirectory, "SKILL.md"), "---\nname: tdd\ndescription: Test-driven development workflow.\n---\n# TDD");
            File.WriteAllText(Path.Combine(manifestDirectory, "plugin.json"), "{ \"skills\": [\"./skills/engineering/tdd\"] }");

            var repository = new RecommendedSkillRepository
            {
                Id = "test",
                RepositoryUrl = "https://example.com/repo.git",
                BranchOrTag = "main",
                ManifestRelativePath = ".claude-plugin/plugin.json",
                ScanRootRelativePath = "skills"
            };

            var skills = RecommendedSkillManifestParser.LoadSkills(repository, repositoryRoot, "abc123");

            Assert.AreEqual(1, skills.Count);
            Assert.AreEqual("tdd", skills[0].Name);
            Assert.AreEqual("skills/engineering/tdd", skills[0].SourceRelativePath);
            Assert.AreEqual("abc123", skills[0].Commit);
        }

        [Test]
        public void ReadsMarketplacePluginSkillList()
        {
            var repositoryRoot = Path.Combine(ProjectRoot, "repo");
            var skillDirectory = Path.Combine(repositoryRoot, "skills", "docx");
            var manifestDirectory = Path.Combine(repositoryRoot, ".claude-plugin");
            Directory.CreateDirectory(skillDirectory);
            Directory.CreateDirectory(manifestDirectory);
            File.WriteAllText(Path.Combine(skillDirectory, "SKILL.md"), "---\nname: docx\ndescription: Word document workflow.\n---\n# DOCX");
            File.WriteAllText(Path.Combine(manifestDirectory, "marketplace.json"), "{ \"plugins\": [{ \"name\": \"document-skills\", \"source\": \"./\", \"skills\": [\"./skills/docx\"] }] }");

            var repository = new RecommendedSkillRepository
            {
                Id = "test",
                RepositoryUrl = "https://example.com/repo.git",
                BranchOrTag = "main",
                ManifestRelativePath = ".claude-plugin/marketplace.json",
                ScanRootRelativePath = "skills"
            };

            var skills = RecommendedSkillManifestParser.LoadSkills(repository, repositoryRoot, "abc123");

            Assert.AreEqual(1, skills.Count);
            Assert.AreEqual("docx", skills[0].Name);
            Assert.AreEqual("skills/docx", skills[0].SourceRelativePath);
        }

        [Test]
        public void ScansWhenManifestHasNoSkillList()
        {
            var repositoryRoot = Path.Combine(ProjectRoot, "repo");
            var skillDirectory = Path.Combine(repositoryRoot, "skills", "test-driven-development");
            var manifestDirectory = Path.Combine(repositoryRoot, ".claude-plugin");
            Directory.CreateDirectory(skillDirectory);
            Directory.CreateDirectory(manifestDirectory);
            File.WriteAllText(Path.Combine(skillDirectory, "SKILL.md"), "---\nname: test-driven-development\ndescription: TDD workflow.\n---\n# TDD");
            File.WriteAllText(Path.Combine(manifestDirectory, "plugin.json"), "{ \"name\": \"superpowers\", \"description\": \"Core skills library\" }");

            var repository = new RecommendedSkillRepository
            {
                Id = "test",
                RepositoryUrl = "https://example.com/repo.git",
                BranchOrTag = "main",
                ManifestRelativePath = ".claude-plugin/plugin.json",
                ScanRootRelativePath = "skills"
            };

            var skills = RecommendedSkillManifestParser.LoadSkills(repository, repositoryRoot, "abc123");

            Assert.AreEqual(1, skills.Count);
            Assert.AreEqual("test-driven-development", skills[0].Name);
            Assert.AreEqual("skills/test-driven-development", skills[0].SourceRelativePath);
        }

        [Test]
        public void ScansWhenManifestMissing()
        {
            var repositoryRoot = Path.Combine(ProjectRoot, "repo");
            var skillDirectory = Path.Combine(repositoryRoot, "skills", "diagnose");
            Directory.CreateDirectory(skillDirectory);
            File.WriteAllText(Path.Combine(skillDirectory, "SKILL.md"), "---\nname: diagnose\ndescription: Diagnose problems.\n---\n# Diagnose");

            var repository = new RecommendedSkillRepository
            {
                Id = "test",
                RepositoryUrl = "https://example.com/repo.git",
                BranchOrTag = "main",
                ManifestRelativePath = ".claude-plugin/plugin.json",
                ScanRootRelativePath = "skills"
            };

            var skills = RecommendedSkillManifestParser.LoadSkills(repository, repositoryRoot, "abc123");

            Assert.AreEqual("diagnose", skills.Single().Name);
        }

        [Test]
        public void SkipsPathOutsideRepository()
        {
            var repositoryRoot = Path.Combine(ProjectRoot, "repo");
            var externalSkillDirectory = Path.Combine(ProjectRoot, "external-skill");
            var manifestDirectory = Path.Combine(repositoryRoot, ".claude-plugin");
            Directory.CreateDirectory(externalSkillDirectory);
            Directory.CreateDirectory(manifestDirectory);
            File.WriteAllText(Path.Combine(externalSkillDirectory, "SKILL.md"), "---\nname: unsafe\ndescription: Unsafe path.\n---\n# Unsafe");
            File.WriteAllText(Path.Combine(manifestDirectory, "plugin.json"), "{ \"skills\": [\"../external-skill\"] }");

            var repository = new RecommendedSkillRepository
            {
                Id = "test",
                RepositoryUrl = "https://example.com/repo.git",
                BranchOrTag = "main",
                ManifestRelativePath = ".claude-plugin/plugin.json",
                ScanRootRelativePath = "skills"
            };

            var skills = RecommendedSkillManifestParser.LoadSkills(repository, repositoryRoot, "abc123");

            Assert.AreEqual(0, skills.Count);
        }
    }
}
