using System.Collections.Generic;

namespace AIBridgeCodeIndex
{
    internal sealed class CodeIndexStatus
    {
        public string projectRoot { get; set; }
        public string projectHash { get; set; }
        public int unityPid { get; set; }
        public int daemonPid { get; set; }
        public string endpoint { get; set; }
        public string token { get; set; }
        public string state { get; set; }
        public bool stale { get; set; }
        public string solution { get; set; }
        public int loadedProjects { get; set; }
        public int loadedDocuments { get; set; }
        public string startedAt { get; set; }
        public string updatedAt { get; set; }
        public string message { get; set; }
    }

    internal sealed class CodeIndexRequest
    {
        public string action { get; set; }
        public Dictionary<string, object> parameters { get; set; }
    }

    internal sealed class CodeIndexResponse
    {
        public bool success { get; set; }
        public bool semantic { get; set; }
        public string source { get; set; }
        public string state { get; set; }
        public bool stale { get; set; }
        public string projectRoot { get; set; }
        public string solution { get; set; }
        public int loadedProjects { get; set; }
        public int loadedDocuments { get; set; }
        public string warning { get; set; }
        public string error { get; set; }
        public List<CodeIndexItem> items { get; set; }

        public static CodeIndexResponse FromStatus(CodeIndexStatus status)
        {
            return new CodeIndexResponse
            {
                success = true,
                semantic = status != null && string.Equals(status.state, "ready", System.StringComparison.OrdinalIgnoreCase),
                source = "status",
                state = status == null ? "unknown" : status.state,
                stale = status == null || status.stale,
                projectRoot = status == null ? null : status.projectRoot,
                solution = status == null ? null : status.solution,
                loadedProjects = status == null ? 0 : status.loadedProjects,
                loadedDocuments = status == null ? 0 : status.loadedDocuments
            };
        }
    }

    internal sealed class CodeIndexItem
    {
        public string kind { get; set; }
        public string name { get; set; }
        public string container { get; set; }
        public string file { get; set; }
        public int line { get; set; }
        public int column { get; set; }
        public string signature { get; set; }
        public string preview { get; set; }
        public string severity { get; set; }
        public string id { get; set; }
        public string message { get; set; }
    }
}
