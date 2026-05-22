using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace AIBridgeCLI.Commands
{
    internal static class MacOSDialogDriver
    {
        private const string PlatformName = "macos";
        private const uint kCFStringEncodingUTF8 = 0x08000100;
        private const int kAXErrorSuccess = 0;
        private const int kAXErrorAPIDisabled = -25211;

        private const string AXWindowsAttribute = "AXWindows";
        private const string AXRoleAttribute = "AXRole";
        private const string AXSubroleAttribute = "AXSubrole";
        private const string AXTitleAttribute = "AXTitle";
        private const string AXDescriptionAttribute = "AXDescription";
        private const string AXValueAttribute = "AXValue";
        private const string AXChildrenAttribute = "AXChildren";
        private const string AXEnabledAttribute = "AXEnabled";
        private const string AXPressAction = "AXPress";

        private const string AXButtonRole = "AXButton";
        private const string AXStaticTextRole = "AXStaticText";
        private const string AXSheetRole = "AXSheet";
        private const string AXDialogSubrole = "AXDialog";
        private const string AXSystemDialogSubrole = "AXSystemDialog";

        [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
        private static extern IntPtr AXUIElementCreateApplication(int pid);

        [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
        private static extern int AXUIElementCopyAttributeValue(IntPtr element, IntPtr attribute, out IntPtr value);

        [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
        private static extern int AXUIElementPerformAction(IntPtr element, IntPtr action);

        [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
        private static extern bool AXIsProcessTrusted();

        [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
        private static extern IntPtr CFStringCreateWithCString(IntPtr alloc, string cStr, uint encoding);

        [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
        private static extern long CFStringGetLength(IntPtr theString);

        [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
        private static extern long CFStringGetMaximumSizeForEncoding(long length, uint encoding);

        [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
        private static extern bool CFStringGetCString(IntPtr theString, byte[] buffer, long bufferSize, uint encoding);

        [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
        private static extern long CFArrayGetCount(IntPtr theArray);

        [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
        private static extern IntPtr CFArrayGetValueAtIndex(IntPtr theArray, long idx);

        [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
        private static extern bool CFBooleanGetValue(IntPtr boolean);

        [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
        private static extern ulong CFGetTypeID(IntPtr cf);

        [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
        private static extern ulong CFStringGetTypeID();

        [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
        private static extern ulong CFBooleanGetTypeID();

        [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
        private static extern void CFRelease(IntPtr cf);

        public static DialogStatusResult GetStatus(Process process)
        {
            var permissionResult = EnsureTrusted(process);
            if (permissionResult != null)
            {
                return permissionResult;
            }

            var app = AXUIElementCreateApplication(process.Id);
            if (app == IntPtr.Zero)
            {
                return CreateFailure(process, "Failed to create macOS Accessibility application element.", "macos_accessibility_unavailable");
            }

            try
            {
                var dialogs = EnumerateDialogs(app, process.MainWindowTitle);
                return DialogService.CreateStatusResult(process, PlatformName, dialogs);
            }
            finally
            {
                CFRelease(app);
            }
        }

        public static DialogClickResult Click(Process process, string choice, string buttonText, string dialogId)
        {
            var permissionResult = EnsureTrusted(process);
            if (permissionResult != null)
            {
                return new DialogClickResult
                {
                    success = false,
                    platform = PlatformName,
                    processId = process.Id,
                    error = permissionResult.error,
                    errorCode = permissionResult.errorCode
                };
            }

            var app = AXUIElementCreateApplication(process.Id);
            if (app == IntPtr.Zero)
            {
                return new DialogClickResult
                {
                    success = false,
                    platform = PlatformName,
                    processId = process.Id,
                    error = "Failed to create macOS Accessibility application element.",
                    errorCode = "macos_accessibility_unavailable"
                };
            }

            try
            {
                var result = TryClick(app, process, choice, buttonText, dialogId);
                return result;
            }
            finally
            {
                CFRelease(app);
            }
        }

        private static DialogStatusResult EnsureTrusted(Process process)
        {
            if (AXIsProcessTrusted())
            {
                return null;
            }

            return new DialogStatusResult
            {
                success = false,
                platform = PlatformName,
                processId = process.Id,
                windowTitle = process.MainWindowTitle,
                error = "AIBridgeCLI needs Accessibility permission to inspect or click Unity dialogs.",
                errorCode = "macos_accessibility_permission_required"
            };
        }

        private static DialogClickResult TryClick(IntPtr app, Process process, string choice, string buttonText, string dialogId)
        {
            if (!TryCopyAttribute(app, AXWindowsAttribute, out var windows, out var errorCode))
            {
                return new DialogClickResult
                {
                    success = false,
                    platform = PlatformName,
                    processId = process.Id,
                    error = errorCode == "macos_accessibility_permission_required"
                        ? "AIBridgeCLI needs Accessibility permission to inspect or click Unity dialogs."
                        : "Failed to read Unity windows through macOS Accessibility.",
                    errorCode = errorCode
                };
            }

            try
            {
                var count = CFArrayGetCount(windows);
                for (long i = 0; i < count; i++)
                {
                    var window = CFArrayGetValueAtIndex(windows, i);
                    var dialog = BuildDialogInfo(window, i);
                    if (!IsLikelyDialog(dialog, process.MainWindowTitle))
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(dialogId) &&
                        !string.Equals(dialog.id, dialogId, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var selectedButton = DialogService.SelectButton(dialog, choice, buttonText);
                    if (selectedButton == null)
                    {
                        continue;
                    }

                    var clicked = TryPressMatchingButton(window, dialog.id, selectedButton);
                    return new DialogClickResult
                    {
                        success = clicked,
                        clicked = clicked ? (bool?)true : null,
                        platform = PlatformName,
                        processId = process.Id,
                        dialogId = dialog.id,
                        buttonId = selectedButton.id,
                        buttonText = selectedButton.text,
                        choice = selectedButton.choice,
                        dialog = dialog,
                        error = clicked ? null : "The matching dialog button could not be pressed.",
                        errorCode = clicked ? null : "dialog_button_press_failed"
                    };
                }
            }
            finally
            {
                CFRelease(windows);
            }

            return new DialogClickResult
            {
                success = false,
                platform = PlatformName,
                processId = process.Id,
                error = "No matching Unity dialog button was found.",
                errorCode = "dialog_button_not_found"
            };
        }

        private static List<DialogInfo> EnumerateDialogs(IntPtr app, string mainWindowTitle)
        {
            var dialogs = new List<DialogInfo>();

            if (!TryCopyAttribute(app, AXWindowsAttribute, out var windows, out _))
            {
                return dialogs;
            }

            try
            {
                var count = CFArrayGetCount(windows);
                for (long i = 0; i < count; i++)
                {
                    var window = CFArrayGetValueAtIndex(windows, i);
                    var dialog = BuildDialogInfo(window, i);
                    if (IsLikelyDialog(dialog, mainWindowTitle))
                    {
                        dialogs.Add(dialog);
                    }
                }
            }
            finally
            {
                CFRelease(windows);
            }

            return dialogs;
        }

        private static DialogInfo BuildDialogInfo(IntPtr window, long index)
        {
            var title = CopyStringAttribute(window, AXTitleAttribute);
            var role = CopyStringAttribute(window, AXRoleAttribute);
            var subrole = CopyStringAttribute(window, AXSubroleAttribute);
            var messageParts = new List<string>();
            var buttons = new List<DialogButtonInfo>();

            CollectChildren(window, "ax:" + index, buttons, messageParts);

            return new DialogInfo
            {
                id = "ax:" + index,
                title = title,
                role = role,
                subrole = subrole,
                message = messageParts.Count > 0 ? string.Join("\n", messageParts) : null,
                buttons = buttons
            };
        }

        private static void CollectChildren(IntPtr element, string idPrefix, List<DialogButtonInfo> buttons, List<string> messageParts)
        {
            if (!TryCopyAttribute(element, AXChildrenAttribute, out var children, out _))
            {
                return;
            }

            try
            {
                var count = CFArrayGetCount(children);
                for (long i = 0; i < count; i++)
                {
                    var child = CFArrayGetValueAtIndex(children, i);
                    var role = CopyStringAttribute(child, AXRoleAttribute);
                    var text = CopyStringAttribute(child, AXTitleAttribute);
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        text = CopyStringAttribute(child, AXValueAttribute);
                    }
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        text = CopyStringAttribute(child, AXDescriptionAttribute);
                    }

                    var childPrefix = idPrefix + ":" + i;
                    if (string.Equals(role, AXButtonRole, StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrWhiteSpace(text))
                    {
                        buttons.Add(new DialogButtonInfo
                        {
                            id = childPrefix,
                            text = text,
                            choice = DialogService.InferChoice(text),
                            enabled = CopyBoolAttribute(child, AXEnabledAttribute, true)
                        });
                    }
                    else if (string.Equals(role, AXStaticTextRole, StringComparison.OrdinalIgnoreCase) &&
                             !string.IsNullOrWhiteSpace(text))
                    {
                        messageParts.Add(text);
                    }

                    CollectChildren(child, childPrefix, buttons, messageParts);
                }
            }
            finally
            {
                CFRelease(children);
            }
        }

        private static bool TryPressMatchingButton(IntPtr window, string dialogId, DialogButtonInfo target)
        {
            return TryPressMatchingButtonRecursive(window, dialogId, target);
        }

        private static bool TryPressMatchingButtonRecursive(IntPtr element, string idPrefix, DialogButtonInfo target)
        {
            if (!TryCopyAttribute(element, AXChildrenAttribute, out var children, out _))
            {
                return false;
            }

            try
            {
                var count = CFArrayGetCount(children);
                for (long i = 0; i < count; i++)
                {
                    var child = CFArrayGetValueAtIndex(children, i);
                    var role = CopyStringAttribute(child, AXRoleAttribute);
                    var text = CopyStringAttribute(child, AXTitleAttribute);
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        text = CopyStringAttribute(child, AXValueAttribute);
                    }
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        text = CopyStringAttribute(child, AXDescriptionAttribute);
                    }

                    var childId = idPrefix + ":" + i;
                    if (string.Equals(role, AXButtonRole, StringComparison.OrdinalIgnoreCase) &&
                        (string.Equals(childId, target.id, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(text, target.text, StringComparison.OrdinalIgnoreCase)))
                    {
                        var action = CreateCFString(AXPressAction);
                        try
                        {
                            return AXUIElementPerformAction(child, action) == kAXErrorSuccess;
                        }
                        finally
                        {
                            CFRelease(action);
                        }
                    }

                    if (TryPressMatchingButtonRecursive(child, childId, target))
                    {
                        return true;
                    }
                }
            }
            finally
            {
                CFRelease(children);
            }

            return false;
        }

        private static bool IsLikelyDialog(DialogInfo dialog, string mainWindowTitle)
        {
            if (dialog == null || dialog.buttons == null || dialog.buttons.Count == 0)
            {
                return false;
            }

            if (string.Equals(dialog.subrole, AXDialogSubrole, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(dialog.subrole, AXSystemDialogSubrole, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(dialog.role, AXSheetRole, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private static string CopyStringAttribute(IntPtr element, string attributeName)
        {
            if (!TryCopyAttribute(element, attributeName, out var value, out _))
            {
                return null;
            }

            try
            {
                return CFStringToString(value);
            }
            finally
            {
                CFRelease(value);
            }
        }

        private static bool CopyBoolAttribute(IntPtr element, string attributeName, bool defaultValue)
        {
            if (!TryCopyAttribute(element, attributeName, out var value, out _))
            {
                return defaultValue;
            }

            try
            {
                if (CFGetTypeID(value) != CFBooleanGetTypeID())
                {
                    return defaultValue;
                }

                return CFBooleanGetValue(value);
            }
            finally
            {
                CFRelease(value);
            }
        }

        private static bool TryCopyAttribute(IntPtr element, string attributeName, out IntPtr value, out string errorCode)
        {
            var attribute = CreateCFString(attributeName);
            try
            {
                var error = AXUIElementCopyAttributeValue(element, attribute, out value);
                if (error == kAXErrorSuccess && value != IntPtr.Zero)
                {
                    errorCode = null;
                    return true;
                }

                errorCode = error == kAXErrorAPIDisabled
                    ? "macos_accessibility_permission_required"
                    : "macos_accessibility_attribute_unavailable";
                return false;
            }
            finally
            {
                CFRelease(attribute);
            }
        }

        private static IntPtr CreateCFString(string value)
        {
            return CFStringCreateWithCString(IntPtr.Zero, value, kCFStringEncodingUTF8);
        }

        private static string CFStringToString(IntPtr cfString)
        {
            if (cfString == IntPtr.Zero)
            {
                return null;
            }

            if (CFGetTypeID(cfString) != CFStringGetTypeID())
            {
                return null;
            }

            var length = CFStringGetLength(cfString);
            var maxSize = CFStringGetMaximumSizeForEncoding(length, kCFStringEncodingUTF8) + 1;
            var buffer = new byte[maxSize];
            if (!CFStringGetCString(cfString, buffer, buffer.Length, kCFStringEncodingUTF8))
            {
                return null;
            }

            var actualLength = Array.IndexOf(buffer, (byte)0);
            if (actualLength < 0)
            {
                actualLength = buffer.Length;
            }

            return Encoding.UTF8.GetString(buffer, 0, actualLength);
        }

        private static DialogStatusResult CreateFailure(Process process, string error, string errorCode)
        {
            return new DialogStatusResult
            {
                success = false,
                platform = PlatformName,
                processId = process.Id,
                windowTitle = process.MainWindowTitle,
                error = error,
                errorCode = errorCode
            };
        }
    }
}
