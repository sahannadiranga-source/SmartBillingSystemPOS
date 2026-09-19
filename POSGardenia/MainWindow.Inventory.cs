using POSGardenia.Models;
using POSGardenia.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace POSGardenia
{
    // Inventory tab: Daily Stock sheet with stock entries (Received / Adjust / Wastage) and the Stock Items list.
    public partial class MainWindow
    {
        private readonly StockService _stockService = new();

        private const string EntryReceived = "Received";
        private const string EntryAdjust = "Adjust (+ or -)";
        private const string EntryWastage = "Wastage";

        // Handlers are attached here (not in XAML) so nothing fires during InitializeComponent.
        private void WireInventoryControls()
        {
            DailyStockDatePicker.SelectedDate = DateTime.Today;

            StockEntryTypeComboBox.ItemsSource = new[] { EntryReceived, EntryAdjust, EntryWastage };
            StockEntryTypeComboBox.SelectedIndex = 0;

            foreach (var grid in new[] { DailyStockDataGrid, StockItemsDataGrid })
                grid.AutoGeneratingColumn += StockGrid_AutoGeneratingColumn;

            ProductsDataGrid.AutoGeneratingColumn += ProductsGrid_AutoGeneratingColumn;

            DailyStockDataGrid.LoadingRow += DailyStockDataGrid_LoadingRow;
            DailyStockDataGrid.SelectionChanged += (_, _) =>
            {
                if (DailyStockDataGrid.SelectedItem is DailyStockDisplay row)
                    SelectEntryItem(row.StockItemId);
            };

            DailyStockDatePicker.SelectedDateChanged += (_, _) => RefreshDailyStock();
            StockEntryItemComboBox.SelectionChanged += (_, _) => LoadEntryUnitOptions();
            StockEntryTypeComboBox.SelectionChanged += (_, _) => UpdateEntryHint();

            MainTabControl.SelectionChanged += (s, e) =>
            {
                // Nested tab controls bubble their own SelectionChanged; only react to the main one.
                if (ReferenceEquals(e.OriginalSource, MainTabControl) && InventoryTabItem.IsSelected)
                    RefreshInventory();
            };

            UpdateEntryHint();
        }

        private static void ProductsGrid_AutoGeneratingColumn(object? sender, DataGridAutoGeneratingColumnEventArgs e)
        {
            HideNonBrowsableColumns(sender, e);
            if (e.Cancel)
                return;

            if (e.PropertyName == nameof(ProductDisplay.MainItem))
                e.Column.Header = "Main Item";
            else if (e.PropertyName == nameof(ProductDisplay.UnitsPerSale))
                e.Column.Header = "Stock Used Per Sale";
        }

        // The DataGrid does not honour [Browsable(false)] by itself, so hidden columns are dropped here.
        private static void HideNonBrowsableColumns(object? sender, DataGridAutoGeneratingColumnEventArgs e)
        {
            if (e.PropertyDescriptor is System.ComponentModel.PropertyDescriptor descriptor && !descriptor.IsBrowsable)
                e.Cancel = true;
        }

        private static void StockGrid_AutoGeneratingColumn(object? sender, DataGridAutoGeneratingColumnEventArgs e)
        {
            HideNonBrowsableColumns(sender, e);
            if (e.Cancel)
                return;

            e.Column.Header = Regex.Replace(e.PropertyName, "(?<=[a-z])(?=[A-Z])", " ");

            if (e.Column is DataGridTextColumn textColumn &&
                textColumn.Binding is Binding binding &&
                (e.PropertyType == typeof(decimal) || e.PropertyType == typeof(decimal?)))
            {
                binding.StringFormat = "0.##";
            }
        }

        private static void DailyStockDataGrid_LoadingRow(object? sender, DataGridRowEventArgs e)
        {
            if (e.Row.Item is DailyStockDisplay row && row.IsNegative)
                e.Row.Background = new SolidColorBrush(Color.FromRgb(0xFE, 0xE2, 0xE2));
            else
                e.Row.ClearValue(DataGridRow.BackgroundProperty);
        }

        private void RefreshInventory()
        {
            try
            {
                RefreshStockItems();
                RefreshDailyStock();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to load inventory.\n" + ex.Message);
            }
        }

        // -----------------------------
        // Daily Stock
        // -----------------------------

        private DateTime SelectedStockDate => DailyStockDatePicker.SelectedDate ?? DateTime.Today;

        private void RefreshDailyStock_Click(object sender, RoutedEventArgs e) => RefreshDailyStock();

        private void RefreshDailyStock()
        {
            try
            {
                var date = SelectedStockDate;
                int? selectedId = (DailyStockDataGrid.SelectedItem as DailyStockDisplay)?.StockItemId;

                var rows = _stockService.GetDailySheet(date);

                var display = rows.Select(r =>
                {
                    var status = new List<string>();
                    if (r.IsNegative) status.Add("NEGATIVE");

                    return new DailyStockDisplay
                    {
                        StockItemId = r.StockItemId,
                        Item = r.StockItemName,
                        Unit = r.TrackingUnit,
                        Opening = r.OpeningQuantity,
                        Received = r.ReceivedQuantity,
                        Sold = r.SoldQuantity,
                        Adjusted = r.AdjustedQuantity,
                        Closing = r.ClosingQuantity,
                        Status = string.Join(" | ", status),
                        IsNegative = r.IsNegative
                    };
                }).ToList();

                DailyStockDataGrid.ItemsSource = null;
                DailyStockDataGrid.ItemsSource = display;

                if (selectedId.HasValue)
                    DailyStockDataGrid.SelectedItem = display.FirstOrDefault(d => d.StockItemId == selectedId.Value);

                int negative = display.Count(d => d.IsNegative);

                DailyStockSummaryTextBlock.Text =
                    $"{date:yyyy-MM-dd}: {display.Count} items, {negative} negative.   " +
                    "Closing = Opening + Received - Sold + Adjusted. Each day's closing is the next day's opening.";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to load daily stock.\n" + ex.Message);
            }
        }

        // -----------------------------
        // Stock entries (Received / Adjust / Wastage)
        // -----------------------------

        private void UpdateEntryHint()
        {
            if (StockEntryHintTextBlock == null)
                return;

            StockEntryHintTextBlock.Text = StockEntryTypeComboBox.SelectedItem as string switch
            {
                EntryReceived => "Received: stock delivered or bought. Sales are taken from bills automatically - do not enter them here.",
                EntryAdjust => "Adjust: correct the count. Use a plus to add stock, a minus (e.g. -50) to remove it.",
                EntryWastage => "Wastage: breakage, spillage or free servings. Enter a positive amount; it is taken out of stock.",
                _ => ""
            };
        }

        private void SelectEntryItem(int stockItemId)
        {
            if (StockEntryItemComboBox.ItemsSource is IEnumerable<StockItem> items)
                StockEntryItemComboBox.SelectedItem = items.FirstOrDefault(i => i.Id == stockItemId);
        }

        // "Counted in": the item's own unit, or a bigger pack (a bottle, a pack of cigarettes).
        private void LoadEntryUnitOptions()
        {
            if (StockEntryItemComboBox.SelectedItem is not StockItem item)
            {
                StockEntryUnitComboBox.ItemsSource = null;
                return;
            }

            StockEntryUnitComboBox.ItemsSource = BuildPackOptions(item.Id, item.TrackingUnit);
            StockEntryUnitComboBox.SelectedIndex = 0;
        }

        private List<PackOption> BuildPackOptions(int stockItemId, string unit)
        {
            var options = new List<PackOption> { new PackOption { Label = unit, Units = 1 } };
            options.AddRange(_stockService.GetPackOptions(stockItemId)
                .GroupBy(o => o.Units)
                .Select(g => new PackOption { Label = $"{g.First().Label} ({g.Key:0.##} {unit})", Units = g.Key }));

            return options;
        }

        private void AddStockEntry_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (StockEntryItemComboBox.SelectedItem is not StockItem item)
                {
                    MessageBox.Show("Select an item.");
                    return;
                }

                if (!decimal.TryParse(StockEntryQuantityTextBox.Text?.Trim(), out decimal quantity) || quantity == 0)
                {
                    MessageBox.Show("Enter a quantity (not zero).");
                    return;
                }

                var pack = StockEntryUnitComboBox.SelectedItem as PackOption ?? new PackOption { Units = 1 };
                decimal total = quantity * pack.Units;
                string? note = string.IsNullOrWhiteSpace(StockEntryNoteTextBox.Text) ? null : StockEntryNoteTextBox.Text.Trim();
                var date = SelectedStockDate;
                string type = StockEntryTypeComboBox.SelectedItem as string ?? EntryReceived;

                switch (type)
                {
                    case EntryReceived:
                        _stockService.ReceiveStock(item.Id, total, note, date);
                        break;
                    case EntryAdjust:
                        _stockService.AdjustStock(item.Id, total, note, date);
                        break;
                    case EntryWastage:
                        _stockService.RecordWastage(item.Id, total, note, date);
                        break;
                }

                StockEntryQuantityTextBox.Clear();
                StockEntryNoteTextBox.Clear();
                RefreshDailyStock();
                RefreshStockItems();

                StockEntryHintTextBlock.Text = $"Saved: {type} {total:+0.##;-0.##} {item.TrackingUnit} for {item.Name} on {date:yyyy-MM-dd}.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        // -----------------------------
        // Stock items
        // -----------------------------

        private void RefreshStockItems()
        {
            var items = _stockService.GetStockItemsForDisplay();
            int? selectedId = (StockItemsDataGrid.SelectedItem as StockItemDisplay)?.Id;

            StockItemsDataGrid.ItemsSource = null;
            StockItemsDataGrid.ItemsSource = items;
            if (selectedId.HasValue)
                StockItemsDataGrid.SelectedItem = items.FirstOrDefault(i => i.Id == selectedId.Value);

            // Entry item list: active items only; keep the current choice.
            int? entryId = (StockEntryItemComboBox.SelectedItem as StockItem)?.Id;
            var active = _stockService.GetActiveStockItems();
            StockEntryItemComboBox.ItemsSource = null;
            StockEntryItemComboBox.ItemsSource = active;
            if (entryId.HasValue)
                SelectEntryItem(entryId.Value);
        }

    }
}
