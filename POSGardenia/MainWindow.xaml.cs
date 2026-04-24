using POSGardenia.Data;
using POSGardenia.Models;
using POSGardenia.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace POSGardenia
{
    public partial class MainWindow : Window
    {
        private readonly CategoryRepository _categoryRepository = new();
        private readonly ProductRepository _productRepository = new();
        private readonly DiningTableRepository _diningTableRepository = new();
        private readonly BillRepository _billRepository = new();
        private readonly BillItemRepository _billItemRepository = new();
        private readonly PaymentRepository _paymentRepository = new();
        private readonly ExpenseRepository _expenseRepository = new();
        private readonly ReceiptPrintService _receiptPrintService = new();
        private readonly SettingsService _settingsService = new();
        private AppSettings _appSettings = new();
        private readonly BackupService _backupService = new();
        private DispatcherTimer? _backupTimer;

        private readonly ObservableCollection<PosCartLine> _cart = new();
        private List<Product> _activeProducts = new();
        private int? _selectedCategoryId = null;
        private int? _currentTargetBillId = null;
        private ProductDisplay? _selectedManagementProduct = null;
        private OpenBillDisplay? _selectedTablesBill = null;
        public MainWindow()
        {
            InitializeComponent();

            LoadAppSettings();
            LoadPrinters();
            StartAutoBackupTimer();

            LoadDefaultReportDates();

            LoadCategories();
            LoadCategoriesGrid();
            LoadProducts();
            LoadTables();

            LoadPaymentMethods();
            LoadReports();

            LoadPosTables();
            LoadPosOpenBills();
            LoadPosProducts();
            LoadPosCategories();

            LoadTablesTabOpenBills();

            CartDataGrid.ItemsSource = _cart;
            RefreshCartView();
        }

        // -----------------------------
        // POS LOADERS
        // -----------------------------
        private void LoadPosCategories()
        {
            CategoryButtonsPanel.Children.Clear();

            var allButton = CreateCategoryButton("All", null);
            CategoryButtonsPanel.Children.Add(allButton);

            var categories = _categoryRepository.GetActiveCategories();
            foreach (var category in categories)
            {
                CategoryButtonsPanel.Children.Add(CreateCategoryButton(category.Name, category.Id));
            }
        }

        private string GetVisibleBillNumber(int billId)
        {
            try
            {
                if (billId <= 0)
                    return $"#{billId}";

                var bill = _billRepository.GetOpenBillsForDisplay()
                    .FirstOrDefault(x => x.Id == billId);

                if (bill != null && !string.IsNullOrWhiteSpace(bill.VisibleBillNumber))
                    return bill.VisibleBillNumber;

                return $"#{billId}";
            }
            catch
            {
                return $"#{billId}";
            }
        }

        private Button CreateCategoryButton(string text, int? categoryId)
        {
            var button = new Button
            {
                Content = text,
                Height = 48,
                Margin = new Thickness(0, 0, 0, 10),
                Background = categoryId == null
                    ? new SolidColorBrush(Color.FromRgb(31, 111, 235))
                    : new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                Foreground = categoryId == null ? Brushes.White : Brushes.Black,
                BorderThickness = new Thickness(0),
                FontWeight = FontWeights.SemiBold,
                Cursor = System.Windows.Input.Cursors.Hand
            };

            button.Click += (s, e) =>
            {
                _selectedCategoryId = categoryId;
                SelectedCategoryTextBlock.Text = categoryId == null ? "All" : text;
                RenderProductButtons();
            };

            return button;
        }

        private void LoadPosProducts()
        {
            _activeProducts = _productRepository.GetActiveProducts();
            RenderProductButtons();
        }

        private void ClearProductFormButton_Click(object sender, RoutedEventArgs e)
        {
            ClearProductForm();
        }

        private void RenderProductButtons()
        {
            ProductButtonsPanel.Children.Clear();

            var products = _selectedCategoryId == null
                ? _activeProducts
                : _activeProducts.Where(p => p.CategoryId == _selectedCategoryId.Value).ToList();

            foreach (var product in products)
            {
                var button = new Button
                {
                    Width = 150,
                    Height = 90,
                    Margin = new Thickness(0, 0, 12, 12),
                    BorderThickness = new Thickness(0),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Background = product.IsKitchenItem
                        ? new SolidColorBrush(Color.FromRgb(14, 165, 164))
                        : new SolidColorBrush(Color.FromRgb(31, 111, 235)),
                    Foreground = Brushes.White,
                    Content = new StackPanel
                    {
                        Children =
                        {
                            new TextBlock
                            {
                                Text = product.Name,
                                FontWeight = FontWeights.Bold,
                                TextAlignment = TextAlignment.Center,
                                TextWrapping = TextWrapping.Wrap
                            },
                            new TextBlock
                            {
                                Text = product.SellingPrice.ToString("F2"),
                                Margin = new Thickness(0, 6, 0, 0),
                                TextAlignment = TextAlignment.Center
                            }
                        }
                    }
                };

                button.Click += (s, e) => AddProductToCart(product);
                ProductButtonsPanel.Children.Add(button);
            }
        }

        private void LoadPosTables()
        {
            PosTableComboBox.ItemsSource = null;
            PosTableComboBox.ItemsSource = _diningTableRepository.GetActiveTables();
        }

        private void LoadPosOpenBills()
        {
            var openBills = _billRepository.GetOpenBillsForDisplay();

            PosExistingBillComboBox.ItemsSource = null;
            PosExistingBillComboBox.ItemsSource = openBills;
        }

        private void RefreshCartView()
        {
            try
            {
                CartDataGrid.Items.Refresh();
                CartTotalTextBlock.Text = $"Cart Total: {_cart.Sum(x => x?.LineTotal ?? 0):F2}";

                if (_currentTargetBillId.HasValue)
                {
                    string tableName = GetBillTableName(_currentTargetBillId.Value);
                    string visibleBillNo = GetVisibleBillNumber(_currentTargetBillId.Value);
                    PosModeTextBlock.Text = $"Mode: Working on Bill No: {visibleBillNo} | Table: {tableName}";
                }
                else
                {
                    PosModeTextBlock.Text = "Mode: New Sale";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to refresh cart view.\n" + ex.Message);
            }
        }
        private void AddProductToCart(Product product)
        {
            try
            {
                if (product == null)
                {
                    MessageBox.Show("Invalid product.");
                    return;
                }

                var existingNewLine = _cart.FirstOrDefault(x => x != null && x.ProductId == product.Id && !x.IsExistingItem);

                if (existingNewLine == null)
                {
                    _cart.Add(new PosCartLine
                    {
                        BillItemId = null,
                        ProductId = product.Id,
                        ProductName = product.Name ?? "",
                        UnitPrice = product.SellingPrice,
                        Quantity = 1,
                        IsKitchenItem = product.IsKitchenItem,
                        IsExistingItem = false
                    });
                }
                else
                {
                    existingNewLine.Quantity += 1;
                }

                RefreshCartView();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to add product to cart.\n" + ex.Message);
            }
        }
        private void RemoveSelectedCartItem_Click(object sender, RoutedEventArgs e)
        {
            if (CartDataGrid.SelectedItem is not PosCartLine selected)
            {
                MessageBox.Show("Select a cart item first.");
                return;
            }

            _cart.Remove(selected);
            RefreshCartView();
        }

        private void ClearCart_Click(object sender, RoutedEventArgs e)
        {
            _cart.Clear();
            _currentTargetBillId = null;
            RefreshCartView();
        }

        private bool ValidateCart()
        {
            if (_cart.Count == 0)
            {
                MessageBox.Show("Cart is empty.");
                return false;
            }

            return true;
        }

        private void SaveCartItemsToBill(int billId)
        {
            try
            {
                if (billId <= 0)
                    throw new Exception("Invalid bill id.");

                if (_cart == null || _cart.Count == 0)
                    throw new Exception("Cart is empty.");

                var newLines = _cart.Where(x => x != null && !x.IsExistingItem).ToList();

                if (newLines.Count == 0)
                    throw new Exception("No new items to save.");

                foreach (var line in newLines)
                {
                    if (line.ProductId <= 0)
                        continue;

                    if (line.Quantity <= 0)
                        continue;

                    _billItemRepository.Add(new BillItem
                    {
                        BillId = billId,
                        ProductId = line.ProductId,
                        UnitPrice = line.UnitPrice,
                        Quantity = line.Quantity,
                        Status = "ACTIVE",
                        IsKitchenPrinted = false
                    });
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to save cart items to bill. " + ex.Message, ex);
            }
        }
        private void CreateNewTableBillFromCart_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!ValidateCart())
                    return;

                if (PosTableComboBox.SelectedItem is not DiningTable selectedTable)
                {
                    MessageBox.Show("Select a table.");
                    return;
                }

                var bill = new Bill
                {
                    DiningTableId = selectedTable.Id,
                    BillType = "TABLE",
                    Status = "OPEN",
                    CreatedAt = DateTime.Now
                };

                int billId = _billRepository.Create(bill);
                SaveCartItemsToBill(billId);
                string visibleBillNo = GetVisibleBillNumber(billId);

                MessageBox.Show($"Table bill created successfully.\nTable: {selectedTable.TableName}\nBill No: {visibleBillNo}");

                _cart.Clear();
                _currentTargetBillId = null;
                PosExistingBillComboBox.SelectedIndex = -1;
                RefreshCartView();
                RefreshAllOpenBillViews();
                LoadPosTables();
                LoadPosOpenBills();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to create table bill.\n" + ex.Message);
            }
        }
        private void AddCartToExistingBill_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!ValidateCart())
                    return;

                int targetBillId;
                string tableName;

                if (_currentTargetBillId.HasValue)
                {
                    targetBillId = _currentTargetBillId.Value;
                    tableName = GetBillTableName(targetBillId);

                    SaveCartItemsToBill(targetBillId);
                    string visibleBillNo = GetVisibleBillNumber(targetBillId);
                    MessageBox.Show($"Items added successfully.\nTable: {tableName}\nBill No: {visibleBillNo}");
                }
                else
                {
                    if (PosExistingBillComboBox.SelectedItem is not OpenBillDisplay selectedBill)
                    {
                        MessageBox.Show("Select an existing open bill.");
                        return;
                    }

                    targetBillId = selectedBill.Id;
                    tableName = string.IsNullOrWhiteSpace(selectedBill.TableName) ? "Quick Sale" : selectedBill.TableName;

                    SaveCartItemsToBill(targetBillId);
                    string visibleBillNo = selectedBill.VisibleBillNumber;
                    MessageBox.Show($"Items added successfully.\nTable: {tableName}\nBill No: {visibleBillNo}");
                }

                _cart.Clear();
                _currentTargetBillId = null;
                PosExistingBillComboBox.SelectedIndex = -1;
                RefreshCartView();
                RefreshAllOpenBillViews();
                LoadPosOpenBills();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to add items to existing bill.\n" + ex.Message);
            }
        }
        private void CreateQuickSaleAndPay_Click(object sender, RoutedEventArgs e)
        {
            PayNowFromPos_Click(sender, e);
        }

        // -----------------------------
        // TABLES TAB
        // -----------------------------
        private void LoadTablesTabOpenBills()
        {
            try
            {
                var openBills = _billRepository.GetOpenBillsForDisplay();
                TablesOpenBillsCountTextBlock.Text = $"Open Bills: {openBills.Count}";
                RenderOpenBillCards(openBills);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to load open bills in tables tab.\n" + ex.Message);
            }
        }

        private void RefreshTablesTab_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _selectedTablesBill = null;

                LoadTablesTabOpenBills();

                SelectedBillItemsDataGrid.ItemsSource = null;
                SelectedTableBillTitleTextBlock.Text = "Select an open bill";
                SelectedTableBillInfoTextBlock.Text = "";
                SelectedBillTotalTextBlock.Text = "Bill Total: 0.00";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to refresh tables tab.\n" + ex.Message);
            }
        }


        private void AddMoreItemsFromTable_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_selectedTablesBill == null)
                {
                    MessageBox.Show("Select an open bill first.");
                    return;
                }

                LoadBillIntoCart(_selectedTablesBill.Id);
                SelectCurrentBillInPosDropdown(_selectedTablesBill.Id);

                string tableName = string.IsNullOrWhiteSpace(_selectedTablesBill.TableName)
                    ? "Quick Sale"
                    : _selectedTablesBill.TableName;

                PosModeTextBlock.Text = $"Mode: Add items to Bill No: {_selectedTablesBill.VisibleBillNumber} | Table: {tableName}";

                MainTabControl.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to prepare Add More Items flow.\n" + ex.Message);
            }
        }

        private void PayNowFromPos_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_cart == null || _cart.Count == 0)
                {
                    MessageBox.Show("Cart is empty.");
                    return;
                }

                if (PosQuickPaymentMethodComboBox.SelectedItem is not string paymentMethod || string.IsNullOrWhiteSpace(paymentMethod))
                {
                    MessageBox.Show("Select payment method.");
                    return;
                }

                int billId;
                string saleTypeText;
                string tableName;

                if (_currentTargetBillId.HasValue)
                {
                    billId = _currentTargetBillId.Value;
                    saleTypeText = "Existing Bill";
                    tableName = GetBillTableName(billId);
                    string visibleBillNo = GetVisibleBillNumber(billId);

                    decimal existingBillTotal = _billItemRepository.GetBillTotal(billId);

                    var newLines = _cart.Where(x => x != null && !x.IsExistingItem).ToList();
                    if (newLines.Count > 0)
                    {
                        SaveCartItemsToBill(billId);
                        existingBillTotal = _billItemRepository.GetBillTotal(billId);
                    }

                    _paymentRepository.Add(new Payment
                    {
                        BillId = billId,
                        PaymentMethod = paymentMethod,
                        Amount = existingBillTotal,
                        PaidAt = DateTime.Now
                    });

                    _billRepository.SettleBill(billId);

                    MessageBox.Show(
      $"Payment completed.\nType: {saleTypeText}\nTable: {tableName}\nBill No: {visibleBillNo}\nTotal: {existingBillTotal:F2}\nMethod: {paymentMethod}");
                    var receipt = BuildReceiptData(billId, paymentMethod, saleTypeText, tableName);
                    _receiptPrintService.PrintReceipt(receipt, _appSettings.ReceiptPrinterName);

                }
                else
                {
                    var bill = new Bill
                    {
                        DiningTableId = null,
                        BillType = "QUICK",
                        Status = "OPEN",
                        CreatedAt = DateTime.Now
                    };

                    billId = _billRepository.Create(bill);
                    SaveCartItemsToBill(billId);
                    string visibleBillNo = GetVisibleBillNumber(billId);

                    decimal total = _cart.Sum(x => x?.LineTotal ?? 0);
                    saleTypeText = "Quick Sale";
                    tableName = "Quick Sale";

                    _paymentRepository.Add(new Payment
                    {
                        BillId = billId,
                        PaymentMethod = paymentMethod,
                        Amount = total,
                        PaidAt = DateTime.Now
                    });

                    _billRepository.SettleBill(billId);

                    MessageBox.Show(
      $"Payment completed.\nType: {saleTypeText}\nTable: {tableName}\nBill No: {visibleBillNo}\nTotal: {total:F2}\nMethod: {paymentMethod}");
                    var receipt = BuildReceiptData(billId, paymentMethod, saleTypeText, tableName);
                    _receiptPrintService.PrintReceipt(receipt, _appSettings.ReceiptPrinterName);

                }

                _cart.Clear();
                ResetPosBillSelection();
                RefreshCartView();
                RefreshAllOpenBillViews();
                LoadReports();
                RunBackup(showMessage: false);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to complete payment.\n" + ex.Message);
            }
        }
        
        // -----------------------------
        // MANAGEMENT
        // -----------------------------
        private void LoadCategories()
        {
            CategoryComboBox.ItemsSource = null;
            CategoryComboBox.ItemsSource = _categoryRepository.GetActiveCategories();
        }

        private void LoadCategoriesGrid()
        {
            CategoriesDataGrid.ItemsSource = null;
            CategoriesDataGrid.ItemsSource = _categoryRepository.GetAll();
        }

        private void LoadProducts()
        {
            ProductsDataGrid.ItemsSource = null;
            ProductsDataGrid.ItemsSource = _productRepository.GetAllForDisplay();
        }

        private void LoadTables()
        {
            TablesDataGrid.ItemsSource = null;
            TablesDataGrid.ItemsSource = _diningTableRepository.GetActiveTables();
        }

        private void SaveCategory_Click(object sender, RoutedEventArgs e)
        {
            var name = CategoryNameTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Enter category name.");
                return;
            }

            _categoryRepository.Add(name);
            CategoryNameTextBox.Clear();

            LoadCategories();
            LoadCategoriesGrid();
            LoadPosCategories();

            MessageBox.Show("Category saved.");
        }

        private void DeactivateSelectedCategory_Click(object sender, RoutedEventArgs e)
        {
            if (CategoriesDataGrid.SelectedItem is not Category selectedCategory)
            {
                MessageBox.Show("Select a category first.");
                return;
            }

            _categoryRepository.Deactivate(selectedCategory.Id);

            LoadCategories();
            LoadCategoriesGrid();
            LoadPosCategories();
            LoadPosProducts();

            MessageBox.Show("Category deactivated.");
        }

        private void SaveProduct_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var name = ProductNameTextBox.Text?.Trim() ?? "";

                if (string.IsNullOrWhiteSpace(name))
                {
                    MessageBox.Show("Enter product name.");
                    return;
                }

                if (CategoryComboBox.SelectedItem is not Category selectedCategory)
                {
                    MessageBox.Show("Select a category.");
                    return;
                }

                if (!decimal.TryParse(PriceTextBox.Text?.Trim(), out decimal price))
                {
                    MessageBox.Show("Enter valid price.");
                    return;
                }

                if (_selectedManagementProduct == null)
                {
                    var product = new Product
                    {
                        Name = name,
                        CategoryId = selectedCategory.Id,
                        SellingPrice = price,
                        IsKitchenItem = KitchenItemCheckBox.IsChecked == true,
                        IsActive = true
                    };

                    _productRepository.Add(product);
                    MessageBox.Show("Product saved.");
                }
                else
                {
                    var updatedProduct = new Product
                    {
                        Id = _selectedManagementProduct.Id,
                        Name = name,
                        CategoryId = selectedCategory.Id,
                        SellingPrice = price,
                        IsKitchenItem = KitchenItemCheckBox.IsChecked == true,
                        IsActive = true
                    };

                    _productRepository.Update(updatedProduct);
                    MessageBox.Show("Product updated.");
                }

                ClearProductForm();
                LoadProducts();
                LoadPosProducts();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to save/update product.\n" + ex.Message);
            }
        }
        private void DeactivateSelectedProduct_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ProductsDataGrid.SelectedItem is not ProductDisplay selectedProduct)
                {
                    MessageBox.Show("Select a product first.");
                    return;
                }

                _productRepository.Deactivate(selectedProduct.Id);

                ClearProductForm();
                LoadProducts();
                LoadPosProducts();

                MessageBox.Show("Product deactivated.");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to deactivate product.\n" + ex.Message);
            }
        }

        private void SaveTable_Click(object sender, RoutedEventArgs e)
        {
            var tableName = TableNameTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(tableName))
            {
                MessageBox.Show("Enter table name.");
                return;
            }

            _diningTableRepository.Add(tableName);
            TableNameTextBox.Clear();

            LoadTables();
            LoadPosTables();

            MessageBox.Show("Table saved.");
        }

        private void DeactivateSelectedTable_Click(object sender, RoutedEventArgs e)
        {
            if (TablesDataGrid.SelectedItem is not DiningTable selectedTable)
            {
                MessageBox.Show("Select a table first.");
                return;
            }

            _diningTableRepository.Deactivate(selectedTable.Id);

            LoadTables();
            LoadPosTables();

            MessageBox.Show("Table deactivated.");
        }

        // -----------------------------
        // REPORTS
        // -----------------------------
        private void LoadReports()
        {
            try
            {
                var selectedDate = ReportDatePicker.SelectedDate ?? DateTime.Today;
                string reportDate = selectedDate.ToString("yyyy-MM-dd");

                decimal sales = _paymentRepository.GetSalesTotalBySingleDate(reportDate);
                int paidBills = _paymentRepository.GetPaidBillCountBySingleDate(reportDate);

                TodaySalesTextBlock.Text = sales.ToString("F2");
                TodayBillCountTextBlock.Text = paidBills.ToString();
                OpenBillsCountTextBlock.Text = _billRepository.GetOpenBillsCount().ToString();

                ItemSalesReportDataGrid.ItemsSource = null;
                ItemSalesReportDataGrid.ItemsSource = _billItemRepository.GetItemSalesReportBySingleDate(reportDate);

                LoadExpensesForSelectedDate();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to load reports.\n" + ex.Message);
            }
        }

        private void LoadSingleDateReport_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ReportDatePicker.SelectedDate == null)
                {
                    MessageBox.Show("Select a report date.");
                    return;
                }

                LoadReports();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to load single date report.\n" + ex.Message);
            }
        }



        private void RefreshReports_Click(object sender, RoutedEventArgs e)
        {
            LoadReports();
        }

        // -----------------------------
        // SHARED
        // -----------------------------
        private void LoadPaymentMethods()
        {
            var methods = new[] { "CASH", "CARD" };

            PosQuickPaymentMethodComboBox.ItemsSource = methods;
            PosQuickPaymentMethodComboBox.SelectedIndex = 0;


        }

        private void RefreshAllOpenBillViews()
        {
            try
            {
                LoadPosOpenBills();
                LoadReports();
                LoadPosTables();
                SyncTablesTabAfterBillChange();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to refresh views.\n" + ex.Message);
            }
        }

        private string BuildKotPreviewText(int billId)
        {
            try
            {
                var pendingItems = _billItemRepository.GetPendingKitchenItemsByBillId(billId);

                if (pendingItems == null || pendingItems.Count == 0)
                    return "No kitchen items to send.";

                string tableText = GetBillTableName(billId);

                var lines = new List<string>
        {
            "KITCHEN ORDER TICKET",
            $"Bill ID: {billId}",
            $"Table: {tableText}",
            $"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
            "--------------------------------"
        };

                foreach (var item in pendingItems)
                {
                    if (item == null)
                        continue;

                    lines.Add($"{item.ProductName} x {item.Quantity}");
                }

                lines.Add("--------------------------------");

                return string.Join(Environment.NewLine, lines);
            }
            catch (Exception ex)
            {
                return "Failed to build KOT preview.\n" + ex.Message;
            }
        }
        //private void SendKitchenItems_Click(object sender, RoutedEventArgs e)
        //{
        //    try
        //    {
        //        if (!ValidateCart())
        //            return;

        //        if (!_currentTargetBillId.HasValue || _currentTargetBillId.Value <= 0)
        //        {
        //            MessageBox.Show("Open the table bill first, then send kitchen items.");
        //            return;
        //        }

        //        bool hasNewKitchenItems = _cart.Any(x => x != null && !x.IsExistingItem && x.IsKitchenItem);
        //        if (!hasNewKitchenItems)
        //        {
        //            MessageBox.Show("There are no new kitchen items to send.");
        //            return;
        //        }

        //        int billId = _currentTargetBillId.Value;

        //        var savedProductIds = SaveOnlyNewCartItemsToBillAndReturnProductIds(billId);

        //        if (savedProductIds == null || savedProductIds.Count == 0)
        //        {
        //            MessageBox.Show("No new items were saved.");
        //            return;
        //        }

        //        string kotText = BuildKotPreviewText(billId);

        //        if (string.IsNullOrWhiteSpace(kotText) || kotText == "No kitchen items to send.")
        //        {
        //            MessageBox.Show(kotText ?? "No kitchen items to send.");
        //            ReloadCurrentBillIntoCartIfAny();
        //            RefreshAllOpenBillViews();
        //            return;
        //        }

        //        _billItemRepository.MarkSpecificKitchenItemsAsPrinted(billId, savedProductIds);

        //        MessageBox.Show(kotText, "KOT Preview");

        //        LoadBillIntoCart(billId);
        //        RefreshAllOpenBillViews();
        //    }
        //    catch (Exception ex)
        //    {
        //        MessageBox.Show("Failed to send kitchen items.\n" + ex.Message);
        //    }
        //}
        private string GetBillTableDisplayText(int billId)
        {
            try
            {
                var openBills = _billRepository.GetOpenBillsForDisplay();
                var bill = openBills.FirstOrDefault(x => x.Id == billId);

                if (bill == null)
                    return "Unknown Table";

                return string.IsNullOrWhiteSpace(bill.TableName) ? "Quick Sale" : bill.TableName;
            }
            catch
            {
                return "Unknown Table";
            }
        }

        private string GetBillTableName(int billId)
        {
            try
            {
                if (billId <= 0)
                    return "Unknown Table";

                var bill = _billRepository.GetOpenBillsForDisplay()
                    .FirstOrDefault(x => x.Id == billId);

                if (bill != null && !string.IsNullOrWhiteSpace(bill.TableName))
                    return bill.TableName;

                return "Quick Sale";
            }
            catch
            {
                return "Unknown Table";
            }
        }

        private void LoadBillIntoCart(int billId)
        {
            try
            {
                if (billId <= 0)
                {
                    MessageBox.Show("Invalid bill id.");
                    return;
                }

                var billItems = _billItemRepository.GetByBillIdForDisplay(billId);

                _cart.Clear();

                foreach (var item in billItems)
                {
                    if (item == null)
                        continue;

                    var matchingProduct = _activeProducts.FirstOrDefault(p => p.Name == item.ProductName);

                    _cart.Add(new PosCartLine
                    {
                        BillItemId = item.Id,
                        ProductId = matchingProduct?.Id ?? 0,
                        ProductName = item.ProductName ?? "",
                        UnitPrice = item.UnitPrice,
                        Quantity = item.Quantity,
                        IsKitchenItem = false,
                        IsExistingItem = true
                    });
                }

                _currentTargetBillId = billId;
                RefreshCartView();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to load bill into POS cart.\n" + ex.Message);
            }
        }

        private void PosExistingBillComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (PosExistingBillComboBox.SelectedItem is not OpenBillDisplay selectedBill)
                    return;

                LoadBillIntoCart(selectedBill.Id);
                _currentTargetBillId = selectedBill.Id;

                string tableName = string.IsNullOrWhiteSpace(selectedBill.TableName)
                    ? "Quick Sale"
                    : selectedBill.TableName;

                PosModeTextBlock.Text = $"Mode: Add items to Bill No: {selectedBill.VisibleBillNumber} | Table: {tableName}";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to load selected bill into POS.\n" + ex.Message);
            }
        }


        private void ResetPosBillSelection()
        {
            try
            {
                PosExistingBillComboBox.SelectedIndex = -1;
                _currentTargetBillId = null;
            }
            catch
            {
            }
        }

        private void OpenSelectedBillForSettlement_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_selectedTablesBill == null)
                {
                    MessageBox.Show("Select an open bill first.");
                    return;
                }

                LoadBillIntoCart(_selectedTablesBill.Id);
                SelectCurrentBillInPosDropdown(_selectedTablesBill.Id);

                string tableName = string.IsNullOrWhiteSpace(_selectedTablesBill.TableName)
                    ? "Quick Sale"
                    : _selectedTablesBill.TableName;

                PosModeTextBlock.Text = $"Mode: Settlement for Bill No: {_selectedTablesBill.VisibleBillNumber} | Table: {tableName}";
                MainTabControl.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to open bill for settlement.\n" + ex.Message);
            }
        }
        private void LoadExpensesForSelectedDate()
        {
            try
            {
                var selectedDate = ReportDatePicker.SelectedDate ?? DateTime.Today;
                string reportDate = selectedDate.ToString("yyyy-MM-dd");

                var expenses = _expenseRepository.GetByDate(reportDate);
                decimal totalExpenses = _expenseRepository.GetTotalByDate(reportDate);

                ExpensesDataGrid.ItemsSource = null;
                ExpensesDataGrid.ItemsSource = expenses;

                TotalExpenseTextBlock.Text = $"Total Expenses: {totalExpenses:F2}";

                decimal sales = _paymentRepository.GetSalesTotalBySingleDate(reportDate);
                decimal net = sales - totalExpenses;

                NetSalesTextBlock.Text = $"Net: {net:F2}";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to load expenses.\n" + ex.Message);
            }
        }

        private void AddExpenseButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var selectedDate = ReportDatePicker.SelectedDate ?? DateTime.Today;
                string reportDate = selectedDate.ToString("yyyy-MM-dd");

                string description = ExpenseDescriptionTextBox.Text?.Trim() ?? "";
               

                if (string.IsNullOrWhiteSpace(description))
                {
                    MessageBox.Show("Enter expense description.");
                    return;
                }

                if (!decimal.TryParse(ExpenseAmountTextBox.Text?.Trim(), out decimal amount) || amount <= 0)
                {
                    MessageBox.Show("Enter valid expense amount.");
                    return;
                }

                var expense = new Expense
                {
                    ExpenseDate = reportDate,
                    Description = description,
                    Amount = amount,
                    CreatedAt = DateTime.Now
                };

                _expenseRepository.Add(expense);

                ExpenseDescriptionTextBox.Clear();
                ExpenseAmountTextBox.Clear();
               

                LoadReports();
                LoadExpensesForSelectedDate();

                MessageBox.Show("Expense added.");
                RunBackup(showMessage: false);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to add expense.\n" + ex.Message);
            }
        }
        private void ClearProductForm()
        {
            try
            {
                _selectedManagementProduct = null;

                ProductNameTextBox.Clear();
                PriceTextBox.Clear();
                KitchenItemCheckBox.IsChecked = false;
                CategoryComboBox.SelectedIndex = -1;

                SaveProductButton.Content = "Save Product";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to clear product form.\n" + ex.Message);
            }
        }

        private void LoadSelectedProductIntoForm(ProductDisplay selectedProduct)
        {
            try
            {
                if (selectedProduct == null)
                {
                    MessageBox.Show("Invalid product selection.");
                    return;
                }

                _selectedManagementProduct = selectedProduct;

                ProductNameTextBox.Text = selectedProduct.Name ?? "";
                PriceTextBox.Text = selectedProduct.SellingPrice.ToString("F2");
                KitchenItemCheckBox.IsChecked = selectedProduct.IsKitchenItem;

                var categories = _categoryRepository.GetActiveCategories();
                var matchingCategory = categories.FirstOrDefault(c => c.Name == selectedProduct.CategoryName);

                CategoryComboBox.ItemsSource = null;
                CategoryComboBox.ItemsSource = categories;
                CategoryComboBox.SelectedItem = matchingCategory;

                SaveProductButton.Content = "Update Product";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to load product into form.\n" + ex.Message);
            }
        }

        private void ProductsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (ProductsDataGrid.SelectedItem is not ProductDisplay selectedProduct)
                    return;

                LoadSelectedProductIntoForm(selectedProduct);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to select product.\n" + ex.Message);
            }
        }


        private void RenderOpenBillCards(List<OpenBillDisplay> openBills)
        {
            try
            {
                OpenBillsCardsPanel.Children.Clear();

                if (openBills == null || openBills.Count == 0)
                {
                    OpenBillsCardsPanel.Children.Add(new TextBlock
                    {
                        Text = "No open bills.",
                        FontSize = 16,
                        Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                        Margin = new Thickness(8)
                    });
                    return;
                }

                foreach (var bill in openBills)
                {
                    if (bill == null)
                        continue;

                    var border = new Border
                    {
                        CornerRadius = new CornerRadius(12),
                        BorderThickness = new Thickness(1),
                        BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                        Background = (_selectedTablesBill != null && _selectedTablesBill.Id == bill.Id)
                            ? new SolidColorBrush(Color.FromRgb(219, 234, 254))
                            : Brushes.White,
                        Padding = new Thickness(12),
                        Margin = new Thickness(0, 0, 0, 10),
                        Cursor = System.Windows.Input.Cursors.Hand
                    };

                    var stack = new StackPanel();

                    stack.Children.Add(new TextBlock
                    {
                        Text = bill.CardTitle,
                        FontSize = 20,
                        FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42))
                    });

                    stack.Children.Add(new TextBlock
                    {
                        Text = bill.CardSubTitle,
                        FontSize = 14,
                        Margin = new Thickness(0, 4, 0, 0),
                        Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105))
                    });

                    stack.Children.Add(new TextBlock
                    {
                        Text = $"Opened: {bill.CreatedAt}",
                        FontSize = 13,
                        Margin = new Thickness(0, 8, 0, 0),
                        Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139))
                    });

                    stack.Children.Add(new TextBlock
                    {
                        Text = $"Status: {bill.Status}",
                        FontSize = 13,
                        Margin = new Thickness(0, 2, 0, 0),
                        Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139))
                    });

                    stack.Children.Add(new TextBlock
                    {
                        Text = $"Total: {bill.TotalAmount:F2}",
                        FontSize = 16,
                        FontWeight = FontWeights.Bold,
                        Margin = new Thickness(0, 10, 0, 0),
                        Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42))
                    });

                    border.Child = stack;

                    border.MouseLeftButtonUp += (s, e) =>
                    {
                        SelectOpenBillCard(bill);
                    };

                    OpenBillsCardsPanel.Children.Add(border);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to render open bill cards.\n" + ex.Message);
            }
        }

        private void SelectOpenBillCard(OpenBillDisplay selectedBill)
        {
            try
            {
                if (selectedBill == null)
                {
                    MessageBox.Show("Invalid bill selection.");
                    return;
                }

                _selectedTablesBill = selectedBill;

                string tableName = string.IsNullOrWhiteSpace(selectedBill.TableName)
                    ? "Quick Sale"
                    : selectedBill.TableName;

                decimal total = _billItemRepository.GetBillTotal(selectedBill.Id);

                SelectedTableBillTitleTextBlock.Text = $"Bill No: {selectedBill.VisibleBillNumber} - {tableName}";
                SelectedTableBillInfoTextBlock.Text =
                    $"Type: {selectedBill.BillType}    |    Opened At: {selectedBill.CreatedAt}    |    Status: {selectedBill.Status}";

                SelectedBillItemsDataGrid.ItemsSource = null;
                SelectedBillItemsDataGrid.ItemsSource = _billItemRepository.GetByBillIdForDisplay(selectedBill.Id);

                SelectedBillTotalTextBlock.Text = $"Bill Total: {total:F2}";

                var openBills = _billRepository.GetOpenBillsForDisplay();
                RenderOpenBillCards(openBills);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to select bill card.\n" + ex.Message);
            }
        }


        private List<int> SaveOnlyNewCartItemsToBillAndReturnProductIds(int billId)
        {
            try
            {
                if (billId <= 0)
                    throw new Exception("Invalid bill id.");

                if (_cart == null || _cart.Count == 0)
                    throw new Exception("Cart is empty.");

                var newLines = _cart.Where(x => x != null && !x.IsExistingItem).ToList();

                if (newLines.Count == 0)
                    return new List<int>();

                var savedProductIds = new List<int>();

                foreach (var line in newLines)
                {
                    if (line == null)
                        continue;

                    if (line.ProductId <= 0 || line.Quantity <= 0)
                        continue;

                    _billItemRepository.Add(new BillItem
                    {
                        BillId = billId,
                        ProductId = line.ProductId,
                        UnitPrice = line.UnitPrice,
                        Quantity = line.Quantity,
                        Status = "ACTIVE",
                        IsKitchenPrinted = false
                    });

                    savedProductIds.Add(line.ProductId);
                }

                return savedProductIds;
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to save new cart items. " + ex.Message, ex);
            }
        }

        private void ReloadCurrentBillIntoCartIfAny()
        {
            try
            {
                if (_currentTargetBillId.HasValue && _currentTargetBillId.Value > 0)
                {
                    LoadBillIntoCart(_currentTargetBillId.Value);
                }
                else
                {
                    RefreshCartView();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to reload current bill into cart.\n" + ex.Message);
            }
        }

        private void LoadDefaultReportDates()
        {
            try
            {
                ReportDatePicker.SelectedDate = DateTime.Today;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to load default report date.\n" + ex.Message);
            }
        }

        private void SelectCurrentBillInPosDropdown(int billId)
        {
            try
            {
                if (billId <= 0)
                    return;

                LoadPosOpenBills();

                if (PosExistingBillComboBox.ItemsSource is IEnumerable<OpenBillDisplay> bills)
                {
                    var matchingBill = bills.FirstOrDefault(x => x != null && x.Id == billId);
                    if (matchingBill != null)
                    {
                        PosExistingBillComboBox.SelectedItem = matchingBill;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to select current bill in POS dropdown.\n" + ex.Message);
            }
        }

        private void SyncTablesTabAfterBillChange()
        {
            try
            {
                var previouslySelectedBillId = _selectedTablesBill?.Id;

                var openBills = _billRepository.GetOpenBillsForDisplay();

                TablesOpenBillsCountTextBlock.Text = $"Open Bills: {openBills.Count}";
                RenderOpenBillCards(openBills);

                if (previouslySelectedBillId == null)
                {
                    return;
                }

                var stillOpenBill = openBills.FirstOrDefault(x => x != null && x.Id == previouslySelectedBillId.Value);

                if (stillOpenBill == null)
                {
                    _selectedTablesBill = null;
                    SelectedBillItemsDataGrid.ItemsSource = null;
                    SelectedTableBillTitleTextBlock.Text = "Select an open bill";
                    SelectedTableBillInfoTextBlock.Text = "";
                    SelectedBillTotalTextBlock.Text = "Bill Total: 0.00";
                    return;
                }

                _selectedTablesBill = stillOpenBill;
                SelectOpenBillCard(stillOpenBill);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to sync tables tab.\n" + ex.Message);
            }
        }

        private ReceiptData BuildReceiptData(int billId, string paymentMethod, string billTypeText, string tableName)
        {
            try
            {
                if (billId <= 0)
                    throw new Exception("Invalid bill id.");

                var items = _billItemRepository.GetReceiptLinesByBillId(billId);
                decimal total = items.Sum(x => x.LineTotal);

                return new ReceiptData
                {
                    BusinessName = "Smart Billing System POS",
                    BillNo = GetVisibleBillNumber(billId),
                    TableName = string.IsNullOrWhiteSpace(tableName) ? "Quick Sale" : tableName,
                    BillType = billTypeText,
                    PaymentMethod = paymentMethod,
                    PrintedAt = DateTime.Now,
                    Total = total,
                    Items = items
                };
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to build receipt data. " + ex.Message, ex);
            }
        }

        private void LoadAppSettings()
        {
            try
            {
                _appSettings = _settingsService.Load();

                BackupFolderTextBox.Text = _appSettings.BackupFolderPath ?? "";
                BackupIntervalTextBox.Text = _appSettings.BackupIntervalMinutes.ToString();

                if (!string.IsNullOrWhiteSpace(_appSettings.BackupFolderPath))
                    BackupStatusTextBlock.Text = $"Backup folder: {_appSettings.BackupFolderPath}";
                else
                    BackupStatusTextBlock.Text = "Backup folder not selected.";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to load app settings.\n" + ex.Message);
                _appSettings = new AppSettings();
            }
        }

        private void LoadPrinters()
        {
            try
            {
                var printServer = new LocalPrintServer();
                var queues = printServer.GetPrintQueues();

                var printerNames = queues
                    .Select(q => q.Name)
                    .OrderBy(x => x)
                    .ToList();

                ReceiptPrinterComboBox.ItemsSource = null;
                ReceiptPrinterComboBox.ItemsSource = printerNames;

                if (!string.IsNullOrWhiteSpace(_appSettings.ReceiptPrinterName) &&
                    printerNames.Contains(_appSettings.ReceiptPrinterName))
                {
                    ReceiptPrinterComboBox.SelectedItem = _appSettings.ReceiptPrinterName;
                    PrinterSettingsStatusTextBlock.Text = $"Saved printer: {_appSettings.ReceiptPrinterName}";
                }
                else
                {
                    PrinterSettingsStatusTextBlock.Text = "No saved receipt printer selected.";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to load printers.\n" + ex.Message);
            }
        }

        private void SavePrinterSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ReceiptPrinterComboBox.SelectedItem is not string selectedPrinter ||
                    string.IsNullOrWhiteSpace(selectedPrinter))
                {
                    MessageBox.Show("Select a printer first.");
                    return;
                }

                _appSettings.ReceiptPrinterName = selectedPrinter;
                _settingsService.Save(_appSettings);

                PrinterSettingsStatusTextBlock.Text = $"Saved printer: {selectedPrinter}";
                MessageBox.Show("Printer settings saved.");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to save printer settings.\n" + ex.Message);
            }
        }

        private void TestReceiptPrinter_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ReceiptPrinterComboBox.SelectedItem is not string selectedPrinter ||
                    string.IsNullOrWhiteSpace(selectedPrinter))
                {
                    MessageBox.Show("Select a printer first.");
                    return;
                }

                _receiptPrintService.PrintTest(selectedPrinter);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to test printer.\n" + ex.Message);
            }
        }

        private void ReloadPrinters_Click(object sender, RoutedEventArgs e)
        {
            LoadPrinters();
        }

        private void ChooseBackupFolder_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Copy your Google Drive backup folder path and paste it into the Backup Folder box.");
        }

        private void SaveBackupSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string folder = BackupFolderTextBox.Text?.Trim() ?? "";

                if (string.IsNullOrWhiteSpace(folder))
                {
                    MessageBox.Show("Select backup folder.");
                    return;
                }

                if (!int.TryParse(BackupIntervalTextBox.Text?.Trim(), out int minutes) || minutes <= 0)
                {
                    MessageBox.Show("Enter valid backup interval minutes.");
                    return;
                }

                _appSettings.BackupFolderPath = folder;
                _appSettings.BackupIntervalMinutes = minutes;

                _settingsService.Save(_appSettings);

                BackupStatusTextBlock.Text = $"Backup saved. Folder: {folder} | Interval: {minutes} minutes";

                StartAutoBackupTimer();

                MessageBox.Show("Backup settings saved.");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to save backup settings.\n" + ex.Message);
            }
        }

        private void BackupNow_Click(object sender, RoutedEventArgs e)
        {
            RunBackup(showMessage: true);
        }

        private void StartAutoBackupTimer()
        {
            try
            {
                _backupTimer?.Stop();

                if (_appSettings == null)
                    return;

                if (string.IsNullOrWhiteSpace(_appSettings.BackupFolderPath))
                {
                    BackupStatusTextBlock.Text = "Auto backup not started. Backup folder not selected.";
                    return;
                }

                int minutes = _appSettings.BackupIntervalMinutes <= 0
                    ? 15
                    : _appSettings.BackupIntervalMinutes;

                _backupTimer = new DispatcherTimer();
                _backupTimer.Interval = TimeSpan.FromMinutes(minutes);
                _backupTimer.Tick += (s, e) => RunBackup(showMessage: false);
                _backupTimer.Start();

                BackupStatusTextBlock.Text = $"Auto backup running every {minutes} minutes.";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to start auto backup.\n" + ex.Message);
            }
        }

        private void RunBackup(bool showMessage)
        {
            try
            {
                if (_appSettings == null || string.IsNullOrWhiteSpace(_appSettings.BackupFolderPath))
                {
                    if (showMessage)
                        MessageBox.Show("Backup folder is not selected.");
                    return;
                }

                string backupPath = _backupService.BackupNow(_appSettings.BackupFolderPath);

                BackupStatusTextBlock.Text = $"Last backup: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n{backupPath}";

                if (showMessage)
                    MessageBox.Show("Backup completed successfully.");
            }
            catch (Exception ex)
            {
                BackupStatusTextBlock.Text = "Backup failed: " + ex.Message;

                if (showMessage)
                    MessageBox.Show("Backup failed.\n" + ex.Message);
            }
        }
    }
}