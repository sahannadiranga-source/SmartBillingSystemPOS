using POSGardenia.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace POSGardenia.Controls
{
    // The Counted column of Stock Items that shows the count as bottles + the rest ("10 bottles + 600 ml").
    // It looks the same as before when not editing; tapping it opens two boxes: whole bottles / packs first,
    // then what is left in ml / units. Each box opens the number pad.
    public class CountedBottlesColumn : DataGridTextColumn
    {
        protected override FrameworkElement GenerateEditingElement(DataGridCell cell, object dataItem)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            var bottles = NumberBox("integer", nameof(StockItemDisplay.CountBottles), nameof(StockItemDisplay.BottlesBoxVisibility));
            var bottlesLabel = Label(nameof(StockItemDisplay.BottlesLabel), nameof(StockItemDisplay.BottlesBoxVisibility));
            var loose = NumberBox("decimal", nameof(StockItemDisplay.CountLoose), nameof(StockItemDisplay.LooseBoxVisibility));
            var looseLabel = Label(nameof(StockItemDisplay.Unit), nameof(StockItemDisplay.LooseBoxVisibility));

            panel.Children.Add(bottles);
            panel.Children.Add(bottlesLabel);
            panel.Children.Add(loose);
            panel.Children.Add(looseLabel);

            return panel;
        }

        // The first visible box takes the focus (the Tag set above picks the number pad).
        protected override object PrepareCellForEdit(FrameworkElement editingElement, RoutedEventArgs editingEventArgs)
        {
            if (editingElement is Panel panel)
            {
                foreach (var child in panel.Children)
                {
                    if (child is TextBox box && box.Visibility == Visibility.Visible)
                    {
                        box.Focus();
                        box.SelectAll();
                        break;
                    }
                }
            }

            return "";
        }

        // The boxes write straight into the row as they are typed; the grid reads them in CellEditEnding.
        protected override bool CommitCellEdit(FrameworkElement editingElement) => true;

        protected override void CancelCellEdit(FrameworkElement editingElement, object uneditedValue) { }

        private static TextBox NumberBox(string keypad, string property, string visibilityProperty)
        {
            var box = new TextBox { Width = 80, Tag = keypad, VerticalContentAlignment = VerticalAlignment.Center };
            box.SetBinding(TextBox.TextProperty, new Binding(property) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            box.SetBinding(UIElement.VisibilityProperty, new Binding(visibilityProperty) { Mode = BindingMode.OneWay });
            return box;
        }

        private static TextBlock Label(string textProperty, string visibilityProperty)
        {
            var label = new TextBlock { Margin = new Thickness(6, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
            label.SetBinding(TextBlock.TextProperty, new Binding(textProperty) { Mode = BindingMode.OneWay });
            label.SetBinding(UIElement.VisibilityProperty, new Binding(visibilityProperty) { Mode = BindingMode.OneWay });
            return label;
        }
    }
}
