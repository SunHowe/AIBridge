using System.Linq;
using NUnit.Framework;

namespace AIBridge.Editor.Tests
{
    public class RuleTemplateTests : AssistantIntegrationTestFixture
    {
        [Test]
        public void AssistantTargetsUseSharedRootRuleTemplate()
        {
            var targets = AssistantIntegrationRegistry.GetTargets();

            Assert.IsTrue(targets.All(target => target.RootRuleTemplateRelativePath == "Templates~/Rules/AIBridge.RootRule.md"));
        }

        [Test]
        public void SharedRootRuleTemplateRoutesThroughWorkflowWithoutSkillIndex()
        {
            var template = RuleTemplateLoader.Load(ProjectRoot, "Templates~/Rules/AIBridge.RootRule.md");

            StringAssert.Contains("{{WORKFLOW_SKILL_ENTRY}}", template.Body);
            StringAssert.Contains("{{SKILL_ROOT_RULE}}", template.Body);
            StringAssert.Contains("{{UNITY_VERSION_RULE}}", template.Body);
            StringAssert.Contains("{{CSHARP_VERSION_RULE}}", template.Body);
            Assert.IsFalse(template.Body.Contains("{{SKILL_INDEX}}"));
        }

        [Test]
        public void ProjectAgentsTemplateVersionTokensAreRendered()
        {
            var template = RuleTemplateLoader.Load(ProjectRoot, "Templates~/ProjectRules/AGENTS.zh-CN.md");

            var rendered = SkillInstaller.ApplyProjectVersionTokens(template.Body);

            Assert.IsFalse(rendered.Contains("{{UNITY_VERSION}}"));
            Assert.IsFalse(rendered.Contains("{{CSHARP_LANGUAGE_VERSION}}"));
            StringAssert.Contains(UnityEngine.Application.unityVersion, rendered);
            StringAssert.Contains("C# ", rendered);
        }
    }
}
