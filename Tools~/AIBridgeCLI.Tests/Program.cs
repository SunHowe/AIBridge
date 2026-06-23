using System;
using System.Collections.Generic;
using AIBridgeCLI.Commands;

namespace AIBridgeCLI.Tests
{
    internal static class Program
    {
        private static int Main()
        {
            try
            {
                DialogButtonInfo_ExposesStrictLogicalChoices();
                DialogButtonInfo_DoesNotExposeChoicesForDisabledButtons();
                SelectButton_FindsUniqueMatchAcrossDialogs();
                SelectButton_IgnoresDisabledButtons();
                SelectButton_RejectsAmbiguousChoiceAcrossDialogs();
                SelectButton_RespectsExplicitDialogId();
                BatchDialogAutoClickPlan_PreservesTargetKind();
                Console.WriteLine("AIBridgeCLI dialog tests passed.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }

        private static void DialogButtonInfo_ExposesStrictLogicalChoices()
        {
            var close = DialogService.CreateButtonInfo("button:close", "Close", true);
            AssertEqual("cancel", close.choice, "Close should map to cancel.");
            AssertContains(close.choices, "cancel", "Close choices should include cancel.");

            var discard = DialogService.CreateButtonInfo("button:discard", "Don't Save", true);
            AssertEqual("discard", discard.choice, "Don't Save should map to discard.");
            AssertContains(discard.choices, "discard", "Don't Save choices should include discard.");

            var unknown = DialogService.CreateButtonInfo("button:custom", "Maybe Later", true);
            AssertEqual(null, unknown.choice, "Unknown text must not become a fake logical choice.");
            AssertEqual(null, unknown.choices, "Unknown text should require --button exact text.");
        }

        private static void DialogButtonInfo_DoesNotExposeChoicesForDisabledButtons()
        {
            var disabledCancel = DialogService.CreateButtonInfo("button:disabledCancel", "Cancel", false);

            AssertTrue(!disabledCancel.enabled, "Disabled button should keep enabled=false.");
            AssertEqual(null, disabledCancel.choice, "Disabled button must not expose a clickable logical choice.");
            AssertEqual(null, disabledCancel.choices, "Disabled button must not expose clickable choices.");
        }

        private static void SelectButton_FindsUniqueMatchAcrossDialogs()
        {
            var dialogs = new List<DialogInfo>
            {
                CreateDialog("dialog:first", DialogService.CreateButtonInfo("button:ok", "OK", true)),
                CreateDialog("dialog:second", DialogService.CreateButtonInfo("button:cancel", "Cancel", true))
            };

            var selection = DialogService.SelectButton(dialogs, "cancel", null, null);

            AssertTrue(selection.Success, "Unique cancel should be selected across dialogs.");
            AssertEqual("dialog:second", selection.Dialog.id, "The matching dialog should be selected.");
            AssertEqual("button:cancel", selection.Button.id, "The matching button should be selected.");
        }

        private static void SelectButton_IgnoresDisabledButtons()
        {
            var dialogs = new List<DialogInfo>
            {
                CreateDialog("dialog:first", DialogService.CreateButtonInfo("button:disabledCancel", "Cancel", false))
            };

            var choiceSelection = DialogService.SelectButton(dialogs, "cancel", null, null);
            AssertTrue(!choiceSelection.Success, "Disabled cancel must not match --choice cancel.");
            AssertEqual("dialog_button_not_found", choiceSelection.ErrorCode, "Disabled choice should be reported as not found.");

            var buttonSelection = DialogService.SelectButton(dialogs, null, "Cancel", null);
            AssertTrue(!buttonSelection.Success, "Disabled cancel must not match --button Cancel.");
            AssertEqual("dialog_button_not_found", buttonSelection.ErrorCode, "Disabled button text should be reported as not found.");
        }

        private static void SelectButton_RejectsAmbiguousChoiceAcrossDialogs()
        {
            var dialogs = new List<DialogInfo>
            {
                CreateDialog("dialog:first", DialogService.CreateButtonInfo("button:firstCancel", "Cancel", true)),
                CreateDialog("dialog:second", DialogService.CreateButtonInfo("button:secondCancel", "Close", true))
            };

            var selection = DialogService.SelectButton(dialogs, "cancel", null, null);

            AssertTrue(!selection.Success, "Ambiguous cancel must fail.");
            AssertEqual("dialog_button_ambiguous", selection.ErrorCode, "Ambiguous cancel should be reported explicitly.");
        }

        private static void SelectButton_RespectsExplicitDialogId()
        {
            var dialogs = new List<DialogInfo>
            {
                CreateDialog("dialog:first", DialogService.CreateButtonInfo("button:firstCancel", "Cancel", true)),
                CreateDialog("dialog:second", DialogService.CreateButtonInfo("button:secondCancel", "Close", true))
            };

            var selection = DialogService.SelectButton(dialogs, "cancel", null, "dialog:second");

            AssertTrue(selection.Success, "Explicit dialog id should disambiguate cancel.");
            AssertEqual("dialog:second", selection.Dialog.id, "Explicit dialog id should be respected.");
            AssertEqual("button:secondCancel", selection.Button.id, "Explicit dialog button should be selected.");
        }

        private static void BatchDialogAutoClickPlan_PreservesTargetKind()
        {
            var plan = BatchDialogAutoClickPlan.Parse(
                "dialog click --choice cancel\n" +
                "dialog click --button \"Don't Save\"\n" +
                "dialog click ok | yes | \"Don't Save\"\n");

            AssertEqual(3, plan.Rules.Count, "All dialog click rules should parse.");

            var choiceTarget = plan.Rules[0].Targets[0];
            AssertEqual("cancel", choiceTarget.Value, "--choice value should parse.");
            AssertEqual("choice", choiceTarget.Kind, "--choice target kind should be preserved.");
            AssertTrue(choiceTarget.AllowsChoiceMatch(), "--choice should allow choice matching.");
            AssertTrue(!choiceTarget.AllowsButtonMatch(), "--choice should not allow button-text matching.");

            var buttonTarget = plan.Rules[1].Targets[0];
            AssertEqual("Don't Save", buttonTarget.Value, "--button value should parse.");
            AssertEqual("button", buttonTarget.Kind, "--button target kind should be preserved.");
            AssertTrue(!buttonTarget.AllowsChoiceMatch(), "--button should not allow choice matching.");
            AssertTrue(buttonTarget.AllowsButtonMatch(), "--button should allow button-text matching.");

            foreach (var target in plan.Rules[2].Targets)
            {
                AssertEqual("any", target.Kind, "Unqualified alternatives should keep compatibility with both matching modes.");
                AssertTrue(target.AllowsChoiceMatch(), "Unqualified target should allow choice matching.");
                AssertTrue(target.AllowsButtonMatch(), "Unqualified target should allow button-text matching.");
            }
        }

        private static DialogInfo CreateDialog(string id, params DialogButtonInfo[] buttons)
        {
            return new DialogInfo
            {
                id = id,
                title = id,
                buttons = new List<DialogButtonInfo>(buttons)
            };
        }

        private static void AssertContains(List<string> values, string expected, string message)
        {
            if (values == null)
            {
                throw new InvalidOperationException(message);
            }

            foreach (var value in values)
            {
                if (string.Equals(value, expected, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            throw new InvalidOperationException(message);
        }

        private static void AssertEqual<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(message + " Expected: " + expected + ", actual: " + actual);
            }
        }

        private static void AssertTrue(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
