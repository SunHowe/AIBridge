using System.Collections.Generic;

namespace AIBridgeCLI.Commands
{
    public class TextIndexCommandBuilder : BaseCommandBuilder
    {
        public override string Type => "text_index";
        public override string Description => "CLI-only local indexed text search";

        public override string[] Actions => new[]
        {
            "status",
            "build",
            "search",
            "reset"
        };

        protected override Dictionary<string, List<ParameterInfo>> ActionParameters => new Dictionary<string, List<ParameterInfo>>
        {
            ["status"] = CommonParameters(),
            ["build"] = CommonParameters(),
            ["reset"] = CommonParameters(),
            ["search"] = WithSearchParameters()
        };

        private static List<ParameterInfo> CommonParameters()
        {
            return new List<ParameterInfo>
            {
                new ParameterInfo("project-root", "Unity project root. Defaults to current Unity project", false)
            };
        }

        private static List<ParameterInfo> WithSearchParameters()
        {
            var parameters = CommonParameters();
            parameters.Add(new ParameterInfo("query", "Literal text or regex pattern", true));
            parameters.Add(new ParameterInfo("regex", "Treat query as a regular expression", false, "false"));
            parameters.Add(new ParameterInfo("glob", "Optional path glob filter, comma or semicolon separated", false));
            parameters.Add(new ParameterInfo("path", "Optional indexed path prefix filter", false));
            parameters.Add(new ParameterInfo("max-results", "Maximum result count", false, "100"));
            return parameters;
        }
    }
}
