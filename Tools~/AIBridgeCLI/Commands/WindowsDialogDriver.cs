using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace AIBridgeCLI.Commands
{
    internal static class WindowsDialogDriver
    {
        private const string PlatformName = "windows";
        private const int ClickVerifyTimeoutMs = 1500;
        private const int ClickVerifyPollIntervalMs = 100;
        private const int GW_OWNER = 4;
        private const int BM_CLICK = 0x00F5;
        private const int GWL_STYLE = -16;
        private const uint WS_DISABLED = 0x08000000;
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const int SW_RESTORE = 9;
        private const int SW_SHOW = 5;

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
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool SetFocus(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, IntPtr dwExtraInfo);

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

            var dialogHwnd = ParseHwnd(dialog.id);
            if (dialogHwnd == IntPtr.Zero)
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
                    error = "The matching dialog cannot be activated by this backend.",
                    errorCode = "dialog_invalid_id"
                };
            }

            FocusDialog(dialogHwnd, hwnd);

            // Unity 主线程被模态窗口阻塞时，CLI 只能从操作系统窗口层发送点击消息解锁。
            SendMessage(hwnd, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
            var statusAfterClick = WaitForClickEffect(process, dialog.id, button.id);
            if (IsStillBlockedBySameButton(statusAfterClick, dialog.id, button.id))
            {
                // 部分 Unity/系统弹窗会忽略 BM_CLICK，改用真实鼠标命中点击作为兜底。
                TryMouseClick(dialogHwnd, hwnd);
                statusAfterClick = WaitForClickEffect(process, dialog.id, button.id);
                if (IsStillBlockedBySameButton(statusAfterClick, dialog.id, button.id))
                {
                    return new DialogClickResult
                    {
                        success = false,
                        clicked = true,
                        platform = PlatformName,
                        processId = process.Id,
                        dialogId = dialog.id,
                        buttonId = button.id,
                        buttonText = button.text,
                        choice = button.choice,
                        dialog = dialog,
                        status = statusAfterClick,
                        error = "The dialog button message and fallback mouse click were sent, but the dialog is still present.",
                        errorCode = "dialog_click_not_confirmed"
                    };
                }
            }

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
                dialog = dialog,
                status = statusAfterClick
            };
        }

        private static void FocusDialog(IntPtr dialogHwnd, IntPtr buttonHwnd)
        {
            if (IsIconic(dialogHwnd))
            {
                ShowWindow(dialogHwnd, SW_RESTORE);
            }

            ShowWindow(dialogHwnd, SW_SHOW);
            SetForegroundWindow(dialogHwnd);
            SetFocus(buttonHwnd);
        }

        private static bool TryMouseClick(IntPtr dialogHwnd, IntPtr buttonHwnd)
        {
            if (!GetWindowRect(buttonHwnd, out var rect))
            {
                return false;
            }

            var x = rect.Left + Math.Max(1, (rect.Right - rect.Left) / 2);
            var y = rect.Top + Math.Max(1, (rect.Bottom - rect.Top) / 2);
            var hasOriginalCursor = GetCursorPos(out var originalCursor);

            FocusDialog(dialogHwnd, buttonHwnd);
            if (!SetCursorPos(x, y))
            {
                return false;
            }

            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, IntPtr.Zero);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, IntPtr.Zero);

            if (hasOriginalCursor)
            {
                SetCursorPos(originalCursor.X, originalCursor.Y);
            }

            return true;
        }

        private static DialogStatusResult WaitForClickEffect(Process process, string dialogId, string buttonId)
        {
            var startTime = DateTime.Now;
            DialogStatusResult lastStatus = null;

            while ((DateTime.Now - startTime).TotalMilliseconds < ClickVerifyTimeoutMs)
            {
                Thread.Sleep(ClickVerifyPollIntervalMs);

                var dialogs = EnumerateDialogs(process);
                lastStatus = DialogService.CreateStatusResult(process, PlatformName, dialogs);
                if (!IsStillBlockedBySameButton(lastStatus, dialogId, buttonId))
                {
                    return lastStatus;
                }
            }

            return lastStatus;
        }

        private static bool IsStillBlockedBySameButton(DialogStatusResult status, string dialogId, string buttonId)
        {
            if (!DialogService.HasBlockingDialog(status))
            {
                return false;
            }

            foreach (var dialog in status.dialogs)
            {
                if (!string.Equals(dialog.id, dialogId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (dialog.buttons == null)
                {
                    return IsWindow(ParseHwnd(dialogId));
                }

                foreach (var button in dialog.buttons)
                {
                    if (string.Equals(button.id, buttonId, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                return IsWindow(ParseHwnd(dialogId));
            }

            return false;
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

        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private struct POINT
        {
            public int X;
            public int Y;
        }
    }
}
