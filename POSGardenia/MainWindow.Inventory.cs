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


        // Handlers are attached here (not in XAML) so nothing fires during InitializeComponent.
        private void WireInventoryControls()
        {
            DailyStockDatePicker.SelectedDate = DateTime.Today;

            foreach (var grid in new[] { DailyStockDataGrid, StockItemsDataGrid })
                grid.AutoGeneratingColumn += StockGrid_AutoGeneratingColumn;

            ProductsDataGrid.AutoGeneratingColumn += ProductsGrid_AutoGeneratingColumn;
            BillHistoryDataGrid.AutoGeneratingColumn += HideNonBrowsableColumns;
            ItemSalesReportDataGrid.AutoGeneratingColumn += ItemSalesGrid_AutoGeneratingColumn;

            DailyStockDataGrid.LoadingRow += DailyStockDataGrid_LoadingRow;
            DailyStockDatePicker.SelectedDateChanged += (_, _) => RefreshDailyStock();
            StockAddItemComboBox.SelectionChanged += (_, _) => OnAddStockItemChanged();
            StockItemsDataGrid.SelectionChanged += (_, _) =>
            {
                if (StockItemsDataGrid.SelectedItem is StockItemDisplay row && StockAddItemComboBox.ItemsSource is IEnumerable<StockItem> items)
                    StockAddItemComboBox.SelectedItem = items.FirstOrDefault(i => i.Id == row.Id);
            };

            MainTabControl.SelectionChanged += (s, e) =>
            {
                // Nested tab controls bubble their own SelectionChanged; only react to the main one.
                if (ReferenceEquals(e.OriginalSource, MainTabControl) && InventoryTabItem.IsSelected)
                    RefreshInventory();
            };
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
                nameof(DailyStockDisplay.InPacks) => dailySheet ? "Left" : "Bottles / Packs",
                nameof(DailyStockDisplay.SoldInPacks) => "Sold",
                nameof(DailyStockDisplay.SalesValue) => "Sales Value",
                _ => Regex.Replace(e.PropertyName, "(?<=[a-z])(?=[A-Z])", " ")
            };

            if (e.Column is DataGridTextColumn textColumn &&
                textColumn.Binding is Binding binding &&
                (e.PropertyType == typeof(decimal) || e.PropertyType == typeof(decimal?)))
            {
                binding.StringFormat = e.PropertyName switch
                {
                    nameof(DailyStockDisplay.SalesValue) => "#,0.00",
                    nameof(DailyStockDisplay.Difference) => "+0.##;-0.##;0",
                    _ => "0.##"
                };
            }
        }

        private static void DailyStockDataGrid_LoadingRow(object? sender, DataGridRowEventArgs e)
        {
            if (e.Row.Item is DailyStockDisplay row && (row.IsNegative || row.CountTone == "red"))
                e.Row.Background = new SolidColorBrush(Color.FromRgb(0xFE, 0xE2, 0xE2));
            else if (e.Row.Item is DailyStockDisplay checkRow && checkRow.CountTone == "amber")
                e.Row.Background = new SolidColorBrush(Color.FromRgb(0xFE, 0xF9, 0xC3));
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

        // Stock Items tab: the quantity label follows the unit of the chosen item.
        private void OnAddStockItemChanged()
        {
            var item = StockAddItemComboBox.SelectedItem as StockItem;
            StockAddQuantityLabel.Text = item == null ? "Quantity" : $"Quantity ({item.TrackingUnit})";
        }

        // Stock only comes in as "received", always dated today (the sheet for any day can still be viewed on Daily Stock).
        private void AddStock_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (StockAddItemComboBox.SelectedItem is not StockItem item)
                {
                    MessageBox.Show("Select an item.");
                    return;
                }

                if (!decimal.TryParse(StockAddQuantityTextBox.Text?.Trim(), out decimal quantity) || quantity <= 0)
                {
                    MessageBox.Show("Enter a quantity greater than zero.");
                    return;
                }

                string? note = string.IsNullOrWhiteSpace(StockAddNoteTextBox.Text) ? null : StockAddNoteTextBox.Text.Trim();

                _stockService.ReceiveStock(item.Id, quantity, note, DateTime.Today);

                StockAddQuantityTextBox.Clear();
                StockAddNoteTextBox.Clear();
                RefreshStockItems();
                RefreshDailyStock();

                StockAddResultTextBlock.Text = $"Added {quantity:0.##} {item.TrackingUnit} to {item.Name}.";
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

            // Active items only; keep the current choice, and never leave the cards blank.
            var active = _stockService.GetActiveStockItems();
            int? addId = (StockAddItemComboBox.SelectedItem as StockItem)?.Id;
            StockAddItemComboBox.ItemsSource = null;
            StockAddItemComboBox.ItemsSource = active;
            StockAddItemComboBox.SelectedItem =
                (addId.HasValue ? active.FirstOrDefault(i => i.Id == addId.Value) : null) ?? active.FirstOrDefault();
        }

    }
}
