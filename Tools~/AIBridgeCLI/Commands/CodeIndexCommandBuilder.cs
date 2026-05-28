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
            "references"
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
            ["references"] = WithLocation()
        };

        private static List<ParameterInfo> CommonParameters()
        {
            return new List<ParameterInfo>
            {
                new ParameterInfo("project-root", "Unity project root. Defaults to current Unity project", false),
                new ParameterInfo("solution", "Explicit .sln path. Defaults to project root .sln", false)
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

        private static List<ParameterInfo> WithCommon(List<ParameterInfo> parameters)
        {
            parameters.AddRange(CommonParameters());
            return parameters;
        }
    }
}
