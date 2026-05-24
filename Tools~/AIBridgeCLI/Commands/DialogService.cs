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
                    if (string.Equals(button.text, buttonText, StringComparison.OrdinalIgnoreCase) ||
                        ButtonTextMatches(button.text, buttonText))
                    {
                        return button;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(choice))
            {
                var normalizedChoice = NormalizeChoice(choice);
                foreach (var button in dialog.buttons)
                {
                    if (string.Equals(button.choice, normalizedChoice, StringComparison.OrdinalIgnoreCase))
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
                case "dontsave":
                case "don'tsave":
                case "don\u2019tsave":
                case "don't save":
                case "don\u2019t save":
                case "dont save":
                case "do not save":
                case "discard":
                case "discard changes":
                    return "discard";
                case "ok":
                    return "ok";
                case "yes":
                    return "yes";
                case "no":
                    return "no";
                case "save":
                case "save changes":
                    return "save";
                case "cancel":
                    return "cancel";
                case "close":
                    return "close";
                case "delete":
                    return "delete";
                case "replace":
                    return "replace";
                default:
                    return normalized;
            }
        }

        internal static string InferChoice(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            var normalized = NormalizeButtonTextForChoice(text);
            if (normalized == "don't save" || normalized == "dont save" || normalized == "do not save" ||
                normalized.Contains("don't save") || normalized.Contains("dont save") ||
                normalized.Contains("do not save") ||
                normalized.Contains("discard"))
            {
                return "discard";
            }

            if (normalized == "save" || normalized.Contains("save"))
            {
                return "save";
            }

            if (normalized == "cancel" || normalized.Contains("cancel"))
            {
                return "cancel";
            }

            if (normalized == "ok" || normalized == "okay")
            {
                return "ok";
            }

            if (normalized == "yes")
            {
                return "yes";
            }

            if (normalized == "no")
            {
                return "no";
            }

            if (normalized.Contains("delete") || normalized.Contains("remove"))
            {
                return "delete";
            }

            if (normalized.Contains("replace") || normalized.Contains("overwrite"))
            {
                return "replace";
            }

            if (normalized.Contains("close"))
            {
                return "close";
            }

            return normalized;
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
