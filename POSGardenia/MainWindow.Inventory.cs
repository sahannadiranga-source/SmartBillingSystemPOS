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
            BillHistoryDataGrid.AutoGeneratingColumn += HideNonBrowsableColumns;
            ItemSalesReportDataGrid.AutoGeneratingColumn += ItemSalesGrid_AutoGeneratingColumn;

            DailyStockDataGrid.LoadingRow += DailyStockDataGrid_LoadingRow;
            DailyStockDataGrid.SelectionChanged += (_, _) =>
            {
                if (DailyStockDataGrid.SelectedItem is DailyStockDisplay row)
                    SelectEntryItem(row.StockItemId);
            };

            DailyStockDatePicker.SelectedDateChanged += (_, _) => RefreshDailyStock();
            StockEntryItemComboBox.SelectionChanged += (_, _) => OnEntryItemChanged();
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

        private static void ItemSalesGrid_AutoGeneratingColumn(object? sender, DataGridAutoGeneratingColumnEventArgs e)
        {
            HideNonBrowsableColumns(sender, e);
            if (e.Cancel)
                return;

            e.Column.Header = e.PropertyName switch
            {
                nameof(ItemSalesReport.QuantitySold) => "Sold",
                nameof(ItemSalesReport.InPacks) => "Bottles / Packs",
                nameof(ItemSalesReport.TotalSales) => "Total Sales",
                _ => e.Column.Header
            };

            if (e.Column is DataGridTextColumn textColumn && textColumn.Binding is Binding binding)
            {
                if (e.PropertyName == nameof(ItemSalesReport.QuantitySold))
                    binding.StringFormat = "#,0.##";
                else if (e.PropertyName == nameof(ItemSalesReport.TotalSales))
                    binding.StringFormat = "#,0.00";
            }
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

            bool dailySheet = sender is DataGrid grid && grid.Name == nameof(DailyStockDataGrid);

            e.Column.Header = e.PropertyName switch
            {
                nameof(DailyStockDisplay.InPacks) => dailySheet ? "Left (Bottles / Packs)" : "Bottles / Packs",
                nameof(DailyStockDisplay.SoldInPacks) => "Sold (Bottles / Packs)",
                nameof(DailyStockDisplay.SalesValue) => "Sales Value",
                _ => Regex.Replace(e.PropertyName, "(?<=[a-z])(?=[A-Z])", " ")
            };

            if (e.Column is DataGridTextColumn textColumn &&
                textColumn.Binding is Binding binding &&
                (e.PropertyType == typeof(decimal) || e.PropertyType == typeof(decimal?)))
            {
                binding.StringFormat = e.PropertyName == nameof(DailyStockDisplay.SalesValue) ? "#,0.00" : "0.##";
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
                        SoldInPacks = PackFormatter.Describe(r.SoldQuantity, r.TrackingUnit, r.PackName, r.PackSize),
                        InPacks = PackFormatter.Describe(r.ClosingQuantity, r.TrackingUnit, r.PackName, r.PackSize),
                        SalesValue = r.SalesValue,
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
                    $"{date:yyyy-MM-dd}: {display.Count} items, {negative} negative.   Sales value: {display.Sum(d => d.SalesValue):#,0.00}";
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

        // The quantity is always in the item's own unit (ml / bottle / unit); say which one.
        private void OnEntryItemChanged()
        {
            var item = StockEntryItemComboBox.SelectedItem as StockItem;
            StockEntryQuantityLabel.Text = item == null ? "Quantity" : $"Quantity ({item.TrackingUnit})";
            LoadPackEditor(item);
        }

        // -----------------------------
        // Bottle / pack size of the selected item
        // -----------------------------

        private void LoadPackEditor(StockItem? item)
        {
            if (PackItemTextBlock == null)
                return;

            bool needsPack = item != null && !PackFormatter.IsCountedInBottles(item.TrackingUnit);
            PackSizeTextBox.IsEnabled = needsPack;
            SavePackSizeButton.IsEnabled = needsPack;

            if (item == null)
            {
                PackItemTextBlock.Text = "Select an item in the list above (or in Item) to set its bottle / pack size.";
                PackNameTextBox.Text = "";
                PackSizeTextBox.Text = "";
                PackUnitTextBlock.Text = "";
                return;
            }

            PackNameTextBox.Text = PackFormatter.PackNameFor(item.TrackingUnit);
            PackUnitTextBlock.Text = item.TrackingUnit;

            if (!needsPack)
            {
                PackItemTextBlock.Text = $"{item.Name} (counted in bottles) - no bottle / pack size is needed.";
                PackSizeTextBox.Text = "";
                return;
            }

            PackItemTextBlock.Text = $"{item.Name} (counted in {item.TrackingUnit})";
            PackSizeTextBox.Text = item.PackSize?.ToString("0.##") ?? "";
        }

        private void SavePackSize_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (StockEntryItemComboBox.SelectedItem is not StockItem item)
                {
                    MessageBox.Show("Select an item first.");
                    return;
                }

                decimal? size = null;
                if (!string.IsNullOrWhiteSpace(PackSizeTextBox.Text))
                {
                    if (!decimal.TryParse(PackSizeTextBox.Text.Trim(), out decimal parsed) || parsed <= 0)
                    {
                        MessageBox.Show("Per bottle/pack size: enter a number greater than zero, or leave it blank to remove it.");
                        return;
                    }

                    size = parsed;
                }

                _stockService.SetPack(item.Id, size);

                RefreshStockItems();      // reloads the item list (and this editor) with the saved size
                RefreshDailyStock();

                StockEntryHintTextBlock.Text = size.HasValue
                    ? $"Saved: 1 {PackFormatter.PackNameFor(item.TrackingUnit)} = {size:0.##} {item.TrackingUnit} for {item.Name}."
                    : $"Bottle / pack size removed for {item.Name}.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
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

                decimal total = quantity;   // always in the item's own unit
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
