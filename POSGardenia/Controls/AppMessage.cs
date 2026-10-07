using System;
using System.Collections.Generic;
using System.Windows;

namespace POSGardenia.Controls
{
    // The app's popups. AppMessage.Show works like MessageBox.Show (same arguments, same answer), but opens
    // the modern in-app dialog. AppMessage.Confirm asks a question with your own button names.
    public static class AppMessage
    {
        public static MessageBoxResult Show(
            string text,
            string caption = "",
            MessageBoxButton button = MessageBoxButton.OK,
            MessageBoxImage image = MessageBoxImage.None)
        {
            DialogKind kind = KindFor(text, image);

            // Cancel / No sit on the left, the main answer on the right.
            var buttons = button switch
            {
                MessageBoxButton.OKCancel => new List<DialogButton>
                {
                    new("Cancel", MessageBoxResult.Cancel, IsCancel: true),
                    new("OK", MessageBoxResult.OK, IsPrimary: true)
                },
                MessageBoxButton.YesNo => new List<DialogButton>
                {
                    new("No", MessageBoxResult.No, IsCancel: true),
                    new("Yes", MessageBoxResult.Yes, IsPrimary: true)
                },
                MessageBoxButton.YesNoCancel => new List<DialogButton>
                {
                    new("Cancel", MessageBoxResult.Cancel, IsCancel: true),
                    new("No", MessageBoxResult.No),
                    new("Yes", MessageBoxResult.Yes, IsPrimary: true)
                },
                _ => new List<DialogButton>
                {
                    new("OK", MessageBoxResult.OK, IsPrimary: true, IsCancel: true)
                }
            };

            // a question that asks yes / no looks like a question even when the caller gave no icon
            if (button != MessageBoxButton.OK && image == MessageBoxImage.None && kind == DialogKind.Info)
                kind = DialogKind.Question;

            return Run(string.IsNullOrWhiteSpace(caption) ? TitleFor(kind) : caption, text, kind, buttons);
        }

        // Asks the user to confirm something with your own button names. True = the confirm button.
        public static bool Confirm(
            string title,
            string message,
            string confirmText,
            string cancelText = "Cancel",
            DialogKind kind = DialogKind.Question,
            bool danger = false)
        {
            var buttons = new List<DialogButton>
            {
                new(cancelText, MessageBoxResult.Cancel, IsCancel: true),
                new(confirmText, MessageBoxResult.OK, IsPrimary: !danger, IsDanger: danger)
            };

            return Run(title, message, kind, buttons) == MessageBoxResult.OK;
        }

        private static MessageBoxResult Run(string title, string message, DialogKind kind, IReadOnlyList<DialogButton> buttons)
        {
            var app = Application.Current;

            // always on the screen's own thread
            if (app != null && !app.Dispatcher.CheckAccess())
                return app.Dispatcher.Invoke(() => Run(title, message, kind, buttons));

            var dialog = new AppDialog(title, message, kind, buttons);

            var owner = app?.MainWindow;
            if (owner != null && owner.IsVisible && !ReferenceEquals(owner, dialog))
            {
                dialog.Owner = owner;
                dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            else
            {
                dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            dialog.ShowDialog();
            return dialog.Result;
        }

        // Plain messages get a colour from their wording, so "Failed to ..." is red and "... saved." is green.
        public static DialogKind KindFor(string text, MessageBoxImage image)
        {
            switch (image)
            {
                case MessageBoxImage.Error:
                    return DialogKind.Error;
                case MessageBoxImage.Warning:
                    return DialogKind.Warning;
                case MessageBoxImage.Question:
                    return DialogKind.Question;
                case MessageBoxImage.Information:
                    return DialogKind.Info;
            }

            string t = text ?? "";

            if (t.StartsWith("Failed", StringComparison.OrdinalIgnoreCase) || t.StartsWith("Could not", StringComparison.OrdinalIgnoreCase))
                return DialogKind.Error;

            if (t.Contains("NOT SENT", StringComparison.Ordinal))
                return DialogKind.Warning;

            if (t.Contains("saved", StringComparison.OrdinalIgnoreCase) ||
                t.Contains("successfully", StringComparison.OrdinalIgnoreCase) ||
                t.Contains("generated", StringComparison.OrdinalIgnoreCase) ||
                t.Contains("updated", StringComparison.OrdinalIgnoreCase) ||
                t.Contains("completed", StringComparison.OrdinalIgnoreCase) ||
                t.StartsWith("Sent", StringComparison.OrdinalIgnoreCase))
                return DialogKind.Success;

            return DialogKind.Info;
        }

        public static string TitleFor(DialogKind kind) => kind switch
        {
            DialogKind.Success => "Done",
            DialogKind.Warning => "Please check",
            DialogKind.Error => "Something went wrong",
            DialogKind.Question => "Please confirm",
            _ => "Notice"
        };
    }
}
