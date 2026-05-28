using System.Collections.Generic;

namespace AIBridgeCLI.Commands
{
    public class CodeIndexCommandBuilder : BaseCommandBuilder
    {
        public override string Type => "code_index";
        public override string Description => "Read-only Roslyn code semantic index";

        public override string[] Actions => new[]
        {
            "status",
            "doctor",
            "warmup",
            "reset",
            "symbol",
            "definition",
            "references",
            "implementations",
            "derived",
            "callers",
            "diagnostics"
        };

        protected override Dictionary<string, List<ParameterInfo>> ActionParameters => new Dictionary<string, List<ParameterInfo>>
        {
            ["status"] = CommonParameters(),
            ["doctor"] = CommonParameters(),
            ["warmup"] = CommonParameters(),
            ["reset"] = CommonParameters(),
            ["symbol"] = WithCommon(new List<ParameterInfo>
            {
                new ParameterInfo("query", "Symbol name or partial name", true),
                new ParameterInfo("fallback", "Fallback to text search when Roslyn is unavailable", false, "true")
            }),
            ["definition"] = WithLocation(),
            ["references"] = WithLocation(),
            ["implementations"] = WithType(),
            ["derived"] = WithType(),
            ["callers"] = WithLocation(),
            ["diagnostics"] = WithCommon(new List<ParameterInfo>
            {
                new ParameterInfo("file", "Optional source file path. Omit for project diagnostics", false),
                new ParameterInfo("fallback", "Fallback to text search when Roslyn is unavailable", false, "true")
            })
        };

        private static List<ParameterInfo> CommonParameters()
        {
            return new List<ParameterInfo>
            {
                new ParameterInfo("project-root", "Unity project root. Defaults to current Unity project", false),
                new ParameterInfo("solution", "Explicit .sln path. Defaults to project root .sln", false),
                new ParameterInfo("unity-pid", "Unity Editor process id to monitor. Daemon exits when the process is gone", false),
                new ParameterInfo("auto-refresh", "Reload the Roslyn workspace automatically when source/project files change", false, "true")
            };
        }

        private static List<ParameterInfo> WithLocation()
        {
            return WithCommon(new List<ParameterInfo>
            {
                new ParameterInfo("file", "Source file path", true),
                new ParameterInfo("line", "1-based line number", true),
                new ParameterInfo("column", "1-based column number", true),
                new ParameterInfo("fallback", "Fallback to text search when Roslyn is unavailable", false, "true")
            });
        }

        private static List<ParameterInfo> WithType()
        {
            return WithCommon(new List<ParameterInfo>
            {
                new ParameterInfo("type", "Type name or fully-qualified metadata/display name", true),
                new ParameterInfo("fallback", "Fallback to text search when Roslyn is unavailable", false, "true")
            });
        }

        private static List<ParameterInfo> WithCommon(List<ParameterInfo> parameters)
        {
            parameters.AddRange(CommonParameters());
            return parameters;
        }
    }
}
