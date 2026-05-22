using System.Collections.Generic;

namespace AIBridgeCLI.Commands
{
    public class DialogStatusResult
    {
        public bool success { get; set; }
        public bool? blockedByDialog { get; set; }
        public string platform { get; set; }
        public int? processId { get; set; }
        public string windowTitle { get; set; }
        public List<DialogInfo> dialogs { get; set; }
        public string error { get; set; }
        public string errorCode { get; set; }
    }

    public class DialogClickResult
    {
        public bool success { get; set; }
        public bool? clicked { get; set; }
        public string platform { get; set; }
        public int? processId { get; set; }
        public string dialogId { get; set; }
        public string buttonId { get; set; }
        public string buttonText { get; set; }
        public string choice { get; set; }
        public DialogInfo dialog { get; set; }
        public DialogStatusResult status { get; set; }
        public string error { get; set; }
        public string errorCode { get; set; }
    }

    public class DialogInfo
    {
        public string id { get; set; }
        public string title { get; set; }
        public string role { get; set; }
        public string subrole { get; set; }
        public string message { get; set; }
        public List<DialogButtonInfo> buttons { get; set; }
    }

    public class DialogButtonInfo
    {
        public string id { get; set; }
        public string text { get; set; }
        public string choice { get; set; }
        public bool enabled { get; set; }
    }
}
