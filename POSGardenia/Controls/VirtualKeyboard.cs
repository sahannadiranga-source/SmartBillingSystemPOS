using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace POSGardenia.Controls
{
    // Which keys to show for the field being edited.
    public enum KeyboardLayout
    {
        Text,       // full keyboard (letters, digits, symbols)
        Decimal,    // keypad: digits and a decimal point (prices, amounts)
        Signed,     // keypad: also a minus sign (stock adjustments)
        Integer,    // keypad: digits only (minutes)
        Time        // keypad: digits and ':' (HH:mm)
    }

    // On-screen keyboard / keypad for touch screens. It types into the TextBox it is attached to.
    // Every key is non-focusable, so tapping a key never moves focus away from the field being edited.
    public class VirtualKeyboard : Border
    {
        private enum ShiftState { Off, Once, Lock }

        private sealed class KeyDef
        {
            public string? Label;            // null = empty spacer
            public Action? Click;
            public double Weight = 1;
            public bool Special;             // grey function key
            public bool Accent;              // blue key (Done / active shift)
            public bool Repeat;              // hold to repeat (backspace)
        }

        private TextBox? _target;
        private PasswordBox? _passwordTarget;    // typing a password: the same keys, written to a PasswordBox
        private KeyboardLayout _layout = KeyboardLayout.Text;
        private ShiftState _shift = ShiftState.Off;
        private bool _symbols;
        private bool _replaceOnFirstKey;

        private static readonly Brush KeyBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)));
        private static readonly Brush SpecialBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1)));
        private static readonly Brush AccentBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x1F, 0x6F, 0xEB)));
        private static readonly Brush KeyBorder = Freeze(new SolidColorBrush(Color.FromRgb(0xB6, 0xC2, 0xD2)));
        private static readonly Brush TextBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)));

        public event EventHandler? DoneRequested;

        public VirtualKeyboard()
        {
            Focusable = false;
            Background = Freeze(new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)));
            BorderBrush = KeyBorder;
            BorderThickness = new Thickness(0, 1, 0, 0);
            Padding = new Thickness(10, 8, 10, 10);
        }

        private static Brush Freeze(SolidColorBrush brush)
        {
            brush.Freeze();
            return brush;
        }

        // -----------------------------
        // Attach / detach
        // -----------------------------

        public void Attach(TextBox target, KeyboardLayout layout)
        {
            _passwordTarget = null;
            _target = target;
            _layout = layout;
            _symbols = false;

            // A keypad field that already holds a value (e.g. the default amount 500.00): the first
            // digit typed replaces it, like a calculator. Use Backspace/Clear/tap to edit instead.
            _replaceOnFirstKey = layout != KeyboardLayout.Text && !string.IsNullOrEmpty(target.Text);

            // Start a text field with a capital letter.
            _shift = layout == KeyboardLayout.Text && string.IsNullOrEmpty(target.Text) ? ShiftState.Once : ShiftState.Off;

            Rebuild();
        }

        // A password field: the full keyboard, nothing is replaced or shifted automatically.
        public void Attach(PasswordBox target)
        {
            _target = null;
            _passwordTarget = target;
            _layout = KeyboardLayout.Text;
            _symbols = false;
            _replaceOnFirstKey = false;
            _shift = ShiftState.Off;

            Rebuild();
        }

        public void Detach()
        {
            _target = null;
            _passwordTarget = null;
        }

        // -----------------------------
        // Typing
        // -----------------------------

        private void TypeText(string s)
        {
            if (_passwordTarget != null)
            {
                if (_passwordTarget.MaxLength == 0 || _passwordTarget.Password.Length + s.Length <= _passwordTarget.MaxLength)
                    _passwordTarget.Password += s;
                return;
            }

            var tb = _target;
            if (tb == null)
                return;

            if (_replaceOnFirstKey)
            {
                tb.Clear();
                _replaceOnFirstKey = false;
            }

            int start = tb.SelectionStart;
            int length = tb.SelectionLength;
            string next = tb.Text.Remove(start, length).Insert(start, s);

            if (tb.MaxLength > 0 && next.Length > tb.MaxLength)
                return;
            if (!IsAllowed(next))
                return;

            tb.Text = next;
            tb.CaretIndex = start + s.Length;
        }

        private bool IsAllowed(string text)
        {
            switch (_layout)
            {
                case KeyboardLayout.Integer:
                    return text.All(char.IsDigit);

                case KeyboardLayout.Decimal:
                    return text.All(c => char.IsDigit(c) || c == '.') && text.Count(c => c == '.') <= 1;

                case KeyboardLayout.Signed:
                    string body = text.StartsWith('-') ? text.Substring(1) : text;
                    return body.All(c => char.IsDigit(c) || c == '.') && body.Count(c => c == '.') <= 1
                           && text.Count(c => c == '-') <= 1;

                case KeyboardLayout.Time:
                    return text.All(c => char.IsDigit(c) || c == ':') && text.Count(c => c == ':') <= 1 && text.Length <= 5;

                default:
                    return true;
            }
        }

        private void Backspace()
        {
            if (_passwordTarget != null)
            {
                string typed = _passwordTarget.Password;
                if (typed.Length > 0)
                    _passwordTarget.Password = typed.Substring(0, typed.Length - 1);
                return;
            }

            var tb = _target;
            if (tb == null)
                return;

            _replaceOnFirstKey = false;

            int start = tb.SelectionStart;
            int length = tb.SelectionLength;

            if (length > 0)
            {
                tb.Text = tb.Text.Remove(start, length);
                tb.CaretIndex = start;
            }
            else if (start > 0)
            {
                tb.Text = tb.Text.Remove(start - 1, 1);
                tb.CaretIndex = start - 1;
            }
        }

        private void ClearAll()
        {
            _replaceOnFirstKey = false;
            _target?.Clear();
            _passwordTarget?.Clear();
        }

        private void TypeLetter(string letter)
        {
            TypeText(letter);

            // One-shot shift applies to a single letter.
            if (_shift == ShiftState.Once)
            {
                _shift = ShiftState.Off;
                Rebuild();
            }
        }

        private void ToggleShift()
        {
            // off -> one capital -> caps lock -> off
            _shift = _shift switch
            {
                ShiftState.Off => ShiftState.Once,
                ShiftState.Once => ShiftState.Lock,
                _ => ShiftState.Off
            };
            Rebuild();
        }

        private void ToggleSymbols()
        {
            _symbols = !_symbols;
            Rebuild();
        }

        // -----------------------------
        // Layout
        // -----------------------------

        private void Rebuild()
        {
            Child = _layout == KeyboardLayout.Text ? BuildTextKeyboard() : BuildKeypad();
        }

        private FrameworkElement BuildTextKeyboard()
        {
            var root = new StackPanel { MaxWidth = 1150 };

            root.Children.Add(Row(
                Digits("1234567890").Concat(new[] { BackspaceKey(1.6) }).ToArray()));

            if (!_symbols)
            {
                root.Children.Add(Row(Letters("QWERTYUIOP")));
                root.Children.Add(Row(
                    new[] { Spacer(0.5) }.Concat(Letters("ASDFGHJKL")).Concat(new[] { Spacer(0.5) }).ToArray()));
                root.Children.Add(Row(
                    new[] { ShiftKey() }
                        .Concat(Letters("ZXCVBNM"))
                        .Concat(new[] { Char(","), Char(".") })
                        .ToArray()));
            }
            else
            {
                root.Children.Add(Row(Chars("@#%&*()/+")));
                root.Children.Add(Row(Chars("!?:;'\"=_,.")));
                root.Children.Add(Row(
                    new[] { Spacer(0.5) }.Concat(Chars("-~^$<>[]")).Concat(new[] { Spacer(0.5) }).ToArray()));
            }

            root.Children.Add(Row(
                new KeyDef { Label = _symbols ? "ABC" : "?123", Weight = 1.6, Special = true, Click = ToggleSymbols },
                new KeyDef { Label = "Clear", Weight = 1.4, Special = true, Click = ClearAll },
                new KeyDef { Label = "space", Weight = 5, Click = () => TypeText(" ") },
                Char("-", 1),
                new KeyDef { Label = "Done", Weight = 1.8, Accent = true, Click = () => DoneRequested?.Invoke(this, EventArgs.Empty) }));

            return root;
        }

        private FrameworkElement BuildKeypad()
        {
            var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Center, Width = 440 };
            for (int i = 0; i < 4; i++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition());
                grid.RowDefinitions.Add(new RowDefinition());
            }

            void Put(KeyDef key, int row, int column, int columnSpan = 1, int rowSpan = 1)
            {
                var button = MakeKey(key);
                Grid.SetRow(button, row);
                Grid.SetColumn(button, column);
                Grid.SetColumnSpan(button, columnSpan);
                Grid.SetRowSpan(button, rowSpan);
                grid.Children.Add(button);
            }

            KeyDef D(string d) => new KeyDef { Label = d, Click = () => TypeText(d) };

            Put(D("7"), 0, 0); Put(D("8"), 0, 1); Put(D("9"), 0, 2); Put(BackspaceKey(1), 0, 3);
            Put(D("4"), 1, 0); Put(D("5"), 1, 1); Put(D("6"), 1, 2);
            Put(new KeyDef { Label = "Clear", Special = true, Click = ClearAll }, 1, 3);
            Put(D("1"), 2, 0); Put(D("2"), 2, 1); Put(D("3"), 2, 2);
            Put(new KeyDef { Label = "Done", Accent = true, Click = () => DoneRequested?.Invoke(this, EventArgs.Empty) }, 2, 3, 1, 2);

            switch (_layout)
            {
                case KeyboardLayout.Decimal:
                    Put(D("0"), 3, 0, 2); Put(D("."), 3, 2);
                    break;
                case KeyboardLayout.Signed:
                    Put(D("-"), 3, 0); Put(D("0"), 3, 1); Put(D("."), 3, 2);
                    break;
                case KeyboardLayout.Time:
                    Put(D("0"), 3, 0, 2); Put(D(":"), 3, 2);
                    break;
                default:    // Integer
                    Put(D("0"), 3, 0, 3);
                    break;
            }

            return grid;
        }

        // -----------------------------
        // Key helpers
        // -----------------------------

        private KeyDef[] Digits(string chars) => chars.Select(c => Char(c.ToString())).ToArray();

        private KeyDef[] Chars(string chars) => chars.Select(c => Char(c.ToString())).ToArray();

        private KeyDef[] Letters(string upper) => upper.Select(c =>
        {
            string shown = _shift == ShiftState.Off ? c.ToString().ToLowerInvariant() : c.ToString();
            return new KeyDef { Label = shown, Click = () => TypeLetter(shown) };
        }).ToArray();

        private KeyDef Char(string c, double weight = 1) => new KeyDef { Label = c, Weight = weight, Click = () => TypeText(c) };

        private static KeyDef Spacer(double weight) => new KeyDef { Weight = weight };

        private KeyDef BackspaceKey(double weight) => new KeyDef { Label = "⌫", Weight = weight, Special = true, Repeat = true, Click = Backspace };

        private KeyDef ShiftKey() => new KeyDef
        {
            Label = _shift == ShiftState.Lock ? "⇪" : "⇧",
            Weight = 1.5,
            Special = true,
            Accent = _shift != ShiftState.Off,
            Click = ToggleShift
        };

        private FrameworkElement Row(params KeyDef[] keys)
        {
            var grid = new Grid();

            for (int i = 0; i < keys.Length; i++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(keys[i].Weight, GridUnitType.Star) });

                if (keys[i].Label == null)
                    continue;

                var button = MakeKey(keys[i]);
                Grid.SetColumn(button, i);
                grid.Children.Add(button);
            }

            return grid;
        }

        private ButtonBase MakeKey(KeyDef key)
        {
            ButtonBase button = key.Repeat
                ? new RepeatButton { Delay = 400, Interval = 70 }
                : new Button();

            button.Focusable = false;                 // never steal focus from the field being edited
            button.IsTabStop = false;
            button.Content = key.Label;
            button.Margin = new Thickness(3);
            button.Height = 62;
            button.MinWidth = 40;
            button.FontSize = key.Label!.Length > 2 ? 18 : 24;
            button.FontWeight = FontWeights.SemiBold;
            button.BorderThickness = new Thickness(1);
            button.BorderBrush = KeyBorder;
            button.Background = key.Accent ? AccentBrush : key.Special ? SpecialBrush : KeyBrush;
            button.Foreground = key.Accent ? Brushes.White : TextBrush;

            if (key.Special)
                button.FontFamily = new FontFamily("Segoe UI Symbol, Segoe UI");

            // The shared rounded / pressed-feedback template from the window resources.
            if (TryFindResource("TouchButtonTemplate") is ControlTemplate template)
                button.Template = template;

            button.Click += (_, _) => key.Click?.Invoke();
            return button;
        }
    }
}
