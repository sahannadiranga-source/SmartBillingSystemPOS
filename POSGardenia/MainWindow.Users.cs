using POSGardenia.Controls;
using POSGardenia.Models;
using POSGardenia.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace POSGardenia
{
    // Signing in, what each person can open, and (for administrators) the Users tab.
    public partial class MainWindow
    {
        private readonly UserService _userService = new();
        private AppUser? _currentUser;
        private AppUser? _editingUser;          // the account loaded into the Users form (null = adding a new one)
        private bool _setupMode;                // no accounts yet: the first screen makes the administrator
        private readonly Dictionary<string, CheckBox> _permissionBoxes = new();

        // Handlers are attached here (not in XAML) so nothing fires during InitializeComponent.
        private void InitAccounts()
        {
            // the version is in the window title and on the sign-in card
            Title = $"POSGardenia {AppInfo.Version}";
            LoginVersionTextBlock.Text = AppInfo.VersionText;

            BuildPermissionBoxes();

            SizeChanged += (_, _) => UpdateUserBadgeLayout();

            LoginButton.Click += LoginButton_Click;
            LogoutButton.Click += LogoutButton_Click;

            UserIsAdminCheckBox.Checked += (_, _) => UpdateAdminTick();
            UserIsAdminCheckBox.Unchecked += (_, _) => UpdateAdminTick();
            SaveUserButton.Click += SaveUser_Click;
            ClearUserButton.Click += (_, _) => ClearUserForm();
            DeactivateUserButton.Click += (_, _) => SetSelectedUserActive(false);
            ReactivateUserButton.Click += (_, _) => SetSelectedUserActive(true);

            UsersDataGrid.AutoGeneratingColumn += UsersGrid_AutoGeneratingColumn;
            UsersDataGrid.SelectionChanged += (_, _) =>
            {
                if (UsersDataGrid.SelectedItem is UserDisplay row)
                    LoadUserIntoForm(row.Id);
            };

            ClearUserForm();
            ShowLogin();
        }

        // -----------------------------
        // Sign in / out
        // -----------------------------

        private void ShowLogin()
        {
            _currentUser = null;
            _setupMode = !_userService.HasUsers();

            LoginTitleTextBlock.Text = _setupMode ? "Create the administrator account" : "Sign in";
            LoginFieldsPanel.Visibility = _setupMode ? Visibility.Collapsed : Visibility.Visible;
            SetupFieldsPanel.Visibility = _setupMode ? Visibility.Visible : Visibility.Collapsed;
            LoginButton.Content = _setupMode ? "Create Account" : "Sign In";
            LoginErrorTextBlock.Text = "";

            LoginUsernameTextBox.Clear();
            LoginPasswordBox.Clear();
            SetupFullNameTextBox.Clear();
            SetupUsernameTextBox.Clear();
            SetupPasswordBox.Clear();
            SetupPasswordConfirmBox.Clear();

            UserChip.Visibility = Visibility.Collapsed;
            MainTabControl.IsEnabled = false;
            LoginOverlay.Visibility = Visibility.Visible;
            HideOnScreenKeyboard(clearFocus: false);

            Dispatcher.BeginInvoke(new Action(() =>
                (_setupMode ? SetupFullNameTextBox : LoginUsernameTextBox).Focus()), DispatcherPriority.Background);
        }

        private void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                LoginErrorTextBlock.Text = "";
                AppUser? user;
                string error;

                if (_setupMode)
                {
                    if (SetupPasswordBox.Password != SetupPasswordConfirmBox.Password)
                    {
                        LoginErrorTextBlock.Text = "The two passwords are not the same.";
                        return;
                    }

                    _userService.CreateFirstAdmin(SetupFullNameTextBox.Text, SetupUsernameTextBox.Text, SetupPasswordBox.Password);
                    (user, error) = _userService.Authenticate(SetupUsernameTextBox.Text, SetupPasswordBox.Password);
                }
                else
                {
                    (user, error) = _userService.Authenticate(LoginUsernameTextBox.Text, LoginPasswordBox.Password);
                }

                if (user == null)
                {
                    LoginErrorTextBlock.Text = error;
                    LoginPasswordBox.Clear();
                    return;
                }

                SignedIn(user);
            }
            catch (Exception ex)
            {
                LoginErrorTextBlock.Text = ex.Message;
            }
        }

        private void SignedIn(AppUser user)
        {
            _currentUser = user;

            LoginOverlay.Visibility = Visibility.Collapsed;
            MainTabControl.IsEnabled = true;
            ShowUserBadge(user);
            HideOnScreenKeyboard(clearFocus: false);

            ApplyAccess(user, keepTab: false);

            if (user.CanManageUsers)
                RefreshUsers();
        }

        private void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
            if (_cart.Count > 0 &&
                AppMessage.Show("The cart still has items. Log out anyway?", "Log out", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                return;

            ShowLogin();
        }

        // The signed-in badge: a coloured circle with their initials, a greeting, their name and Admin / Staff.
        private void ShowUserBadge(AppUser user)
        {
            UserNameTextBlock.Text = user.FullName;
            UserInitialsTextBlock.Text = UserBadge.Initials(user.FullName);
            UserAvatar.Background = new System.Windows.Media.SolidColorBrush(UserBadge.AvatarColor(user.Id));
            UserGreetingTextBlock.Text = UserBadge.Greeting(DateTime.Now);

            UserRoleTextBlock.Text = user.IsAdmin ? "Admin" : "Staff";
            UserRolePill.Background = new System.Windows.Media.SolidColorBrush(
                user.IsAdmin ? System.Windows.Media.Color.FromRgb(0xFF, 0xED, 0xD5) : System.Windows.Media.Color.FromRgb(0xDB, 0xEA, 0xFE));
            UserRoleTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(
                user.IsAdmin ? System.Windows.Media.Color.FromRgb(0xC2, 0x41, 0x0C) : System.Windows.Media.Color.FromRgb(0x1D, 0x4E, 0xD8));

            UserChip.ToolTip = $"{user.FullName} ({user.Username})\n{user.AccessSummary}";
            UserChip.Visibility = Visibility.Visible;
            UpdateUserBadgeLayout();
        }

        // A wide window shows the whole name and the Admin / Staff pill; a narrow one trims both so the badge
        // never covers the tabs.
        private void UpdateUserBadgeLayout()
        {
            bool wide = ActualWidth >= 1300;
            UserRolePill.Visibility = wide ? Visibility.Visible : Visibility.Collapsed;
            UserNameTextBlock.MaxWidth = wide ? 280 : 170;
        }

        // Shows only the tabs this person may open. keepTab = stay where they are if that tab is still allowed.
        private void ApplyAccess(AppUser user, bool keepTab)
        {
            void Set(TabItem tab, string key) =>
                tab.Visibility = AccessMap.CanSee(user, key) ? Visibility.Visible : Visibility.Collapsed;

            Set(PosTab, AccessMap.Pos);
            Set(TablesTab, AccessMap.Tables);
            Set(InventoryTabItem, AccessMap.Inventory);
            Set(DailyStockSubTab, AccessMap.DailyStock);
            Set(StockItemsSubTab, AccessMap.StockItems);
            Set(HistoryTab, AccessMap.History);
            Set(ReportsTab, AccessMap.Reports);
            Set(ManagementTab, AccessMap.Management);
            Set(CategoriesSubTab, AccessMap.Categories);
            Set(ProductsSubTab, AccessMap.Products);
            Set(TableMasterSubTab, AccessMap.TableMaster);
            Set(UsersSubTab, AccessMap.Users);
            Set(SettingsTabItem, AccessMap.Settings);

            // the on-screen keyboard switch is a device setting
            KeyboardEnabledCheckBox.IsEnabled = user.Can(AppPermissions.SettingsDevices);

            // without "load report of another date" the Reports page stays on today
            bool anyDate = user.Can(AppPermissions.ReportsLoad);
            ReportDatePicker.IsEnabled = anyDate;
            if (!anyDate)
            {
                ReportDatePicker.SelectedDate = DateTime.Today;
                LoadReports();
            }

            foreach (var tabs in new[] { MainTabControl, InventorySubTabControl, ManagementSubTabControl })
            {
                if (keepTab && tabs.SelectedItem is TabItem current && current.Visibility == Visibility.Visible)
                    continue;

                tabs.SelectedItem = tabs.Items.OfType<TabItem>().FirstOrDefault(t => t.Visibility == Visibility.Visible);
            }
        }

        // Before an action (or a jump to a page): true when this person may. Otherwise says what was refused.
        private bool RequireAccess(string permission, string action)
        {
            if (_currentUser?.Can(permission) == true)
                return true;

            AppMessage.Show($"You are not allowed to {action}. Ask an administrator.", "No access", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        // -----------------------------
        // Users tab (administrators)
        // -----------------------------

        private bool _changingTicks;

        // One box per page / part, and under it the actions inside it (indented).
        private void BuildPermissionBoxes()
        {
            UserPermissionsPanel.Children.Clear();
            _permissionBoxes.Clear();

            foreach (var group in AppPermissions.All.GroupBy(p => p.Group))
            {
                var panel = new StackPanel { Margin = new Thickness(6, 0, 44, 12) };
                panel.Children.Add(new TextBlock
                {
                    Text = group.Key,
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush"),
                    Margin = new Thickness(0, 0, 0, 2)
                });

                foreach (var permission in group)
                {
                    var box = new CheckBox
                    {
                        Content = permission.Label,
                        Margin = new Thickness(permission.IsAction ? 28 : 0, 8, 0, 0)
                    };

                    var captured = permission;
                    box.Checked += (_, _) => OnPermissionTick(captured, true);
                    box.Unchecked += (_, _) => OnPermissionTick(captured, false);

                    _permissionBoxes[permission.Key] = box;
                    panel.Children.Add(box);
                }

                UserPermissionsPanel.Children.Add(panel);
            }
        }

        // Ticking an action ticks its page / part; unticking a page / part clears its actions.
        private void OnPermissionTick(AppPermission permission, bool on)
        {
            if (_changingTicks)
                return;

            _changingTicks = true;
            try
            {
                if (permission.IsAction && on)
                    _permissionBoxes[permission.Parent!].IsChecked = true;

                if (!permission.IsAction && !on)
                {
                    foreach (var action in AppPermissions.All.Where(p => p.Parent == permission.Key))
                        _permissionBoxes[action.Key].IsChecked = false;
                }
            }
            finally
            {
                _changingTicks = false;
            }

            RefreshTickAvailability();
        }

        // Actions can only be ticked once their page / part is ticked; an administrator has everything, locked.
        private void RefreshTickAvailability()
        {
            bool admin = UserIsAdminCheckBox.IsChecked == true;

            foreach (var permission in AppPermissions.All)
            {
                var box = _permissionBoxes[permission.Key];
                box.IsEnabled = !admin && (!permission.IsAction || _permissionBoxes[permission.Parent!].IsChecked == true);
            }
        }

        private void SetPermissionTicks(Func<string, bool> isOn)
        {
            _changingTicks = true;
            try
            {
                foreach (var pair in _permissionBoxes)
                    pair.Value.IsChecked = isOn(pair.Key);
            }
            finally
            {
                _changingTicks = false;
            }

            RefreshTickAvailability();
        }

        // Administrator = everything: all boxes ticked and locked.
        private void UpdateAdminTick()
        {
            if (UserIsAdminCheckBox.IsChecked == true)
                SetPermissionTicks(_ => true);
            else
                RefreshTickAvailability();
        }

        private static void UsersGrid_AutoGeneratingColumn(object? sender, DataGridAutoGeneratingColumnEventArgs e)
        {
            if (e.PropertyDescriptor is System.ComponentModel.PropertyDescriptor descriptor && !descriptor.IsBrowsable)
            {
                e.Cancel = true;
                return;
            }

            e.Column.Header = Regex.Replace(e.PropertyName, "(?<=[a-z])(?=[A-Z])", " ");
        }

        private void RefreshUsers()
        {
            try
            {
                int? selected = (UsersDataGrid.SelectedItem as UserDisplay)?.Id;
                var rows = _userService.GetAll().Select(UserDisplay.From).ToList();

                UsersDataGrid.ItemsSource = null;
                UsersDataGrid.ItemsSource = rows;

                if (selected.HasValue)
                    UsersDataGrid.SelectedItem = rows.FirstOrDefault(r => r.Id == selected.Value);
            }
            catch (Exception ex)
            {
                AppMessage.Show("Failed to load users.\n" + ex.Message);
            }
        }

        private void ClearUserForm()
        {
            _editingUser = null;

            foreach (var box in new[] { UserFullNameTextBox, UserNicTextBox, UserPhoneTextBox, UserAddressTextBox,
                                        UserEmailTextBox, UserEmergencyTextBox, UserUsernameTextBox })
                box.Clear();

            UserPasswordBox.Clear();
            UserPasswordConfirmBox.Clear();
            UserBirthDatePicker.SelectedDate = null;
            UserJoinedDatePicker.SelectedDate = DateTime.Today;
            UserUsernameTextBox.IsReadOnly = false;
            UserPasswordLabel.Text = "Password *";

            UserIsAdminCheckBox.IsChecked = false;
            SetPermissionTicks(_ => false);

            SaveUserButton.Content = "Save User";
            UsersDataGrid.SelectedItem = null;
        }

        // Selecting a user loads them into the form and switches Save to Update.
        private void LoadUserIntoForm(int id)
        {
            var user = _userService.GetById(id);
            if (user == null)
                return;

            _editingUser = user;

            UserFullNameTextBox.Text = user.FullName;
            UserNicTextBox.Text = user.NicNumber;
            UserPhoneTextBox.Text = user.Phone;
            UserAddressTextBox.Text = user.Address;
            UserEmailTextBox.Text = user.Email;
            UserEmergencyTextBox.Text = user.EmergencyContact;
            UserBirthDatePicker.SelectedDate = ParseDate(user.DateOfBirth);
            UserJoinedDatePicker.SelectedDate = ParseDate(user.JoinedDate);

            UserUsernameTextBox.Text = user.Username;
            UserUsernameTextBox.IsReadOnly = true;                 // the username does not change
            UserPasswordBox.Clear();
            UserPasswordConfirmBox.Clear();
            UserPasswordLabel.Text = "New password";               // blank = keep the current one

            UserIsAdminCheckBox.IsChecked = user.IsAdmin;
            SetPermissionTicks(key => user.IsAdmin || user.Permissions.Contains(key));

            SaveUserButton.Content = "Update User";
        }

        private static DateTime? ParseDate(string? text) =>
            DateTime.TryParse(text, out var date) ? date : null;

        private UserInput ReadUserForm() => new()
        {
            FullName = UserFullNameTextBox.Text,
            NicNumber = UserNicTextBox.Text,
            Phone = UserPhoneTextBox.Text,
            Address = UserAddressTextBox.Text,
            DateOfBirth = UserBirthDatePicker.SelectedDate?.ToString("yyyy-MM-dd"),
            Email = UserEmailTextBox.Text,
            EmergencyContact = UserEmergencyTextBox.Text,
            JoinedDate = (UserJoinedDatePicker.SelectedDate ?? DateTime.Today).ToString("yyyy-MM-dd"),
            Username = UserUsernameTextBox.Text,
            IsAdmin = UserIsAdminCheckBox.IsChecked == true,
            Permissions = _permissionBoxes.Where(p => p.Value.IsChecked == true).Select(p => p.Key).ToList()
        };

        private void SaveUser_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (UserPasswordBox.Password != UserPasswordConfirmBox.Password)
                {
                    AppMessage.Show("The two passwords are not the same.");
                    return;
                }

                var input = ReadUserForm();
                AppUser saved;

                if (_editingUser == null)
                {
                    saved = _userService.CreateUser(input, UserPasswordBox.Password, _currentUser);
                    AppMessage.Show($"User {saved.Username} saved.");
                }
                else
                {
                    saved = _userService.UpdateUser(_editingUser.Id, input, UserPasswordBox.Password, _currentUser);
                    AppMessage.Show($"User {saved.Username} updated.");

                    // changing your own access takes effect straight away
                    if (_currentUser != null && saved.Id == _currentUser.Id)
                    {
                        _currentUser = saved;
                        ShowUserBadge(saved);
                        ApplyAccess(saved, keepTab: true);
                    }
                }

                ClearUserForm();
                RefreshUsers();
            }
            catch (Exception ex)
            {
                AppMessage.Show(ex.Message);
            }
        }

        private void SetSelectedUserActive(bool active)
        {
            try
            {
                if (UsersDataGrid.SelectedItem is not UserDisplay row)
                {
                    AppMessage.Show("Select a user first.");
                    return;
                }

                _userService.SetActive(row.Id, active, _currentUser);
                ClearUserForm();
                RefreshUsers();
            }
            catch (Exception ex)
            {
                AppMessage.Show(ex.Message);
            }
        }
    }
}
