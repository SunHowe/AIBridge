using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace AIBridgeCLI.Commands
{
    internal static class WindowsDialogDriver
    {
        private const string PlatformName = "windows";
        private const int GW_OWNER = 4;
        private const int BM_CLICK = 0x00F5;
        private const int GWL_STYLE = -16;
        private const uint WS_DISABLED = 0x08000000;

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, int uCmd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        public static DialogStatusResult GetStatus(Process process)
        {
            var dialogs = EnumerateDialogs(process);
            return DialogService.CreateStatusResult(process, PlatformName, dialogs);
        }

        public static DialogClickResult Click(Process process, string choice, string buttonText, string dialogId)
        {
            var dialogs = EnumerateDialogs(process);
            var dialog = DialogService.SelectDialog(dialogs, dialogId);
            if (dialog == null)
            {
                return new DialogClickResult
                {
                    success = false,
                    platform = PlatformName,
                    processId = process.Id,
                    error = "No matching Unity dialog was found.",
                    errorCode = "dialog_not_found"
                };
            }

            var button = DialogService.SelectButton(dialog, choice, buttonText);
            if (button == null)
            {
                return new DialogClickResult
                {
                    success = false,
                    platform = PlatformName,
                    processId = process.Id,
                    dialog = dialog,
                    error = "No matching dialog button was found.",
                    errorCode = "dialog_button_not_found"
                };
            }

            if (!button.enabled)
            {
                return new DialogClickResult
                {
                    success = false,
                    platform = PlatformName,
                    processId = process.Id,
                    dialog = dialog,
                    buttonId = button.id,
                    buttonText = button.text,
                    choice = button.choice,
                    error = "The matching dialog button is disabled.",
                    errorCode = "dialog_button_disabled"
                };
            }

            var hwnd = ParseHwnd(button.id);
            if (hwnd == IntPtr.Zero)
            {
                return new DialogClickResult
                {
                    success = false,
                    platform = PlatformName,
                    processId = process.Id,
                    dialog = dialog,
                    buttonId = button.id,
                    buttonText = button.text,
                    choice = button.choice,
                    error = "The matching dialog button cannot be clicked by this backend.",
                    errorCode = "dialog_button_invalid_id"
                };
            }

            // Unity 主线程被模态窗口阻塞时，CLI 只能从操作系统窗口层发送点击消息解锁。
            SendMessage(hwnd, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
            return new DialogClickResult
            {
                success = true,
                clicked = true,
                platform = PlatformName,
                processId = process.Id,
                dialogId = dialog.id,
                buttonId = button.id,
                buttonText = button.text,
                choice = button.choice,
                dialog = dialog
            };
        }

        private static List<DialogInfo> EnumerateDialogs(Process process)
        {
            var dialogs = new List<DialogInfo>();

            EnumWindows((hWnd, lParam) =>
            {
                if (!IsWindowVisible(hWnd))
                {
                    return true;
                }

                GetWindowThreadProcessId(hWnd, out var processId);
                if (processId != (uint)process.Id)
                {
                    return true;
                }

                if (hWnd == process.MainWindowHandle)
                {
                    return true;
                }

                var owner = GetWindow(hWnd, GW_OWNER);
                if (owner == IntPtr.Zero)
                {
                    return true;
                }

                GetWindowThreadProcessId(owner, out var ownerProcessId);
                if (ownerProcessId != (uint)process.Id)
                {
                    return true;
                }

                var dialog = BuildDialogInfo(hWnd);
                if (dialog.buttons != null && dialog.buttons.Count > 0)
                {
                    dialogs.Add(dialog);
                }

                return true;
            }, IntPtr.Zero);

            return dialogs;
        }

        private static DialogInfo BuildDialogInfo(IntPtr dialogHwnd)
        {
            var title = GetWindowTextValue(dialogHwnd);
            var buttons = new List<DialogButtonInfo>();
            var messages = new List<string>();

            EnumChildWindows(dialogHwnd, (childHwnd, lParam) =>
            {
                var className = GetClassNameValue(childHwnd);
                var text = GetWindowTextValue(childHwnd);
                if (string.IsNullOrWhiteSpace(text))
                {
                    return true;
                }

                if (string.Equals(className, "Button", StringComparison.OrdinalIgnoreCase))
                {
                    buttons.Add(new DialogButtonInfo
                    {
                        id = "hwnd:" + childHwnd.ToInt64(),
                        text = text,
                        choice = DialogService.InferChoice(text),
                        enabled = IsWindowEnabledByStyle(childHwnd)
                    });
                }
                else if (className.IndexOf("Static", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    messages.Add(text);
                }

                return true;
            }, IntPtr.Zero);

            return new DialogInfo
            {
                id = "hwnd:" + dialogHwnd.ToInt64(),
                title = title,
                message = JoinMessage(messages),
                buttons = buttons
            };
        }

        private static bool IsWindowEnabledByStyle(IntPtr hwnd)
        {
            var style = unchecked((uint)GetWindowLong(hwnd, GWL_STYLE));
            return (style & WS_DISABLED) == 0;
        }

        private static string GetWindowTextValue(IntPtr hwnd)
        {
            var sb = new StringBuilder(1024);
            GetWindowText(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        private static string GetClassNameValue(IntPtr hwnd)
        {
            var sb = new StringBuilder(256);
            GetClassName(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        private static string JoinMessage(List<string> messages)
        {
            if (messages == null || messages.Count == 0)
            {
                return null;
            }

            return string.Join("\n", messages);
        }

        private static IntPtr ParseHwnd(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || !id.StartsWith("hwnd:", StringComparison.OrdinalIgnoreCase))
            {
                return IntPtr.Zero;
            }

            var value = id.Substring("hwnd:".Length);
            return long.TryParse(value, out var handle) ? new IntPtr(handle) : IntPtr.Zero;
        }
    }
}
