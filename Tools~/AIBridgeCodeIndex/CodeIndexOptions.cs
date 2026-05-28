using System;
using System.Collections.Generic;
using System.IO;

namespace AIBridgeCodeIndex
{
    internal sealed class CodeIndexOptions
    {
        public string ProjectRoot { get; set; }
        public string SolutionPath { get; set; }
        public string StatusPath { get; set; }
        public string Token { get; set; }
        public int UnityPid { get; set; }

        public static CodeIndexOptions Parse(string[] args)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (!arg.StartsWith("--", StringComparison.Ordinal))
                {
                    continue;
                }

                var key = arg.Substring(2);
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    values[key] = args[i + 1];
                    i++;
                }
                else
                {
                    values[key] = "true";
                }
            }

            values.TryGetValue("project-root", out var projectRoot);
            values.TryGetValue("solution", out var solutionPath);
            values.TryGetValue("status-path", out var statusPath);
            values.TryGetValue("token", out var token);

            var unityPid = 0;
            if (values.TryGetValue("unity-pid", out var unityPidText))
            {
                int.TryParse(unityPidText, out unityPid);
            }

            if (string.IsNullOrWhiteSpace(statusPath) && !string.IsNullOrWhiteSpace(projectRoot))
            {
                statusPath = Path.Combine(projectRoot, ".aibridge", "code-index", "status.json");
            }

            return new CodeIndexOptions
            {
                ProjectRoot = projectRoot,
                SolutionPath = solutionPath,
                StatusPath = statusPath,
                Token = string.IsNullOrWhiteSpace(token) ? Guid.NewGuid().ToString("N") : token,
                UnityPid = unityPid
            };
        }
    }
}
