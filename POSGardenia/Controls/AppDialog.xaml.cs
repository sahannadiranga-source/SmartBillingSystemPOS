using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace POSGardenia.Controls
{
    public enum DialogKind { Info, Success, Warning, Error, Question }

    // One button of the dialog. IsPrimary = the blue main action (also the Enter key); IsCancel = the Esc key.
    public record DialogButton(string Text, MessageBoxResult Result, bool IsPrimary = false, bool IsCancel = false, bool IsDanger = false);

    // The app's own popup: rounded card, colour strip and icon by kind, big touch buttons.
    // Use AppMessage.Show / AppMessage.Confirm rather than creating this directly.
    public partial class AppDialog : Window
    {
        public MessageBoxResult Result { get; private set; } = MessageBoxResult.None;

        private readonly MessageBoxResult _escapeResult;

        public AppDialog(string title, string message, DialogKind kind, IReadOnlyList<DialogButton> buttons)
        {
            InitializeComponent();

            var (color, glyph) = kind switch
            {
                DialogKind.Success => (Color.FromRgb(0x16, 0xA3, 0x4A), "✓"),
                DialogKind.Warning => (Color.FromRgb(0xF5, 0x9E, 0x0B), "!"),
                DialogKind.Error => (Color.FromRgb(0xDC, 0x26, 0x26), "✕"),
                DialogKind.Question => (Color.FromRgb(0x1F, 0x6F, 0xEB), "?"),
                _ => (Color.FromRgb(0x1F, 0x6F, 0xEB), "i")
            };

            var brush = new SolidColorBrush(color);
            AccentBar.Background = brush;
            IconCircle.Background = brush;
            IconText.Text = glyph;
            TitleText.Text = title;
            MessageText.Text = message;

            _escapeResult = MessageBoxResult.None;

            foreach (var spec in buttons)
            {
                var button = new Button
                {
                    Content = spec.Text,
                    Template = (ControlTemplate)FindResource("DialogButtonTemplate"),
                    Height = 54,
                    MinWidth = 120,
                    Padding = new Thickness(24, 0, 24, 0),
                    Margin = new Thickness(spec.IsPrimary ? 0 : 0, 0, 12, 0),
                    FontSize = 17,
                    FontWeight = FontWeights.SemiBold,
                    Cursor = Cursors.Hand,
                    Foreground = spec.IsPrimary || spec.IsDanger ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)),
                    Background = spec.IsDanger
                        ? new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26))
                        : spec.IsPrimary
                            ? new SolidColorBrush(Color.FromRgb(0x1F, 0x6F, 0xEB))
                            : new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)),
                    IsDefault = spec.IsPrimary,
                    IsCancel = spec.IsCancel
                };

                var result = spec.Result;
                button.Click += (_, _) =>
                {
                    Result = result;
                    Close();
                };

                ButtonsPanel.Children.Add(button);

                if (spec.IsCancel)
                    _escapeResult = spec.Result;
            }

            // the last button sits flush with the right edge
            if (ButtonsPanel.Children.Count > 0 && ButtonsPanel.Children[^1] is Button last)
                last.Margin = new Thickness(0);

            // closing the window any other way counts as the cancel answer
            Closing += (_, _) =>
            {
                if (Result == MessageBoxResult.None)
                    Result = _escapeResult;
            };
        }
    }
}
