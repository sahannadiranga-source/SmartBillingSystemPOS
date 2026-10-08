using POSGardenia.Controls;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace POSGardenia
{
    // Touch support: the on-screen keyboard / keypad that appears when a text field is tapped.
    public partial class MainWindow
    {
        // Handlers are attached here (not in XAML) so nothing fires during InitializeComponent.
        private void InitTouchSupport()
        {
            OnScreenKeyboard.Visibility = Visibility.Collapsed;
            OnScreenKeyboard.DoneRequested += (_, _) => HideOnScreenKeyboard(clearFocus: true);

            KeyboardEnabledCheckBox.Checked += KeyboardEnabledCheckBox_Changed;
            KeyboardEnabledCheckBox.Unchecked += KeyboardEnabledCheckBox_Changed;

            // GotKeyboardFocus / LostKeyboardFocus bubble, so one handler on the window sees every text
            // field, including the text part inside an editable ComboBox.
            AddHandler(Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(AnyElementGotKeyboardFocus), true);
            AddHandler(Keyboard.LostKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(AnyElementLostKeyboardFocus), true);
        }

        private void AnyElementGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            // a password box takes the full keyboard
            if (e.OriginalSource is PasswordBox passwordBox)
            {
                if (!_appSettings.UseOnScreenKeyboard || !passwordBox.IsEnabled)
                {
                    HideOnScreenKeyboard(clearFocus: false);
                    return;
                }

                OnScreenKeyboard.Attach(passwordBox);
                OnScreenKeyboard.Visibility = Visibility.Visible;
                Dispatcher.BeginInvoke(new Action(() => passwordBox.BringIntoView()), DispatcherPriority.Background);
                return;
            }

            if (e.OriginalSource is not TextBox textBox)
                return;

            // Dates are picked from the calendar; read-only or disabled boxes cannot be typed into.
            if (!_appSettings.UseOnScreenKeyboard || textBox is DatePickerTextBox || textBox.IsReadOnly || !textBox.IsEnabled)
            {
                HideOnScreenKeyboard(clearFocus: false);
                return;
            }

            OnScreenKeyboard.Attach(textBox, LayoutFor(textBox));
            OnScreenKeyboard.Visibility = Visibility.Visible;

            // The keyboard takes space at the bottom; keep the field in view above it.
            Dispatcher.BeginInvoke(new Action(() => textBox.BringIntoView()), DispatcherPriority.Background);
        }

        private void AnyElementLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            // Focus moving to another text field keeps the keyboard; moving anywhere else closes it.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (Keyboard.FocusedElement is not TextBox and not PasswordBox)
                    HideOnScreenKeyboard(clearFocus: false);
            }), DispatcherPriority.Input);
        }

        // Numeric fields are marked with Tag="decimal" / "integer" / "signed" / "time" in the XAML.
        private static KeyboardLayout LayoutFor(TextBox textBox)
        {
            return (textBox.Tag as string) switch
            {
                "decimal" => KeyboardLayout.Decimal,
                "signed" => KeyboardLayout.Signed,
                "integer" => KeyboardLayout.Integer,
                "time" => KeyboardLayout.Time,
                _ => KeyboardLayout.Text
            };
        }

        private void HideOnScreenKeyboard(bool clearFocus)
        {
            OnScreenKeyboard.Detach();
            OnScreenKeyboard.Visibility = Visibility.Collapsed;

            if (clearFocus)
                Keyboard.ClearFocus();
        }

        private void KeyboardEnabledCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            try
            {
                _appSettings.UseOnScreenKeyboard = KeyboardEnabledCheckBox.IsChecked == true;
                _settingsService.Save(_appSettings);

                if (!_appSettings.UseOnScreenKeyboard)
                    HideOnScreenKeyboard(clearFocus: false);
            }
            catch (Exception ex)
            {
                AppMessage.Show("Failed to save the keyboard setting.\n" + ex.Message);
            }
        }
    }
}
