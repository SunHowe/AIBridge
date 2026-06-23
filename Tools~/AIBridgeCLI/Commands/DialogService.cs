using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace AIBridgeCLI.Commands
{
    public static class DialogService
    {
        private const int DefaultWaitPollIntervalMs = 100;
        private const int PostClickSettlingMs = 200;
        private const string WindowsPlatformName = "windows";
        private const string MacOSPlatformName = "macos";
        private const string LinuxPlatformName = "linux";
        private const string UnsupportedPlatformName = "unsupported";

        public static DialogStatusResult GetStatus()
        {
            if (!UnityEditorInstanceResolver.TryResolve(out var process, out var resolveError))
            {
                return new DialogStatusResult
                {
                    success = false,
                    platform = GetPlatformName(),
                    error = resolveError,
                    errorCode = "unity_editor_not_resolved"
                };
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return WindowsDialogDriver.GetStatus(process);
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return MacOSDialogDriver.GetStatus(process);
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return new DialogStatusResult
                {
                    success = false,
                    platform = LinuxPlatformName,
                    processId = process.Id,
                    windowTitle = process.MainWindowTitle,
                    error = "Dialog inspection is not supported on Linux yet.",
                    errorCode = "dialog_platform_not_supported"
                };
            }

            return new DialogStatusResult
            {
                success = false,
                platform = UnsupportedPlatformName,
                processId = process.Id,
                windowTitle = process.MainWindowTitle,
                error = "Dialog inspection is not supported on this platform.",
                errorCode = "dialog_platform_not_supported"
            };
        }

        public static DialogClickResult Click(string choice, string buttonText, string dialogId)
        {
            if (!UnityEditorInstanceResolver.TryResolve(out var process, out var resolveError))
            {
                return new DialogClickResult
                {
                    success = false,
                    platform = GetPlatformName(),
                    error = resolveError,
                    errorCode = "unity_editor_not_resolved"
                };
            }

            DialogClickResult result;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                result = WindowsDialogDriver.Click(process, choice, buttonText, dialogId);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                result = MacOSDialogDriver.Click(process, choice, buttonText, dialogId);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                result = new DialogClickResult
                {
                    success = false,
                    platform = LinuxPlatformName,
                    processId = process.Id,
                    error = "Dialog clicking is not supported on Linux yet.",
                    errorCode = "dialog_platform_not_supported"
                };
            }
            else
            {
                result = new DialogClickResult
                {
                    success = false,
                    platform = UnsupportedPlatformName,
                    processId = process.Id,
                    error = "Dialog clicking is not supported on this platform.",
                    errorCode = "dialog_platform_not_supported"
                };
            }

            if (result.success)
            {
                if (result.status == null)
                {
                    Thread.Sleep(PostClickSettlingMs);
                    result.status = GetStatus();
                }
            }

            return result;
        }

        public static DialogStatusResult Wait(int timeoutMs)
        {
            var startTime = DateTime.Now;
            DialogStatusResult lastStatus = null;

            while ((DateTime.Now - startTime).TotalMilliseconds < timeoutMs)
            {
                lastStatus = GetStatus();
                if (!HasBlockingDialog(lastStatus))
                {
                    return lastStatus;
                }

                Thread.Sleep(DefaultWaitPollIntervalMs);
            }

            if (lastStatus == null)
            {
                lastStatus = GetStatus();
            }

            if (HasBlockingDialog(lastStatus))
            {
                lastStatus.success = false;
                lastStatus.error = "Timed out waiting for Unity dialog to disappear.";
                lastStatus.errorCode = "dialog_wait_timeout";
            }

            return lastStatus;
        }

        public static bool HasBlockingDialog(DialogStatusResult status)
        {
            return status != null && status.success && status.dialogs != null && status.dialogs.Count > 0;
        }

        internal static DialogInfo SelectDialog(List<DialogInfo> dialogs, string dialogId)
        {
            if (dialogs == null || dialogs.Count == 0)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(dialogId))
            {
                foreach (var dialog in dialogs)
                {
                    if (string.Equals(dialog.id, dialogId, StringComparison.OrdinalIgnoreCase))
                    {
                        return dialog;
                    }
                }

                return null;
            }

            return dialogs[0];
        }

        internal static DialogButtonSelection SelectButton(List<DialogInfo> dialogs, string choice, string buttonText, string dialogId)
        {
            if (dialogs == null || dialogs.Count == 0)
            {
                return DialogButtonSelection.FromFailure("No matching Unity dialog was found.", "dialog_not_found", null);
            }

            var candidateDialogs = new List<DialogInfo>();
            if (!string.IsNullOrWhiteSpace(dialogId))
            {
                var dialog = SelectDialog(dialogs, dialogId);
                if (dialog == null)
                {
                    return DialogButtonSelection.FromFailure("No matching Unity dialog was found.", "dialog_not_found", null);
                }

                candidateDialogs.Add(dialog);
            }
            else
            {
                candidateDialogs.AddRange(dialogs);
            }

            var matches = new List<DialogButtonMatch>();
            foreach (var dialog in candidateDialogs)
            {
                if (dialog == null || dialog.buttons == null)
                {
                    continue;
                }

                foreach (var button in dialog.buttons)
                {
                    if (ButtonMatches(button, choice, buttonText))
                    {
                        matches.Add(new DialogButtonMatch
                        {
                            Dialog = dialog,
                            Button = button
                        });
                    }
                }
            }

            if (matches.Count == 1)
            {
                return DialogButtonSelection.FromSuccess(matches[0].Dialog, matches[0].Button);
            }

            if (matches.Count > 1)
            {
                return DialogButtonSelection.FromFailure(
                    "Multiple Unity dialog buttons matched. Provide --dialog-id or --button to choose one exactly.",
                    "dialog_button_ambiguous",
                    candidateDialogs.Count == 1 ? candidateDialogs[0] : null);
            }

            return DialogButtonSelection.FromFailure(
                "No matching dialog button was found.",
                "dialog_button_not_found",
                candidateDialogs.Count == 1 ? candidateDialogs[0] : null);
        }

        internal static DialogButtonInfo SelectButton(DialogInfo dialog, string choice, string buttonText)
        {
            if (dialog == null || dialog.buttons == null || dialog.buttons.Count == 0)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(buttonText))
            {
                foreach (var button in dialog.buttons)
                {
                    if (button == null || !button.enabled)
                    {
                        continue;
                    }

                    if (string.Equals(button.text, buttonText, StringComparison.OrdinalIgnoreCase) ||
                        ButtonTextMatches(button.text, buttonText))
                    {
                        return button;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(choice))
            {
                foreach (var button in dialog.buttons)
                {
                    if (button == null || !button.enabled)
                    {
                        continue;
                    }

                    if (ButtonMatchesChoice(button, choice))
                    {
                        return button;
                    }
                }
            }

            return null;
        }

        internal static string NormalizeChoice(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var normalized = NormalizeButtonTextForChoice(value);
            switch (normalized)
            {
                case "取消":
                case "关闭":
                case "關閉":
                case "cancel":
                case "close":
                case "abort":
                case "dismiss":
                    return "cancel";
                case "确认":
                case "確認":
                case "确定":
                case "確定":
                case "ok":
                case "okay":
                    return "ok";
                case "是":
                case "yes":
                    return "yes";
                case "否":
                case "no":
                    return "no";
                case "dontsave":
                case "don'tsave":
                case "don\u2019tsave":
                case "don't save":
                case "don\u2019t save":
                case "dont save":
                case "do not save":
                case "不保存":
                case "discard":
                case "discard changes":
                case "放弃":
                case "放棄":
                    return "discard";
                case "save":
                case "save changes":
                case "保存":
                    return "save";
                case "delete":
                case "remove":
                case "删除":
                case "刪除":
                    return "delete";
                case "replace":
                case "overwrite":
                case "替换":
                case "替換":
                case "覆盖":
                case "覆蓋":
                    return "replace";
                default:
                    return normalized;
            }
        }

        internal static DialogButtonInfo CreateButtonInfo(string id, string text, bool enabled)
        {
            var choices = enabled ? BuildChoiceList(text) : new List<string>();
            return new DialogButtonInfo
            {
                id = id,
                text = text,
                choice = choices.Count > 0 ? choices[0] : null,
                choices = choices.Count > 0 ? choices : null,
                enabled = enabled
            };
        }

        internal static string InferChoice(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            var choices = BuildChoiceList(text);
            return choices.Count > 0 ? choices[0] : null;
        }

        private static List<string> BuildChoiceList(string text)
        {
            var choices = new List<string>();
            var normalized = NormalizeButtonTextForChoice(text);
            if (normalized == "don't save" || normalized == "dont save" || normalized == "do not save" ||
                normalized.Contains("don't save") || normalized.Contains("dont save") ||
                normalized.Contains("do not save") || normalized.Contains("不保存") ||
                normalized.Contains("discard") || normalized.Contains("放弃") || normalized.Contains("放棄"))
            {
                AddChoice(choices, "discard");
                return choices;
            }

            if (normalized == "save" || normalized.Contains("save"))
            {
                AddChoice(choices, "save");
                return choices;
            }

            if (normalized == "cancel" || normalized.Contains("cancel") ||
                normalized == "close" || normalized.Contains("close") ||
                normalized == "abort" || normalized.Contains("abort") ||
                normalized == "dismiss" || normalized.Contains("dismiss") ||
                normalized == "取消" || normalized.Contains("取消") ||
                normalized == "关闭" || normalized.Contains("关闭") ||
                normalized == "關閉" || normalized.Contains("關閉"))
            {
                AddChoice(choices, "cancel");
                return choices;
            }

            if (normalized == "ok" || normalized == "okay" ||
                normalized == "确定" || normalized == "確認" || normalized == "确认" || normalized == "確定")
            {
                AddChoice(choices, "ok");
                return choices;
            }

            if (normalized == "yes" || normalized == "是")
            {
                AddChoice(choices, "yes");
                return choices;
            }

            if (normalized == "no" || normalized == "否")
            {
                AddChoice(choices, "no");
                return choices;
            }

            if (normalized.Contains("delete") || normalized.Contains("remove") ||
                normalized.Contains("删除") || normalized.Contains("刪除"))
            {
                AddChoice(choices, "delete");
                return choices;
            }

            if (normalized.Contains("replace") || normalized.Contains("overwrite") ||
                normalized.Contains("替换") || normalized.Contains("替換") ||
                normalized.Contains("覆盖") || normalized.Contains("覆蓋"))
            {
                AddChoice(choices, "replace");
                return choices;
            }

            return choices;
        }

        private static void AddChoice(List<string> choices, string value)
        {
            var normalized = NormalizeChoice(value);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            foreach (var choice in choices)
            {
                if (string.Equals(choice, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            choices.Add(normalized);
        }

        private static bool ButtonMatches(DialogButtonInfo button, string choice, string buttonText)
        {
            if (button == null || !button.enabled)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(buttonText) &&
                (string.Equals(button.text, buttonText, StringComparison.OrdinalIgnoreCase) ||
                 ButtonTextMatches(button.text, buttonText)))
            {
                return true;
            }

            return ButtonMatchesChoice(button, choice);
        }

        private static bool ButtonMatchesChoice(DialogButtonInfo button, string choice)
        {
            if (button == null || string.IsNullOrWhiteSpace(choice))
            {
                return false;
            }

            var normalizedChoice = NormalizeChoice(choice);
            if (string.IsNullOrWhiteSpace(normalizedChoice))
            {
                return false;
            }

            // choice 是逻辑选项，不再把未知按钮文本推断为可点选项；精确文本请走 --button。
            if (button.choices != null)
            {
                foreach (var buttonChoice in button.choices)
                {
                    if (string.Equals(buttonChoice, normalizedChoice, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return string.Equals(button.choice, normalizedChoice, StringComparison.OrdinalIgnoreCase);
        }

        private static bool ButtonTextMatches(string left, string right)
        {
            return string.Equals(
                NormalizeButtonTextForChoice(left),
                NormalizeButtonTextForChoice(right),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeButtonTextForChoice(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var normalized = value.Trim().ToLowerInvariant()
                .Replace("\u2019", "'")
                .Replace("`", "'")
                .Replace("&", string.Empty);

            while (normalized.Contains("  "))
            {
                normalized = normalized.Replace("  ", " ");
            }

            return normalized;
        }

        internal sealed class DialogButtonSelection
        {
            public bool Success { get; private set; }
            public DialogInfo Dialog { get; private set; }
            public DialogButtonInfo Button { get; private set; }
            public string Error { get; private set; }
            public string ErrorCode { get; private set; }

            public static DialogButtonSelection FromSuccess(DialogInfo dialog, DialogButtonInfo button)
            {
                return new DialogButtonSelection
                {
                    Success = true,
                    Dialog = dialog,
                    Button = button
                };
            }

            public static DialogButtonSelection FromFailure(string error, string errorCode, DialogInfo dialog)
            {
                return new DialogButtonSelection
                {
                    Success = false,
                    Dialog = dialog,
                    Error = error,
                    ErrorCode = errorCode
                };
            }
        }

        private sealed class DialogButtonMatch
        {
            public DialogInfo Dialog { get; set; }
            public DialogButtonInfo Button { get; set; }
        }

        internal static DialogStatusResult CreateStatusResult(Process process, string platform, List<DialogInfo> dialogs)
        {
            var result = new DialogStatusResult
            {
                success = true,
                platform = platform,
                processId = process.Id,
                windowTitle = process.MainWindowTitle
            };

            if (dialogs != null && dialogs.Count > 0)
            {
                result.blockedByDialog = true;
                result.dialogs = dialogs;
            }

            return result;
        }

        private static string GetPlatformName()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return WindowsPlatformName;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return MacOSPlatformName;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return LinuxPlatformName;
            }

            return UnsupportedPlatformName;
        }
    }
}
