using System;
using System.Collections.Generic;
using System.IO;
using AIBridgeCLI.Core;

namespace AIBridgeCLI.Commands
{
    /// <summary>
    /// Prefab command builder: instantiate, save, unpack, get_info, get_hierarchy, apply, patch
    /// </summary>
    public class PrefabCommandBuilder : BaseCommandBuilder
    {
        public override string Type => "prefab";
        public override string Description => "Prefab operations (instantiate, inspect, save, unpack, apply, patch)";

        public override string[] Actions => new[]
        {
            "instantiate", "save", "unpack", "get_info", "get_hierarchy", "apply", "patch"
        };

        protected override Dictionary<string, List<ParameterInfo>> ActionParameters => new Dictionary<string, List<ParameterInfo>>
        {
            ["instantiate"] = new List<ParameterInfo>
            {
                new ParameterInfo("prefabPath", "Path to the prefab asset", true),
                new ParameterInfo("posX", "X position", false, "0"),
                new ParameterInfo("posY", "Y position", false, "0"),
                new ParameterInfo("posZ", "Z position", false, "0")
            },
            ["save"] = new List<ParameterInfo>
            {
                new ParameterInfo("gameObjectPath", "Path to the GameObject (uses selection if not specified)", false),
                new ParameterInfo("savePath", "Path to save the prefab", true)
            },
            ["unpack"] = new List<ParameterInfo>
            {
                new ParameterInfo("gameObjectPath", "Path to the prefab instance (uses selection if not specified)", false),
                new ParameterInfo("completely", "Unpack completely (recursive)", false, "false")
            },
            ["get_info"] = new List<ParameterInfo>
            {
                new ParameterInfo("prefabPath", "Path to the prefab asset", false),
                new ParameterInfo("gameObjectPath", "Path to the prefab instance", false)
            },
            ["get_hierarchy"] = new List<ParameterInfo>
            {
                new ParameterInfo("prefabPath", "Path to the prefab asset", true),
                new ParameterInfo("depth", "Max depth to traverse", false, "5"),
                new ParameterInfo("includeInactive", "Include inactive GameObjects", false, "true"),
                new ParameterInfo("includeComponents", "Include component type names", false, "true")
            },
            ["apply"] = new List<ParameterInfo>
            {
                new ParameterInfo("gameObjectPath", "Path to the prefab instance (uses selection if not specified)", false)
            },
            ["patch"] = new List<ParameterInfo>
            {
                new ParameterInfo("prefabPath", "Path to the prefab asset", true),
                new ParameterInfo("ops", "Path to a JSON patch operations file", false),
                new ParameterInfo("ops-json", "JSON array or object containing patch operations", false),
                new ParameterInfo("dryRun", "Validate and preview without saving", false, "false"),
                new ParameterInfo("dry-run", "Validate and preview without saving", false, "false")
            }
        };

        public override CommandRequest Build(string action, Dictionary<string, string> options)
        {
            if (!string.Equals(action, "patch", StringComparison.OrdinalIgnoreCase))
            {
                return base.Build(action, options);
            }

            var request = base.Build(action, options);

            string opsPath;
            if (options.TryGetValue("ops", out opsPath) && !string.IsNullOrWhiteSpace(opsPath))
            {
                if (!File.Exists(opsPath))
                {
                    throw new ArgumentException($"Patch operations file not found: {opsPath}");
                }

                request.@params.Remove("ops");
                request.@params["opsJson"] = File.ReadAllText(opsPath);
            }

            string opsJson;
            if (options.TryGetValue("ops-json", out opsJson) && !string.IsNullOrWhiteSpace(opsJson))
            {
                request.@params.Remove("ops-json");
                request.@params["opsJson"] = opsJson;
            }

            if (!request.@params.ContainsKey("opsJson") && !request.@params.ContainsKey("ops"))
            {
                throw new ArgumentException("Missing patch operations. Use --ops <file> or --ops-json <json>.");
            }

            return request;
        }
    }
}
