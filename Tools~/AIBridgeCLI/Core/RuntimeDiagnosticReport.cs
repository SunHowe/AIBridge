using System.Collections.Generic;

namespace AIBridgeCLI.Core
{
    public sealed class RuntimeDiagnosticReport
    {
        public bool success { get; set; }
        public string summary { get; set; }
        public string transport { get; set; }
        public string runtimeDirectory { get; set; }
        public string targetId { get; set; }
        public List<RuntimeDiagnosticCheck> checks { get; set; } = new List<RuntimeDiagnosticCheck>();
        public List<string> suggestions { get; set; } = new List<string>();
    }

    public sealed class RuntimeDiagnosticCheck
    {
        public string name { get; set; }
        public string status { get; set; }
        public string detail { get; set; }
        public string fix { get; set; }
    }
}
