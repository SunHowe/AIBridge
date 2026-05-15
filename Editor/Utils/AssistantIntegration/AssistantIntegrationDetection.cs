using System.IO;

namespace AIBridge.Editor
{
    internal sealed class AssistantIntegrationDetection
    {
        public string TargetId { get; set; }
        public bool IsDetected { get; set; }
        public string Detail { get; set; }
    }

    internal static class AssistantIntegrationDetector
    {
        public static AssistantIntegrationDetection Detect(string projectRoot, AssistantIntegrationTarget target)
        {
            var rootRuleFileName = target.RootRuleFileName;
            if (!string.IsNullOrEmpty(rootRuleFileName))
            {
                var rootRulePath = Path.Combine(projectRoot, rootRuleFileName);
                if (File.Exists(rootRulePath))
                {
                    return new AssistantIntegrationDetection
                    {
                        TargetId = target.Id,
                        IsDetected = true,
                        Detail = rootRuleFileName
                    };
                }

                var rootRuleDirectory = Path.GetDirectoryName(rootRulePath);
                if (!string.IsNullOrEmpty(rootRuleDirectory) && Directory.Exists(rootRuleDirectory))
                {
                    var relativeDirectory = Path.GetDirectoryName(rootRuleFileName.Replace('/', Path.DirectorySeparatorChar));
                    return new AssistantIntegrationDetection
                    {
                        TargetId = target.Id,
                        IsDetected = true,
                        Detail = string.IsNullOrEmpty(relativeDirectory) ? rootRuleFileName : relativeDirectory.Replace(Path.DirectorySeparatorChar, '/')
                    };
                }
            }

            if (target.SupportsSkillDirectory)
            {
                var resolvedSkillDirectory = target.GetResolvedSkillDirectoryRelativePath(projectRoot);
                if (!string.IsNullOrEmpty(resolvedSkillDirectory))
                {
                    var skillDirPath = Path.Combine(projectRoot, resolvedSkillDirectory.Replace('/', Path.DirectorySeparatorChar));
                    if (Directory.Exists(skillDirPath))
                    {
                        return new AssistantIntegrationDetection
                        {
                            TargetId = target.Id,
                            IsDetected = true,
                            Detail = resolvedSkillDirectory
                        };
                    }
                }

                var relativeSkillPath = target.GetResolvedSkillFileRelativePath(projectRoot);
                if (!string.IsNullOrEmpty(relativeSkillPath))
                {
                    var skillFilePath = Path.Combine(projectRoot, relativeSkillPath.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(skillFilePath))
                    {
                        return new AssistantIntegrationDetection
                        {
                            TargetId = target.Id,
                            IsDetected = true,
                            Detail = relativeSkillPath
                        };
                    }
                }
            }

            return new AssistantIntegrationDetection
            {
                TargetId = target.Id,
                IsDetected = false,
                Detail = BuildExpectedSignal(target)
            };
        }

        private static string BuildExpectedSignal(AssistantIntegrationTarget target)
        {
            if (target.SupportsSkillDirectory)
            {
                var relativeSkillPath = target.GetResolvedSkillFileRelativePath(null);
                if (!string.IsNullOrEmpty(target.RootRuleFileName) && !string.IsNullOrEmpty(relativeSkillPath))
                {
                    return target.RootRuleFileName + " or " + relativeSkillPath;
                }

                if (!string.IsNullOrEmpty(relativeSkillPath))
                {
                    return relativeSkillPath;
                }
            }

            return target.RootRuleFileName ?? target.DisplayName;
        }
    }
}
