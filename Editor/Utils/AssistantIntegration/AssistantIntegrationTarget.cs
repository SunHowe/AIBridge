using System;
using System.IO;

namespace AIBridge.Editor
{
    internal enum MissingRootRuleStrategy
    {
        Skip,
        CreateMinimalFile,
        CreateWithInjectedBlock
    }

    internal sealed class AssistantIntegrationTarget
    {
        private const string CodexTargetId = "codex";
        private const string AgentsDirectoryName = ".agents";
        private const string AgentsSkillRootDirectory = ".agents/skills";

        public string Id { get; set; }
        public string DisplayName { get; set; }
        public bool SupportsSkillDirectory { get; set; }
        public string RootRuleFileName { get; set; }
        public string SkillDirectoryRelativePath { get; set; }
        public string SkillFileName { get; set; }
        public string RootRuleTemplateRelativePath { get; set; }
        public MissingRootRuleStrategy MissingRootRuleStrategy { get; set; }
        public string TemplateId { get; set; }
        public string RuleTarget { get; set; }

        public string GetSkillFileRelativePath()
        {
            if (!SupportsSkillDirectory || string.IsNullOrEmpty(SkillDirectoryRelativePath) || string.IsNullOrEmpty(SkillFileName))
            {
                return null;
            }

            return SkillDirectoryRelativePath.TrimEnd('/', '\\') + "/" + SkillFileName;
        }

        public string GetDefaultSkillRootDirectoryRelativePath()
        {
            if (!SupportsSkillDirectory || string.IsNullOrEmpty(SkillDirectoryRelativePath))
            {
                return null;
            }

            var normalized = NormalizeRelativePath(SkillDirectoryRelativePath);
            var separatorIndex = normalized.LastIndexOf('/');
            return separatorIndex >= 0 ? normalized.Substring(0, separatorIndex) : string.Empty;
        }

        public string GetSkillDirectoryName()
        {
            if (string.IsNullOrEmpty(SkillDirectoryRelativePath))
            {
                return null;
            }

            var normalized = NormalizeRelativePath(SkillDirectoryRelativePath);
            var separatorIndex = normalized.LastIndexOf('/');
            return separatorIndex >= 0 ? normalized.Substring(separatorIndex + 1) : normalized;
        }

        public string GetResolvedSkillRootDirectoryRelativePath(string projectRoot)
        {
            if (!SupportsSkillDirectory)
            {
                return null;
            }

            string customRootDirectory;
            if (AIBridgeProjectSettings.Instance.TryGetAssistantSkillRootDirectory(Id, out customRootDirectory))
            {
                return customRootDirectory;
            }

            // Codex 项目如果已存在 .agents，则优先采用开放标准的 .agents/skills 根目录。
            if (string.Equals(Id, CodexTargetId, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(projectRoot)
                && Directory.Exists(Path.Combine(projectRoot, AgentsDirectoryName)))
            {
                return AgentsSkillRootDirectory;
            }

            return GetDefaultSkillRootDirectoryRelativePath();
        }

        public string GetResolvedSkillDirectoryRelativePath(string projectRoot)
        {
            var skillDirectoryName = GetSkillDirectoryName();
            if (string.IsNullOrEmpty(skillDirectoryName))
            {
                return null;
            }

            var skillRootDirectory = GetResolvedSkillRootDirectoryRelativePath(projectRoot);
            return string.IsNullOrEmpty(skillRootDirectory)
                ? skillDirectoryName
                : NormalizeRelativePath(skillRootDirectory) + "/" + skillDirectoryName;
        }

        public string GetResolvedSkillFileRelativePath(string projectRoot)
        {
            if (!SupportsSkillDirectory || string.IsNullOrEmpty(SkillFileName))
            {
                return null;
            }

            var skillDirectory = GetResolvedSkillDirectoryRelativePath(projectRoot);
            return string.IsNullOrEmpty(skillDirectory) ? null : skillDirectory + "/" + SkillFileName;
        }

        public string GetResolvedSiblingSkillFileRelativePath(string projectRoot, string skillDirectoryName)
        {
            if (!SupportsSkillDirectory || string.IsNullOrEmpty(skillDirectoryName) || string.IsNullOrEmpty(SkillFileName))
            {
                return null;
            }

            var skillRootDirectory = GetResolvedSkillRootDirectoryRelativePath(projectRoot);
            return string.IsNullOrEmpty(skillRootDirectory)
                ? skillDirectoryName + "/" + SkillFileName
                : NormalizeRelativePath(skillRootDirectory) + "/" + skillDirectoryName + "/" + SkillFileName;
        }

        public string GetSiblingSkillFileRelativePath(string skillDirectoryName)
        {
            if (!SupportsSkillDirectory || string.IsNullOrEmpty(SkillDirectoryRelativePath) || string.IsNullOrEmpty(SkillFileName))
            {
                return null;
            }

            var normalized = SkillDirectoryRelativePath.Replace('\\', '/').TrimEnd('/');
            var separatorIndex = normalized.LastIndexOf('/');
            var skillRoot = separatorIndex >= 0 ? normalized.Substring(0, separatorIndex) : string.Empty;
            return string.IsNullOrEmpty(skillRoot)
                ? skillDirectoryName + "/" + SkillFileName
                : skillRoot + "/" + skillDirectoryName + "/" + SkillFileName;
        }

        private static string NormalizeRelativePath(string value)
        {
            return value.Replace('\\', '/').Trim('/');
        }
    }
}
