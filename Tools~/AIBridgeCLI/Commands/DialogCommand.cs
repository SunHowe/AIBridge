using System;
using System.Text;
using Newtonsoft.Json;

namespace AIBridgeCLI.Commands
{
    public static class DialogCommand
    {
        private const int DefaultWaitTimeoutMs = 5000;

        public static int Execute(string action, Func<string, bool> hasOption, Func<string, string> getOption, bool pretty)
        {
            var normalizedAction = string.IsNullOrWhiteSpace(action) ? "status" : action.Trim().ToLowerInvariant();
            switch (normalizedAction)
            {
                case "status":
                case "list":
                    return PrintStatus(DialogService.GetStatus(), pretty);
                case "click":
                    return PrintClick(DialogService.Click(getOption("choice"), getOption("button"), getOption("dialog-id")), pretty);
                case "wait":
                    return ExecuteWait(hasOption, getOption, pretty);
                case "help":
                case "--help":
                    Console.WriteLine(GetHelp());
                    return 0;
                default:
                    Console.Error.WriteLine("Error: Unknown dialog action: " + action);
                    Console.WriteLine(GetHelp());
                    return 1;
            }
        }

        public static string GetHelp()
        {
            var sb = new StringBuilder();
            sb.AppendLine("dialog: Inspect and click Unity modal dialogs (CLI-only)");
            sb.AppendLine();
            sb.AppendLine("Actions:");
            sb.AppendLine("  status       Inspect Unity modal dialogs");
            sb.AppendLine("  list         Alias of status");
            sb.AppendLine("  click        Click a dialog button by --choice or --button");
            sb.AppendLine("  wait         Wait until dialogs disappear; optionally click first");
            sb.AppendLine();
            sb.AppendLine("Usage:");
            sb.AppendLine("  AIBridgeCLI dialog status");
            sb.AppendLine("  AIBridgeCLI dialog click --choice cancel");
            sb.AppendLine("  AIBridgeCLI dialog click --button \"Don't Save\"");
            sb.AppendLine("  AIBridgeCLI dialog wait --timeout 5000");
            sb.AppendLine("  AIBridgeCLI dialog wait --timeout 5000 --click cancel");
            sb.AppendLine();
            sb.AppendLine("Options:");
            sb.AppendLine("  --choice <name>      Logical choice: cancel, save, discard, ok, yes, no, delete, replace");
            sb.AppendLine("  --button <text>      Exact visible button text");
            sb.AppendLine("  --dialog-id <id>     Dialog id from status/list output");
            sb.AppendLine("  --click <choice>     For wait: click one button before waiting");
            sb.AppendLine("  --timeout <ms>       Wait timeout in milliseconds");
            return sb.ToString();
        }

        private static int ExecuteWait(Func<string, bool> hasOption, Func<string, string> getOption, bool pretty)
        {
            if (hasOption("click"))
            {
                var clickResult = DialogService.Click(getOption("click"), getOption("button"), getOption("dialog-id"));
                if (!clickResult.success)
                {
                    return PrintClick(clickResult, pretty);
                }
            }

            var timeout = DefaultWaitTimeoutMs;
            var timeoutValue = getOption("timeout");
            if (!string.IsNullOrWhiteSpace(timeoutValue) && int.TryParse(timeoutValue, out var parsedTimeout))
            {
                timeout = parsedTimeout;
            }

            return PrintStatus(DialogService.Wait(timeout), pretty);
        }

        private static int PrintStatus(DialogStatusResult result, bool pretty)
        {
            if (!pretty)
            {
                Console.WriteLine(JsonConvert.SerializeObject(result, Formatting.None, JsonSettings()));
                return result.success ? 0 : 1;
            }

            if (!result.success)
            {
                Console.Error.WriteLine("Error: " + result.error);
                if (!string.IsNullOrWhiteSpace(result.errorCode))
                {
                    Console.Error.WriteLine("Code: " + result.errorCode);
                }
                return 1;
            }

            if (!DialogService.HasBlockingDialog(result))
            {
                Console.WriteLine("No Unity modal dialogs detected.");
                return 0;
            }

            Console.WriteLine("Unity is blocked by modal dialog(s):");
            PrintDialogs(result.dialogs, "  ");

            return 0;
        }

        private static int PrintClick(DialogClickResult result, bool pretty)
        {
            if (!pretty)
            {
                Console.WriteLine(JsonConvert.SerializeObject(result, Formatting.None, JsonSettings()));
                return result.success ? 0 : 1;
            }

            if (result.success)
            {
                Console.WriteLine("Clicked Unity dialog button: " + result.buttonText);
                if (DialogService.HasBlockingDialog(result.status))
                {
                    Console.WriteLine("Unity still has modal dialog(s):");
                    PrintDialogs(result.status.dialogs, "  ");
                }

                return 0;
            }

            Console.Error.WriteLine("Error: " + result.error);
            if (!string.IsNullOrWhiteSpace(result.errorCode))
            {
                Console.Error.WriteLine("Code: " + result.errorCode);
            }

            if (DialogService.HasBlockingDialog(result.status))
            {
                Console.Error.WriteLine("Unity modal dialog(s) after click attempt:");
                PrintDialogs(result.status.dialogs, "  ");
            }

            return 1;
        }

        private static void PrintDialogs(System.Collections.Generic.List<DialogInfo> dialogs, string indent)
        {
            if (dialogs == null)
            {
                return;
            }

            foreach (var dialog in dialogs)
            {
                Console.WriteLine(indent + "dialog: " + dialog.id);
                if (!string.IsNullOrWhiteSpace(dialog.title))
                {
                    Console.WriteLine(indent + "  title: " + dialog.title);
                }
                if (!string.IsNullOrWhiteSpace(dialog.message))
                {
                    Console.WriteLine(indent + "  message: " + dialog.message.Replace("\n", " "));
                }
                if (dialog.buttons != null)
                {
                    Console.WriteLine(indent + "  buttons:");
                    foreach (var button in dialog.buttons)
                    {
                        Console.WriteLine(indent + "    - " + button.text + " (" + button.choice + ")");
                    }
                }
            }
        }

        private static JsonSerializerSettings JsonSettings()
        {
            return new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore
            };
        }
    }
}
