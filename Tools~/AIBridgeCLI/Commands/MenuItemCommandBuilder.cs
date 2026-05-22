using System.Collections.Generic;
using System.Text;
using AIBridgeCLI.Core;

namespace AIBridgeCLI.Commands
{
    /// <summary>
    /// MenuItem command builder: invoke menu items
    /// </summary>
    public class MenuItemCommandBuilder : BaseCommandBuilder
    {
        public override string Type => "menu_item";
        public override string Description => "Invoke Unity menu items";

        public override string[] Actions => new[] { "invoke" };

        protected override Dictionary<string, List<ParameterInfo>> ActionParameters => new Dictionary<string, List<ParameterInfo>>
        {
            ["invoke"] = new List<ParameterInfo>
            {
                new ParameterInfo("menuPath", "Full menu path (e.g., 'GameObject/Create Empty')", true)
            },
            [""] = new List<ParameterInfo>
            {
                new ParameterInfo("menuPath", "Full menu path (e.g., 'GameObject/Create Empty')", true)
            }
        };

        public override CommandRequest Build(string action, Dictionary<string, string> options)
        {
            // For menu_item, we don't need action parameter
            var request = new CommandRequest
            {
                id = PathHelper.GenerateCommandId(),
                type = Type,
                @params = new Dictionary<string, object>()
            };

            foreach (var kvp in options)
            {
                if (kvp.Key == "json" || kvp.Key == "stdin" || kvp.Key == "timeout" ||
                    kvp.Key == "no-wait" || kvp.Key == "raw" || kvp.Key == "pretty" ||
                    kvp.Key == "quiet" || kvp.Key == "help" || kvp.Key == "on-dialog") continue;
                request.@params[kvp.Key] = ParseValue(kvp.Value);
            }

            return request;
        }

        public override string GetHelp(string action = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{Type}: {Description}");
            sb.AppendLine();
            sb.AppendLine("Parameters:");
            sb.AppendLine("  --menuPath            (required) Full menu path (e.g., 'GameObject/Create Empty')");
            sb.AppendLine();
            sb.AppendLine($"Usage: AIBridgeCLI {Type} --menuPath \"GameObject/Create Empty\"");
            sb.AppendLine($"       AIBridgeCLI {Type} invoke --menuPath \"GameObject/Create Empty\"");
            return sb.ToString();
        }
    }
}
