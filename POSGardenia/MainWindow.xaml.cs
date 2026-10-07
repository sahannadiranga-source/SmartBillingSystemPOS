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
        private readonly ProductSetupService _productSetupService = new();
        private readonly StockItemRepository _stockItemRepository = new();
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

        private readonly DailyReportService _dailyReportService = new();
        private DispatcherTimer? _dailyReportTimer;
        private DateTime? _lastDailyReportGeneratedDate = null;

        private readonly ObservableCollection<PosCartLine> _cart = new();
        private List<Product> _activeProducts = new();
        private int? _selectedCategoryId = null;
        private int? _currentTargetBillId = null;
        private ProductDisplay? _selectedManagementProduct = null;
        private OpenBillDisplay? _selectedTablesBill = null;
        private int? _editingExpenseId = null;

        private BillHistoryDisplay? _selectedHistoryBill = null;
        public MainWindow()
        {
            InitializeComponent();

            WireProductSetupControls();
            WireInventoryControls();
            WirePosPaymentControls();

            LoadAppSettings();
            InitTouchSupport();
            BackupWarningBanner.MouseLeftButtonUp += BackupWarningBanner_Tapped;
            RefreshBackupBanner();
            LoadPrinters();
            StartAutoBackupTimer();
            StartDailyReportTimer();
            GenerateMissingYesterdayReportIfNeeded();

            LoadDefaultReportDates();

            BillHistoryDatePicker.SelectedDate = DateTime.Today;

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
                return _billRepository.GetVisibleBillNumber(billId);
            }
            catch
            {
                return $"#{billId}";
            }
        }

        // Shared rounded button look with pressed feedback (defined in MainWindow.xaml).
        private ControlTemplate? TouchTemplate() => TryFindResource("TouchButtonTemplate") as ControlTemplate;

        private Button CreateCategoryButton(string text, int? categoryId)
        {
            var button = new Button
            {
                Content = text,
                Height = 60,
                Margin = new Thickness(0, 0, 0, 12),
                FontSize = 17,
                Template = TouchTemplate(),
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

            // Products of the same main item sit together, biggest serving first.
            products = products
                .OrderBy(p => p.MainItemName ?? p.Name, StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(p => p.UnitsPerSale ?? 0)
                .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var product in products)
            {
                var (buttonColor, textColor) = PosColors.Resolve(product.CategoryButtonColor, product.IsKitchenItem);
                var button = new Button
                {
                    Width = 180,
                    Height = 110,
                    Margin = new Thickness(0, 0, 14, 14),
                    BorderThickness = new Thickness(0),
                    Template = TouchTemplate(),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Background = new SolidColorBrush(buttonColor),
                    Foreground = new SolidColorBrush(textColor),
                    Content = new StackPanel
                    {
                        Children =
                        {
                            new TextBlock
                            {
                                Text = product.Name,
                                FontSize = 16,
                                FontWeight = FontWeights.Bold,
                                TextAlignment = TextAlignment.Center,
                                TextWrapping = TextWrapping.Wrap
                            },
                            new TextBlock
                            {
                                Text = product.SellingPrice.ToString("F2"),
                                FontSize = 15,
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
                decimal cartTotal = _cart.Sum(x => x?.LineTotal ?? 0);
                CartTotalTextBlock.Text = $"Cart Total: {cartTotal:F2}";

                decimal alreadyPaid = _currentTargetBillId.HasValue
                    ? _paymentRepository.GetPaidTotalForBill(_currentTargetBillId.Value)
                    : 0;
                decimal due = Math.Max(0, cartTotal - alreadyPaid);

                PosPaymentSummaryTextBlock.Text = alreadyPaid > 0
                    ? $"Bill {cartTotal:F2} | Paid {alreadyPaid:F2} | Due {due:F2}"
                    : $"Due {due:F2}";
                PosPayAmountTextBox.Text = due.ToString("F2");

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

                var newBillItems = BuildNewBillItemsFromCart();

                if (newBillItems.Count == 0)
                    throw new Exception("No new items to save.");

                foreach (var item in newBillItems)
                {
                    item.BillId = billId;
                    _billItemRepository.Add(item);
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to save cart items to bill. " + ex.Message, ex);
            }
        }

        private List<BillItem> BuildNewBillItemsFromCart()
        {
            var billItems = new List<BillItem>();

            if (_cart == null || _cart.Count == 0)
                return billItems;

            foreach (var line in _cart.Where(x => x != null && !x.IsExistingItem))
            {
                if (line.ProductId <= 0 || line.Quantity <= 0)
                    continue;

                billItems.Add(new BillItem
                {
                    ProductId = line.ProductId,
                    UnitPrice = line.UnitPrice,
                    Quantity = line.Quantity,
                    Status = "ACTIVE",
                    IsKitchenPrinted = false
                });
            }

            return billItems;
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
                SendKitchenTicket(billId, selectedTable.TableName);
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
                    SendKitchenTicket(targetBillId, tableName);
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
                    SendKitchenTicket(targetBillId, tableName);
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
                SelectedBillPaidDueTextBlock.Visibility = Visibility.Collapsed;
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

        // Amount due for the current cart (what is still to be paid on it).
        private decimal GetPosDue()
        {
            decimal cartTotal = _cart?.Sum(x => x?.LineTotal ?? 0) ?? 0;
            decimal alreadyPaid = _currentTargetBillId.HasValue
                ? _paymentRepository.GetPaidTotalForBill(_currentTargetBillId.Value)
                : 0;
            return Math.Max(0, cartTotal - alreadyPaid);
        }

        // Handlers are attached here (not in XAML) so nothing fires during InitializeComponent.
        private void WirePosPaymentControls()
        {
            PosPayAmountTextBox.TextChanged += (_, _) => UpdatePosChange();
            PosQuickPaymentMethodComboBox.SelectionChanged += (_, _) => UpdatePosChange();
        }

        // Shows the change to give back while the cashier types the amount received.
        private void UpdatePosChange()
        {
            try
            {
                bool cash = PosQuickPaymentMethodComboBox.SelectedItem is string method
                    && string.Equals(method, "CASH", StringComparison.OrdinalIgnoreCase);

                if (cash && decimal.TryParse(PosPayAmountTextBox.Text?.Trim(), out decimal received) && _cart != null && _cart.Count > 0)
                {
                    decimal change = received - GetPosDue();
                    if (change > 0.005m)
                    {
                        PosChangeTextBlock.Text = $"Change: {change:F2}";
                        PosChangeBorder.Visibility = Visibility.Visible;
                        return;
                    }
                }

                PosChangeBorder.Visibility = Visibility.Collapsed;
            }
            catch
            {
                PosChangeBorder.Visibility = Visibility.Collapsed;
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

                decimal cartTotal = _cart.Sum(x => x?.LineTotal ?? 0);
                decimal alreadyPaid = _currentTargetBillId.HasValue
                    ? _paymentRepository.GetPaidTotalForBill(_currentTargetBillId.Value)
                    : 0;
                decimal dueBefore = Math.Max(0, cartTotal - alreadyPaid);

                // The box holds what the customer handed over. Only up to the amount due is recorded as
                // the payment (so sales and reports stay exact); the rest is given back as change.
                if (!decimal.TryParse(PosPayAmountTextBox.Text?.Trim(), out decimal received) || received <= 0)
                {
                    MessageBox.Show("Enter the amount received.");
                    return;
                }

                received = Math.Round(received, 2);

                if (received > dueBefore + 0.01m && !string.Equals(paymentMethod, "CASH", StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show($"A {paymentMethod} payment cannot be more than the amount due ({dueBefore:F2}).");
                    return;
                }

                decimal amount = Math.Min(received, dueBefore);
                decimal change = received - amount;

                int billId;
                string saleTypeText;
                string tableName;
                decimal total;
                decimal due;

                if (_currentTargetBillId.HasValue)
                {
                    billId = _currentTargetBillId.Value;
                    saleTypeText = "Existing Bill";
                    tableName = GetBillTableName(billId);

                    var result = _billRepository.AddItemsAndPayBill(
                        billId,
                        BuildNewBillItemsFromCart(),
                        paymentMethod,
                        amount,
                        DateTime.Now);

                    total = result.Total;
                    due = result.Due;
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

                    var result = _billRepository.CreateQuickSaleAndPay(
                        bill,
                        BuildNewBillItemsFromCart(),
                        paymentMethod,
                        amount,
                        DateTime.Now);

                    billId = result.BillId;
                    total = result.Total;
                    due = result.Due;
                    saleTypeText = "Quick Sale";
                    tableName = "Quick Sale";
                }

                // Kitchen first: food should start while the receipt is still printing.
                SendKitchenTicket(billId, tableName);

                string visibleBillNo = GetVisibleBillNumber(billId);
                string dueText = due > 0
                    ? $"\nDue balance: {due:F2} (bill stays open)"
                    : "\nBill fully paid.";
                string changeText = change > 0
                    ? $"\nReceived: {received:F2}\n\nCHANGE: {change:F2}"
                    : "";

                MessageBox.Show(
  $"Payment completed.\nType: {saleTypeText}\nTable: {tableName}\nBill No: {visibleBillNo}\nTotal: {total:F2}\nPaid now: {amount:F2} ({paymentMethod}){dueText}{changeText}");

                var receipt = BuildReceiptData(billId, saleTypeText, tableName);
                receipt.ReceivedAmount = received;
                receipt.ChangeAmount = change;
                _receiptPrintService.PrintReceipt(receipt, _appSettings.ReceiptPrinterName);

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
            TablesDataGrid.ItemsSource = _diningTableRepository.GetAll();
        }

        // Adds a new category, or updates the one loaded into the form (selected in the list).
        private void SaveCategory_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var name = CategoryNameTextBox.Text.Trim();

                if (string.IsNullOrWhiteSpace(name))
                {
                    MessageBox.Show("Enter category name.");
                    return;
                }

                bool kitchen = CategoryKitchenCheckBox.IsChecked == true;
                string? buttonColor = SelectedCategoryColor();
                var existing = _categoryRepository.GetByName(name);

                if (_selectedManagementCategory == null)
                {
                    if (existing != null)
                    {
                        MessageBox.Show(existing.IsActive
                            ? $"A category named '{name}' already exists."
                            : $"A category named '{name}' already exists but is deactivated. Select it and click Reactivate Selected instead of creating a new one.");
                        return;
                    }

                    _categoryRepository.Add(name, kitchen, buttonColor);
                    MessageBox.Show("Category saved.");
                }
                else
                {
                    if (existing != null && existing.Id != _selectedManagementCategory.Id)
                    {
                        MessageBox.Show($"A category named '{name}' already exists.");
                        return;
                    }

                    _categoryRepository.Update(_selectedManagementCategory.Id, name, kitchen, buttonColor);
                    MessageBox.Show("Category updated.");
                }

                ClearCategoryForm();
                RefreshCategoryViews();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to save/update category.\n" + ex.Message);
            }
        }

        private Category? _selectedManagementCategory = null;

        // Selecting a category in the list loads it into the form and switches Save to Update.
        private void LoadSelectedCategoryIntoForm(Category selected)
        {
            _selectedManagementCategory = selected;

            CategoryNameTextBox.Text = selected.Name;
            CategoryKitchenCheckBox.IsChecked = selected.IsKitchenItem;
            CategoryColorComboBox.SelectedItem = PosColors.Normalize(selected.ButtonColor) ?? PosColors.Automatic;
            SaveCategoryButton.Content = "Update Category";
        }

        // null = Automatic
        private string? SelectedCategoryColor()
        {
            return CategoryColorComboBox.SelectedItem is string name && name != PosColors.Automatic ? name : null;
        }

        // The little swatch beside the colour box shows what the POS buttons will look like.
        private void UpdateCategoryColorSwatch()
        {
            var (background, _) = PosColors.Resolve(SelectedCategoryColor(), CategoryKitchenCheckBox.IsChecked == true);
            CategoryColorSwatch.Background = new SolidColorBrush(background);
        }

        private void ClearCategoryForm()
        {
            _selectedManagementCategory = null;

            CategoryNameTextBox.Clear();
            CategoryKitchenCheckBox.IsChecked = false;
            CategoryColorComboBox.SelectedItem = PosColors.Automatic;
            SaveCategoryButton.Content = "Save Category";
            CategoriesDataGrid.SelectedItem = null;
        }

        private void ClearCategoryForm_Click(object sender, RoutedEventArgs e) => ClearCategoryForm();

        // A category change can affect the pickers, the product list (category name, kitchen flag) and the POS.
        private void RefreshCategoryViews()
        {
            LoadCategories();
            LoadCategoriesGrid();
            LoadProducts();
            LoadPosCategories();
            LoadPosProducts();
        }

        private void DeactivateSelectedCategory_Click(object sender, RoutedEventArgs e)
        {
            if (CategoriesDataGrid.SelectedItem is not Category selectedCategory)
            {
                MessageBox.Show("Select a category first.");
                return;
            }

            _categoryRepository.Deactivate(selectedCategory.Id);

            ClearCategoryForm();
            LoadCategories();
            LoadCategoriesGrid();
            LoadPosCategories();
            LoadPosProducts();

            MessageBox.Show("Category deactivated.");
        }

        private void ReactivateSelectedCategory_Click(object sender, RoutedEventArgs e)
        {
            if (CategoriesDataGrid.SelectedItem is not Category selectedCategory)
            {
                MessageBox.Show("Select a category first.");
                return;
            }

            _categoryRepository.Reactivate(selectedCategory.Id);

            ClearCategoryForm();
            LoadCategories();
            LoadCategoriesGrid();
            LoadPosCategories();
            LoadPosProducts();

            MessageBox.Show("Category reactivated.");
        }

        private void DeleteSelectedCategoryPermanently_Click(object sender, RoutedEventArgs e)
        {
            if (CategoriesDataGrid.SelectedItem is not Category selectedCategory)
            {
                MessageBox.Show("Select a category first.");
                return;
            }

            var confirm = MessageBox.Show(
                $"Remove category '{selectedCategory.Name}' permanently? It will no longer appear anywhere in the app. (Existing bill history that references it is kept.)",
                "Confirm Delete",
                MessageBoxButton.YesNo);

            if (confirm != MessageBoxResult.Yes)
                return;

            _categoryRepository.MarkDeleted(selectedCategory.Id);

            ClearCategoryForm();
            LoadCategories();
            LoadCategoriesGrid();
            LoadPosCategories();

            MessageBox.Show("Category deleted.");
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

                if (!decimal.TryParse(PriceTextBox.Text?.Trim(), out decimal price) || price < 0)
                {
                    MessageBox.Show("Enter valid price.");
                    return;
                }

                if (_selectedManagementProduct != null)
                {
                    SaveEditedProduct(name, selectedCategory, price);
                    return;
                }

                var existingProduct = _productRepository.GetByName(name);
                if (existingProduct != null)
                {
                    MessageBox.Show(existingProduct.IsActive
                        ? $"A product named '{name}' already exists."
                        : $"A product named '{name}' already exists but is deactivated. Select it and click Reactivate Selected Product instead of creating a new one.");
                    return;
                }

                if (TrackStockCheckBox.IsChecked == true)
                {
                    if (!TryReadTrackedSettings(name, out string mainItem, out string unit, out decimal used))
                        return;

                    if (!TryReadPackSize(out bool setPack, out decimal? packSize))
                        return;

                    _productSetupService.CreateTrackedProduct(
                        name, selectedCategory.Id, price, mainItem, unit, used, setPack, packSize);
                }
                else
                {
                    _productRepository.Add(new Product
                    {
                        Name = name,
                        CategoryId = selectedCategory.Id,
                        SellingPrice = price,
                        IsActive = true
                    });
                }

                ClearProductForm();
                LoadProducts();
                LoadPosProducts();
                LoadMainItemOptions();

                MessageBox.Show("Product saved.");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to save/update product.\n" + ex.Message);
            }
        }

        private void SaveEditedProduct(string name, Category selectedCategory, decimal price)
        {
            var existingProduct = _productRepository.GetByName(name);
            if (existingProduct != null && existingProduct.Id != _selectedManagementProduct!.Id)
            {
                MessageBox.Show(existingProduct.IsActive
                    ? $"A product named '{name}' already exists."
                    : $"A product named '{name}' already exists but is deactivated. Select it and click Reactivate Selected Product instead of creating a new one.");
                return;
            }

            string? mainItem = null;
            string unit = "";
            decimal used = 0;
            bool setPack = false;
            decimal? packSize = null;

            if (TrackStockCheckBox.IsChecked == true)
            {
                if (!TryReadTrackedSettings(name, out string trackedMainItem, out unit, out used))
                    return;

                if (!TryReadPackSize(out setPack, out packSize))
                    return;

                mainItem = trackedMainItem;
            }

            var updatedProduct = new Product
            {
                Id = _selectedManagementProduct.Id,
                Name = name,
                CategoryId = selectedCategory.Id,
                SellingPrice = price,
                IsActive = _selectedManagementProduct.IsActive   // not changed by an edit: use Deactivate / Reactivate
            };

            _productSetupService.UpdateProduct(updatedProduct, mainItem, unit, used, setPack, packSize);

            ClearProductForm();
            LoadProducts();
            LoadPosProducts();
            LoadMainItemOptions();

            MessageBox.Show("Product updated.");
        }

        // Reads Main item / Stock unit / Stock used per sale. A blank main item means "this product itself".
        private bool TryReadTrackedSettings(string productName, out string mainItem, out string unit, out decimal used)
        {
            mainItem = string.IsNullOrWhiteSpace(MainItemComboBox.Text) ? productName : MainItemComboBox.Text.Trim();
            unit = MainItemUnitComboBox.Text?.Trim() ?? "";
            used = 0;

            if (unit.Length == 0)
            {
                MessageBox.Show("Enter the stock unit (for example ml, bottle, unit).");
                return false;
            }

            return TryReadPositiveNumber(StockUsedPerSaleTextBox, "Stock used per sale", out used);
        }

        // Per bottle/pack size of the main item. setPack = false when the field is not in use (not tracked,
        // or counted in bottles): the saved size is then left as it is. Blank = no size.
        private bool TryReadPackSize(out bool setPack, out decimal? size)
        {
            setPack = PackSizeTextBox.IsEnabled;
            size = null;

            if (!setPack || string.IsNullOrWhiteSpace(PackSizeTextBox.Text))
                return true;

            if (!decimal.TryParse(PackSizeTextBox.Text.Trim(), out decimal value) || value <= 0)
            {
                MessageBox.Show("Per bottle/pack size: enter a number greater than zero, or leave it blank.");
                return false;
            }

            size = value;
            return true;
        }

        private static bool TryReadPositiveNumber(TextBox box, string label, out decimal value)
        {
            if (!decimal.TryParse(box.Text?.Trim(), out value) || value <= 0)
            {
                MessageBox.Show($"{label}: enter a number greater than zero.");
                return false;
            }

            return true;
        }

        // -----------------------------
        // Product form: stock (main item) fields
        // -----------------------------

        private Dictionary<string, StockItem> _mainItems = new(StringComparer.OrdinalIgnoreCase);

        // Handlers are attached here (not in XAML) so nothing fires during InitializeComponent.
        private void WireProductSetupControls()
        {
            MainItemUnitComboBox.ItemsSource = new[] { "ml", "bottle", "unit" };

            CategoriesDataGrid.SelectionChanged += (_, _) =>
            {
                if (CategoriesDataGrid.SelectedItem is Category selected)
                    LoadSelectedCategoryIntoForm(selected);
            };
            CategoriesDataGrid.AutoGeneratingColumn += (_, e) =>
            {
                if (e.PropertyName == nameof(Category.IsKitchenItem))
                    e.Column.Header = "Kitchen Category";
                else if (e.PropertyName == nameof(Category.ButtonColor))
                    e.Column.Header = "POS Button Colour";
            };

            CategoryColorComboBox.ItemsSource = PosColors.Choices;
            CategoryColorComboBox.SelectedItem = PosColors.Automatic;
            CategoryColorComboBox.SelectionChanged += (_, _) => UpdateCategoryColorSwatch();
            CategoryKitchenCheckBox.Checked += (_, _) => UpdateCategoryColorSwatch();
            CategoryKitchenCheckBox.Unchecked += (_, _) => UpdateCategoryColorSwatch();
            UpdateCategoryColorSwatch();

            TrackStockCheckBox.Checked += (_, _) => UpdateTrackStockEnabled();
            TrackStockCheckBox.Unchecked += (_, _) => UpdateTrackStockEnabled();
            MainItemComboBox.AddHandler(
                System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
                new TextChangedEventHandler((_, _) => UpdateMainItemUnit()));
            MainItemUnitComboBox.AddHandler(
                System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
                new TextChangedEventHandler((_, _) => UpdatePackField(CurrentExistingMainItem())));

            LoadMainItemOptions();
            ResetStockSetupFields();
        }

        private void ResetStockSetupFields()
        {
            TrackStockCheckBox.IsChecked = false;
            MainItemComboBox.Text = "";
            MainItemUnitComboBox.Text = "unit";
            StockUsedPerSaleTextBox.Text = "1";
            PackSizeTextBox.Text = "";
            _packFilledFor = 0;

            UpdateTrackStockEnabled();
        }

        // The list of existing main items (for picking the one a sub product shares).
        private void LoadMainItemOptions()
        {
            var items = _stockItemRepository.GetActive();

            _mainItems = new Dictionary<string, StockItem>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items)
                _mainItems.TryAdd(item.Name, item);

            string text = MainItemComboBox.Text;
            MainItemComboBox.ItemsSource = null;
            MainItemComboBox.ItemsSource = items.Select(i => i.Name).ToList();
            MainItemComboBox.Text = text;
        }

        // An existing main item fixes the unit (shown, not editable); a new name lets you choose it.
        private void UpdateMainItemUnit()
        {
            if (MainItemComboBox == null || MainItemUnitComboBox == null || TrackStockCheckBox == null)
                return;

            var existing = CurrentExistingMainItem();

            if (existing != null)
            {
                MainItemUnitComboBox.Text = existing.TrackingUnit;
                MainItemUnitComboBox.IsEnabled = false;
            }
            else
            {
                MainItemUnitComboBox.IsEnabled = TrackStockCheckBox.IsChecked == true;
            }

            UpdatePackField(existing);
        }

        private StockItem? CurrentExistingMainItem()
        {
            var typed = MainItemComboBox?.Text?.Trim() ?? "";
            return _mainItems.TryGetValue(typed, out var existing) ? existing : null;
        }

        // Which existing main item the size box was last filled from (0 = none), so typing a size is never overwritten.
        private int _packFilledFor;

        // The size box is only in use for a tracked item that is not counted in bottles. Choosing an existing
        // main item shows its saved size; going back to a new name clears it.
        private void UpdatePackField(StockItem? existing)
        {
            if (PackSizeTextBox == null || MainItemUnitComboBox == null || TrackStockCheckBox == null)
                return;

            string unit = MainItemUnitComboBox.Text?.Trim() ?? "";
            bool allowed = TrackStockCheckBox.IsChecked == true && unit.Length > 0 && !PackFormatter.IsCountedInBottles(unit);

            PackSizeTextBox.IsEnabled = allowed;
            PackUnitTextBlock.Text = allowed ? unit : "";

            if (existing != null)
            {
                if (_packFilledFor != existing.Id)
                {
                    PackSizeTextBox.Text = existing.PackSize?.ToString("0.##") ?? "";
                    _packFilledFor = existing.Id;
                }
            }
            else if (_packFilledFor != 0)
            {
                PackSizeTextBox.Text = "";
                _packFilledFor = 0;
            }

            if (!allowed)
                PackSizeTextBox.Text = "";
        }

        private void UpdateTrackStockEnabled()
        {
            if (MainItemComboBox == null)
                return;

            bool on = TrackStockCheckBox.IsChecked == true;

            MainItemComboBox.IsEnabled = on;
            StockUsedPerSaleTextBox.IsEnabled = on;

            if (on && string.IsNullOrWhiteSpace(MainItemComboBox.Text))
                MainItemComboBox.Text = ProductNameTextBox.Text?.Trim() ?? "";

            UpdateMainItemUnit();
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

        private void ReactivateSelectedProduct_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ProductsDataGrid.SelectedItem is not ProductDisplay selectedProduct)
                {
                    MessageBox.Show("Select a product first.");
                    return;
                }

                _productRepository.Reactivate(selectedProduct.Id);

                ClearProductForm();
                LoadProducts();
                LoadPosProducts();

                MessageBox.Show("Product reactivated.");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to reactivate product.\n" + ex.Message);
            }
        }

        private void DeleteSelectedProductPermanently_Click(object sender, RoutedEventArgs e)
        {
            if (ProductsDataGrid.SelectedItem is not ProductDisplay selectedProduct)
            {
                MessageBox.Show("Select a product first.");
                return;
            }

            var confirm = MessageBox.Show(
                $"Remove product '{selectedProduct.Name}' permanently? It will no longer appear anywhere in the app. (Existing bill history that references it is kept.)",
                "Confirm Delete",
                MessageBoxButton.YesNo);

            if (confirm != MessageBoxResult.Yes)
                return;

            _productRepository.MarkDeleted(selectedProduct.Id);

            ClearProductForm();
            LoadProducts();
            LoadPosProducts();

            MessageBox.Show("Product deleted.");
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

        private void ReactivateSelectedTable_Click(object sender, RoutedEventArgs e)
        {
            if (TablesDataGrid.SelectedItem is not DiningTable selectedTable)
            {
                MessageBox.Show("Select a table first.");
                return;
            }

            _diningTableRepository.Reactivate(selectedTable.Id);

            LoadTables();
            LoadPosTables();

            MessageBox.Show("Table reactivated.");
        }

        private void DeleteSelectedTablePermanently_Click(object sender, RoutedEventArgs e)
        {
            if (TablesDataGrid.SelectedItem is not DiningTable selectedTable)
            {
                MessageBox.Show("Select a table first.");
                return;
            }

            var confirm = MessageBox.Show(
                $"Remove table '{selectedTable.TableName}' permanently? It will no longer appear anywhere in the app. (Existing bill history that references it is kept.)",
                "Confirm Delete",
                MessageBoxButton.YesNo);

            if (confirm != MessageBoxResult.Yes)
                return;

            _diningTableRepository.MarkDeleted(selectedTable.Id);

            LoadTables();
            LoadPosTables();

            MessageBox.Show("Table deleted.");
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

                var outstanding = _paymentRepository.GetOutstandingSummary();
                PartiallyPaidSummaryTextBlock.Text = $"{outstanding.PartiallyPaidBills} bill(s) | Due {outstanding.TotalDue:F2}";

                ItemSalesReportDataGrid.ItemsSource = null;
                ItemSalesReportDataGrid.ItemsSource = _billItemRepository.GetItemSalesReportBySingleDate(reportDate);

                PaymentBreakdownDataGrid.ItemsSource = null;
                PaymentBreakdownDataGrid.ItemsSource = _paymentRepository.GetPaymentBreakdownBySingleDate(reportDate);

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

                var billItems = _billItemRepository.GetActiveByBillIdForDisplay(billId);

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

                NetSalesTextBlock.Text = net.ToString("F2");
                NetSalesTextBlock.Foreground = net < 0
                    ? new SolidColorBrush(Color.FromRgb(0xB9, 0x1C, 0x1C))
                    : new SolidColorBrush(Color.FromRgb(0x16, 0x65, 0x34));
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
                    MessageBox.Show("Enter valid amount.");
                    return;
                }

                var exp = new Expense
                {
                    ExpenseDate = reportDate,
                    Description = description,
                    Amount = amount,
                    CreatedAt = DateTime.Now
                };

                // 🔥 THIS IS THE NEW LOGIC
                if (_editingExpenseId.HasValue)
                {
                    exp.Id = _editingExpenseId.Value;
                    _expenseRepository.Update(exp);
                    _editingExpenseId = null;
                }
                else
                {
                    _expenseRepository.Add(exp);
                }

                ExpenseDescriptionTextBox.Clear();
                ExpenseAmountTextBox.Clear();

                LoadReports();
                RunBackup(false);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error: " + ex.Message);
            }
        }
        private void ClearProductForm()
        {
            try
            {
                _selectedManagementProduct = null;

                ProductNameTextBox.Clear();
                PriceTextBox.Clear();
                CategoryComboBox.SelectedIndex = -1;

                ResetStockSetupFields();

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

                var categories = _categoryRepository.GetActiveCategories();
                var matchingCategory = categories.FirstOrDefault(c => c.Name == selectedProduct.CategoryName);

                CategoryComboBox.ItemsSource = null;
                CategoryComboBox.ItemsSource = categories;
                CategoryComboBox.SelectedItem = matchingCategory;

                LoadMainItemOptions();
                _packFilledFor = 0;

                bool tracked = selectedProduct.StockItemId.HasValue;
                TrackStockCheckBox.IsChecked = tracked;
                MainItemComboBox.Text = tracked ? selectedProduct.MainItem : "";
                StockUsedPerSaleTextBox.Text = tracked && selectedProduct.UnitsPerSale.HasValue
                    ? selectedProduct.UnitsPerSale.Value.ToString("0.##")
                    : "1";
                UpdateTrackStockEnabled();

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
                        FontSize = 18,
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
                        Padding = new Thickness(18),
                        Margin = new Thickness(0, 0, 0, 14),
                        Cursor = System.Windows.Input.Cursors.Hand
                    };

                    var stack = new StackPanel();

                    stack.Children.Add(new TextBlock
                    {
                        Text = bill.CardTitle,
                        FontSize = 22,
                        FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42))
                    });

                    stack.Children.Add(new TextBlock
                    {
                        Text = bill.CardSubTitle,
                        FontSize = 15,
                        Margin = new Thickness(0, 4, 0, 0),
                        Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105))
                    });

                    stack.Children.Add(new TextBlock
                    {
                        Text = $"Opened: {bill.CreatedAt}",
                        FontSize = 14,
                        Margin = new Thickness(0, 8, 0, 0),
                        Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139))
                    });

                    stack.Children.Add(new TextBlock
                    {
                        Text = $"Status: {bill.Status}",
                        FontSize = 14,
                        Margin = new Thickness(0, 2, 0, 0),
                        Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139))
                    });

                    stack.Children.Add(new TextBlock
                    {
                        Text = $"Total: {bill.TotalAmount:F2}",
                        FontSize = 18,
                        FontWeight = FontWeights.Bold,
                        Margin = new Thickness(0, 10, 0, 0),
                        Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42))
                    });

                    if (bill.IsPartiallyPaid)
                    {
                        stack.Children.Add(new TextBlock
                        {
                            Text = $"Paid: {bill.PaidAmount:F2}    Due: {bill.DueAmount:F2}",
                            FontSize = 15,
                            FontWeight = FontWeights.SemiBold,
                            Margin = new Thickness(0, 4, 0, 0),
                            Foreground = new SolidColorBrush(Color.FromRgb(194, 65, 12))
                        });

                        stack.Children.Add(new TextBlock
                        {
                            Text = "PARTIALLY PAID",
                            FontSize = 13,
                            FontWeight = FontWeights.Bold,
                            Margin = new Thickness(0, 2, 0, 0),
                            Foreground = new SolidColorBrush(Color.FromRgb(194, 65, 12))
                        });
                    }

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

                decimal paid = _paymentRepository.GetPaidTotalForBill(selectedBill.Id);
                if (paid > 0)
                {
                    SelectedBillPaidDueTextBlock.Text = $"Paid: {paid:F2}    Due: {Math.Max(0, total - paid):F2}    (Partially Paid)";
                    SelectedBillPaidDueTextBlock.Visibility = Visibility.Visible;
                }
                else
                {
                    SelectedBillPaidDueTextBlock.Text = "";
                    SelectedBillPaidDueTextBlock.Visibility = Visibility.Collapsed;
                }

                var openBills = _billRepository.GetOpenBillsForDisplay();
                RenderOpenBillCards(openBills);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to select bill card.\n" + ex.Message);
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
                    SelectedBillPaidDueTextBlock.Visibility = Visibility.Collapsed;
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

        private ReceiptData BuildReceiptData(int billId, string billTypeText, string tableName)
        {
            try
            {
                if (billId <= 0)
                    throw new Exception("Invalid bill id.");

                var items = _billItemRepository.GetReceiptLinesByBillId(billId);
                decimal total = items.Sum(x => x.LineTotal);

                // Cash/card are cumulative over every payment made on this bill so far.
                var (cash, card) = _paymentRepository.GetPaidByMethodForBill(billId);
                decimal due = Math.Max(0, total - cash - card);
                if (due <= 0.01m)
                    due = 0;

                return new ReceiptData
                {
                    BusinessName = "Gardenia Restaurant",
                    BillNo = _billRepository.GetVisibleBillNumber(billId),
                    TableName = string.IsNullOrWhiteSpace(tableName) ? "Quick Sale" : tableName,
                    BillType = billTypeText,
                    PrintedAt = DateTime.Now,
                    Total = total,
                    CashAmount = cash,
                    CardAmount = card,
                    DueAmount = due,
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
                KeyboardEnabledCheckBox.IsChecked = _appSettings.UseOnScreenKeyboard;

                BackupFolderTextBox.Text = _appSettings.BackupFolderPath ?? "";
                BackupIntervalTextBox.Text = _appSettings.BackupIntervalMinutes.ToString();

                if (!string.IsNullOrWhiteSpace(_appSettings.BackupFolderPath))
                    BackupStatusTextBlock.Text = $"Backup folder: {_appSettings.BackupFolderPath}";
                else
                    BackupStatusTextBlock.Text = "Backup folder not selected.";

                DailyReportFolderTextBox.Text = _appSettings.DailyReportFolderPath ?? "";
                DailyReportTimeTextBox.Text = string.IsNullOrWhiteSpace(_appSettings.DailyReportTime)
                    ? "23:00"
                    : _appSettings.DailyReportTime;

                if (!string.IsNullOrWhiteSpace(_appSettings.DailyReportFolderPath))
                    DailyReportStatusTextBlock.Text = $"Daily report folder: {_appSettings.DailyReportFolderPath}";
                else
                    DailyReportStatusTextBlock.Text = "Daily report folder not selected.";
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

                KitchenPrinterComboBox.ItemsSource = null;
                KitchenPrinterComboBox.ItemsSource = new[] { SameAsReceiptPrinter }.Concat(printerNames).ToList();
                KitchenPrinterComboBox.SelectedItem =
                    !string.IsNullOrWhiteSpace(_appSettings.KitchenPrinterName) && printerNames.Contains(_appSettings.KitchenPrinterName)
                        ? _appSettings.KitchenPrinterName
                        : SameAsReceiptPrinter;

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
        private void ChooseDailyReportFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFolderDialog
                {
                    Title = "Choose the folder for the daily report files",
                    InitialDirectory = System.IO.Directory.Exists(DailyReportFolderTextBox.Text)
                        ? DailyReportFolderTextBox.Text
                        : ""
                };

                if (dialog.ShowDialog(this) != true)
                    return;

                DailyReportFolderTextBox.Text = dialog.FolderName;
                DailyReportStatusTextBlock.Text = "Folder chosen. Click Save Report Settings to use it.";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to choose daily report folder.\n" + ex.Message);
            }
        }

        private void SaveDailyReportSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string folder = DailyReportFolderTextBox.Text?.Trim() ?? "";
                string reportTime = DailyReportTimeTextBox.Text?.Trim() ?? "";

                if (string.IsNullOrWhiteSpace(folder))
                {
                    MessageBox.Show("Select daily report folder.");
                    return;
                }

                if (!TimeSpan.TryParse(reportTime, out _))
                {
                    MessageBox.Show("Enter valid report time. Example: 23:00");
                    return;
                }

                _appSettings.DailyReportFolderPath = folder;
                _appSettings.DailyReportTime = reportTime;

                _settingsService.Save(_appSettings);

                DailyReportStatusTextBlock.Text = $"Daily report saved. Folder: {folder} | Time: {reportTime}";

                StartDailyReportTimer();

                MessageBox.Show("Daily report settings saved.");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to save daily report settings.\n" + ex.Message);
            }
        }

        // Reports page: the PDF for the selected date. Goes to the daily report folder when one is set,
        // otherwise asks where to save it. It is opened afterwards so it can be checked or printed.
        private void SaveReportPdf_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                DateTime date = ReportDatePicker.SelectedDate ?? DateTime.Today;
                string filePath;

                string folder = _appSettings?.DailyReportFolderPath ?? "";
                if (!string.IsNullOrWhiteSpace(folder))
                {
                    filePath = _dailyReportService.GenerateDailyReport(folder, date);
                }
                else
                {
                    var dialog = new Microsoft.Win32.SaveFileDialog
                    {
                        Title = "Save daily report",
                        Filter = "PDF file (*.pdf)|*.pdf",
                        FileName = DailyReportService.DefaultFileName(date)
                    };

                    if (dialog.ShowDialog() != true)
                        return;

                    filePath = dialog.FileName;
                    _dailyReportService.SaveReport(filePath, date);
                }

                DailyReportStatusTextBlock.Text = $"Daily report saved: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n{filePath}";

                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(filePath) { UseShellExecute = true });
                }
                catch
                {
                    MessageBox.Show("The PDF was saved here:\n" + filePath);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not save the PDF.\n" + ex.Message);
            }
        }

        private void GenerateTodayReport_Click(object sender, RoutedEventArgs e)
        {
            GenerateDailyReportForDate(DateTime.Today, showMessage: true);
        }

        private void StartDailyReportTimer()
        {
            try
            {
                _dailyReportTimer?.Stop();

                if (_appSettings == null || string.IsNullOrWhiteSpace(_appSettings.DailyReportFolderPath))
                {
                    DailyReportStatusTextBlock.Text = "Daily report auto generation not started. Folder not selected.";
                    return;
                }

                _dailyReportTimer = new DispatcherTimer();
                _dailyReportTimer.Interval = TimeSpan.FromMinutes(1);
                _dailyReportTimer.Tick += (s, e) => CheckDailyReportSchedule();
                _dailyReportTimer.Start();

                DailyReportStatusTextBlock.Text =
                    $"Daily report auto generation running. Time: {_appSettings.DailyReportTime}";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to start daily report timer.\n" + ex.Message);
            }
        }

        private void CheckDailyReportSchedule()
        {
            try
            {
                if (_appSettings == null)
                    return;

                if (string.IsNullOrWhiteSpace(_appSettings.DailyReportFolderPath))
                    return;

                string reportTimeText = string.IsNullOrWhiteSpace(_appSettings.DailyReportTime)
                    ? "23:00"
                    : _appSettings.DailyReportTime;

                if (!TimeSpan.TryParse(reportTimeText, out TimeSpan reportTime))
                    return;

                DateTime now = DateTime.Now;

                if (now.TimeOfDay.Hours == reportTime.Hours &&
                    now.TimeOfDay.Minutes == reportTime.Minutes)
                {
                    if (_lastDailyReportGeneratedDate.HasValue &&
                        _lastDailyReportGeneratedDate.Value.Date == now.Date)
                    {
                        return;
                    }

                    GenerateDailyReportForDate(now.Date, showMessage: false);
                    _lastDailyReportGeneratedDate = now.Date;
                }
            }
            catch (Exception ex)
            {
                DailyReportStatusTextBlock.Text = "Daily report schedule failed: " + ex.Message;
            }
        }

        private void GenerateMissingYesterdayReportIfNeeded()
        {
            try
            {
                if (_appSettings == null || string.IsNullOrWhiteSpace(_appSettings.DailyReportFolderPath))
                    return;

                DateTime yesterday = DateTime.Today.AddDays(-1);

                bool exists = _dailyReportService.ReportExists(_appSettings.DailyReportFolderPath, yesterday);

                if (!exists)
                {
                    string filePath = _dailyReportService.GenerateDailyReport(_appSettings.DailyReportFolderPath, yesterday);
                    DailyReportStatusTextBlock.Text = $"Missing yesterday report generated:\n{filePath}";
                }
            }
            catch
            {
                // do not block app startup
            }
        }

        private void GenerateDailyReportForDate(DateTime date, bool showMessage)
        {
            try
            {
                if (_appSettings == null || string.IsNullOrWhiteSpace(_appSettings.DailyReportFolderPath))
                {
                    if (showMessage)
                        MessageBox.Show("Daily report folder is not selected.");
                    return;
                }

                string filePath = _dailyReportService.GenerateDailyReport(_appSettings.DailyReportFolderPath, date);

                DailyReportStatusTextBlock.Text =
                    $"Daily report generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n{filePath}";

                if (showMessage)
                    MessageBox.Show("Daily report generated successfully.");
            }
            catch (Exception ex)
            {
                DailyReportStatusTextBlock.Text = "Daily report failed: " + ex.Message;

                if (showMessage)
                    MessageBox.Show("Daily report failed.\n" + ex.Message);
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
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Choose the backup folder on your external drive",
                InitialDirectory = System.IO.Directory.Exists(BackupFolderTextBox.Text)
                    ? BackupFolderTextBox.Text
                    : ""
            };

            if (dialog.ShowDialog(this) != true)
                return;

            BackupFolderTextBox.Text = dialog.FolderName;

            var risk = BackupService.DescribeFolderRisk(dialog.FolderName, DatabaseHelper.GetDatabasePath());
            BackupStatusTextBlock.Text = risk == null
                ? "Folder chosen. Click Save Backup Settings to use it."
                : "Folder chosen. Note: " + risk;
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
                RefreshBackupBanner();

                var risk = BackupService.DescribeFolderRisk(folder, DatabaseHelper.GetDatabasePath());
                MessageBox.Show(risk == null
                    ? "Backup settings saved."
                    : "Backup settings saved.\n\nNote: " + risk);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to save backup settings.\n" + ex.Message);
            }
        }

        private void BackupNow_Click(object sender, RoutedEventArgs e)
        {
            RunBackup(showMessage: true, force: true);
        }

        private void StartAutoBackupTimer()
        {
            try
            {
                _backupTimer?.Stop();

                if (_appSettings == null)
                    return;

                int minutes = _appSettings.BackupIntervalMinutes <= 0
                    ? 15
                    : _appSettings.BackupIntervalMinutes;

                _backupTimer = new DispatcherTimer();
                _backupTimer.Interval = TimeSpan.FromMinutes(minutes);
                _backupTimer.Tick += (s, e) => RunBackup(showMessage: false, force: true);
                _backupTimer.Start();

                BackupStatusTextBlock.Text = $"Auto backup running every {minutes} minutes.";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to start auto backup.\n" + ex.Message);
            }
        }

        private DateTime _lastBackupAt = DateTime.MinValue;
        private string? _lastBackupError;

        // Copies made after a sale / cancel / void are skipped if one was made in the last minute
        // (the 15-minute timer and "Backup Now" always run). Keeps the number of copies sensible.
        private void RunBackup(bool showMessage, bool force = false)
        {
            try
            {
                if (!force && (DateTime.Now - _lastBackupAt) < TimeSpan.FromSeconds(60))
                    return;

                var result = _backupService.BackupNow(_appSettings?.BackupFolderPath);

                _lastBackupAt = DateTime.Now;
                _lastBackupError = null;

                BackupStatusTextBlock.Text = $"Last backup: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n{result.Path}"
                    + (result.Warning == null ? "" : "\n" + result.Warning);
                RefreshBackupBanner();

                if (showMessage)
                    MessageBox.Show(result.Warning == null
                        ? "Backup completed successfully."
                        : "Backup completed.\n\n" + result.Warning);
            }
            catch (Exception ex)
            {
                _lastBackupError = ex.Message;
                BackupStatusTextBlock.Text = "Backup failed: " + ex.Message;
                RefreshBackupBanner();

                if (showMessage)
                    MessageBox.Show("Backup failed.\n" + ex.Message);
            }
        }

        // The orange bar at the top: visible whenever backups are not properly protected.
        private void RefreshBackupBanner()
        {
            string? message = _lastBackupError != null
                ? "Backup FAILED: " + _lastBackupError
                : BackupService.DescribeFolderRisk(_appSettings?.BackupFolderPath, DatabaseHelper.GetDatabasePath());

            BackupWarningTextBlock.Text = message == null ? "" : "⚠ " + message + "   (tap to open Settings)";
            BackupWarningBanner.Visibility = message == null ? Visibility.Collapsed : Visibility.Visible;
        }

        private void BackupWarningBanner_Tapped(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            MainTabControl.SelectedItem = SettingsTabItem;
        }

        public void Delete(int id)
        {
            using var connection = DatabaseHelper.GetConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM Expenses WHERE Id = @id";
            command.Parameters.AddWithValue("@id", id);

            command.ExecuteNonQuery();
        }

     
        private void DeleteExpense_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ExpensesDataGrid.SelectedItem is not Expense exp)
                {
                    MessageBox.Show("Select expense.");
                    return;
                }

                var confirm = MessageBox.Show(
                    "Delete this expense?",
                    "Confirm",
                    MessageBoxButton.YesNo);

                if (confirm != MessageBoxResult.Yes)
                    return;

                _expenseRepository.Delete(exp.Id);

                LoadReports();
                RunBackup(false);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void EditExpense_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ExpensesDataGrid.SelectedItem is not Expense exp)
                {
                    MessageBox.Show("Select expense.");
                    return;
                }

                ExpenseDescriptionTextBox.Text = exp.Description;
                ExpenseAmountTextBox.Text = exp.Amount.ToString();

                _editingExpenseId = exp.Id;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void CancelCartItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (CartDataGrid.SelectedItem is not PosCartLine item)
                {
                    MessageBox.Show("Select item.");
                    return;
                }

                var confirm = MessageBox.Show(
                    "Cancel selected item?",
                    "Confirm",
                    MessageBoxButton.YesNo);

                if (confirm != MessageBoxResult.Yes)
                    return;

                if (item.IsExistingItem && item.BillItemId.HasValue)
                {
                    _billItemRepository.CancelItem(item.BillItemId.Value);

                    if (_currentTargetBillId.HasValue)
                        LoadBillIntoCart(_currentTargetBillId.Value);

                    RefreshAllOpenBillViews();
                }
                else
                {
                    _cart.Remove(item);
                    RefreshCartView();
                }

                RunBackup(false);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to cancel item.\n" + ex.Message);
            }
        }

        private void VoidBill_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_selectedTablesBill == null)
                {
                    MessageBox.Show("Select bill.");
                    return;
                }

                var confirm = MessageBox.Show(
                    "Void this bill?",
                    "Confirm",
                    MessageBoxButton.YesNo);

                if (confirm != MessageBoxResult.Yes)
                    return;

                _billRepository.VoidBill(_selectedTablesBill.Id);

                RefreshAllOpenBillViews();
                RunBackup(false);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }


        private void SearchBillHistoryByDate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var selectedDate = BillHistoryDatePicker.SelectedDate ?? DateTime.Today;
                string dateText = selectedDate.ToString("yyyy-MM-dd");

                var bills = _billRepository.GetBillHistoryByDate(dateText);

                BillHistoryDataGrid.ItemsSource = null;
                BillHistoryDataGrid.ItemsSource = bills;

                BillHistoryItemsDataGrid.ItemsSource = null;
                BillHistorySelectedTitleTextBlock.Text = "Select a bill";
                _selectedHistoryBill = null;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to search bill history by date.\n" + ex.Message);
            }
        }

        private void SearchBillHistoryByNumber_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string searchText = BillHistorySearchTextBox.Text?.Trim() ?? "";

                if (string.IsNullOrWhiteSpace(searchText))
                {
                    MessageBox.Show("Enter bill number or internal bill id.");
                    return;
                }

                var bills = _billRepository.SearchBillHistoryByVisibleBillNumber(searchText);

                BillHistoryDataGrid.ItemsSource = null;
                BillHistoryDataGrid.ItemsSource = bills;

                BillHistoryItemsDataGrid.ItemsSource = null;
                BillHistorySelectedTitleTextBlock.Text = "Select a bill";
                _selectedHistoryBill = null;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to search bill history by number.\n" + ex.Message);
            }
        }

        private void BillHistoryDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (BillHistoryDataGrid.SelectedItem is not BillHistoryDisplay selectedBill)
                    return;

                _selectedHistoryBill = selectedBill;

                BillHistorySelectedTitleTextBlock.Text =
                    $"Bill No: {selectedBill.VisibleBillNumber} | {selectedBill.Status}";

                BillHistoryItemsDataGrid.ItemsSource = null;
                BillHistoryItemsDataGrid.ItemsSource = _billItemRepository.GetByBillIdForDisplay(selectedBill.Id);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to load bill history items.\n" + ex.Message);
            }
        }

        private void ReprintSelectedBillReceipt_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_selectedHistoryBill == null)
                {
                    MessageBox.Show("Select a bill first.");
                    return;
                }

                if (_selectedHistoryBill.Status == "VOID")
                {
                    var confirm = MessageBox.Show(
                        "This bill is VOID. Reprint anyway?",
                        "Confirm Reprint",
                        MessageBoxButton.YesNo);

                    if (confirm != MessageBoxResult.Yes)
                        return;
                }

                var receipt = BuildReceiptData(
                    _selectedHistoryBill.Id,
                    _selectedHistoryBill.BillType,
                    _selectedHistoryBill.TableName);

                _receiptPrintService.PrintReceipt(receipt, _appSettings.ReceiptPrinterName);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to reprint receipt.\n" + ex.Message);
            }
        }

    }
}
