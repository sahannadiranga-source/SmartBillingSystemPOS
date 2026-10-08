using System;
using System.Collections.Generic;
using System.Linq;

namespace POSGardenia.Models
{
    // One thing an account can be allowed to do. A page or part ("can open it"), or an action inside one
    // (Parent = the page / part it belongs to; an action only counts when its parent is allowed too).
    public record AppPermission(string Key, string Label, string Group, string? Parent = null)
    {
        public bool IsAction => Parent != null;
    }

    public static class AppPermissions
    {
        // pages and parts
        public const string Pos = "pos";
        public const string Tables = "tables";
        public const string History = "history";
        public const string Reports = "reports";
        public const string Settings = "settings";
        public const string InventoryDaily = "inventory.daily";
        public const string InventoryItems = "inventory.items";
        public const string Categories = "management.categories";
        public const string Products = "management.products";
        public const string TableMaster = "management.tables";

        // actions inside them
        public const string PosPay = "pos.pay";
        public const string TablesAddItems = "tables.additems";
        public const string TablesKitchen = "tables.kitchen";
        public const string TablesSettle = "tables.settle";
        public const string TablesVoid = "tables.void";
        public const string HistoryReprint = "history.reprint";
        public const string ReportsLoad = "reports.load";
        public const string ReportsPdf = "reports.pdf";
        public const string ReportsExpenses = "reports.expenses";
        public const string StockAdd = "inventory.items.addstock";
        public const string StockCount = "inventory.items.count";
        public const string CategoriesEdit = "management.categories.edit";
        public const string CategoriesDelete = "management.categories.delete";
        public const string ProductsEdit = "management.products.edit";
        public const string ProductsDelete = "management.products.delete";
        public const string TableMasterEdit = "management.tables.edit";
        public const string TableMasterDelete = "management.tables.delete";
        public const string SettingsDevices = "settings.devices";
        public const string SettingsBackup = "settings.backup";
        public const string SettingsReports = "settings.reports";

        // The tick boxes of the Users tab, in this order, grouped by page. (The Users tab itself is for administrators only.)
        public static readonly IReadOnlyList<AppPermission> All = new List<AppPermission>
        {
            new(Pos, "POS", "POS"),
            new(PosPay, "Take payment (Pay Now)", "POS", Pos),

            new(Tables, "Tables", "Tables"),
            new(TablesAddItems, "Add more items", "Tables", Tables),
            new(TablesKitchen, "Send to kitchen", "Tables", Tables),
            new(TablesSettle, "Open for settlement", "Tables", Tables),
            new(TablesVoid, "Void bills", "Tables", Tables),

            new(History, "History", "History"),
            new(HistoryReprint, "Reprint receipts", "History", History),

            new(Reports, "Reports", "Reports"),
            new(ReportsLoad, "Load report of another date", "Reports", Reports),
            new(ReportsPdf, "Save report as PDF", "Reports", Reports),
            new(ReportsExpenses, "Add, change and delete expenses", "Reports", Reports),

            new(InventoryDaily, "Daily Stock", "Inventory"),
            new(InventoryItems, "Stock Items", "Inventory"),
            new(StockAdd, "Add stock", "Inventory", InventoryItems),
            new(StockCount, "Enter morning manual count", "Inventory", InventoryItems),

            new(Categories, "Categories", "Management"),
            new(CategoriesEdit, "Add, change, deactivate", "Management", Categories),
            new(CategoriesDelete, "Delete permanently", "Management", Categories),
            new(Products, "Products", "Management"),
            new(ProductsEdit, "Add, change, deactivate", "Management", Products),
            new(ProductsDelete, "Delete permanently", "Management", Products),
            new(TableMaster, "Table Master", "Management"),
            new(TableMasterEdit, "Add, change, deactivate", "Management", TableMaster),
            new(TableMasterDelete, "Delete permanently", "Management", TableMaster),

            new(Settings, "Settings", "Settings"),
            new(SettingsDevices, "Printer and keyboard settings", "Settings", Settings),
            new(SettingsBackup, "Backup settings and Backup Now", "Settings", Settings),
            new(SettingsReports, "Daily report settings", "Settings", Settings)
        };

        public static bool IsKnown(string key) => All.Any(p => p.Key == key);

        public static string? ParentOf(string key) => All.FirstOrDefault(p => p.Key == key)?.Parent;

        // Known keys only, and an action only when its page / part is there too.
        public static HashSet<string> Normalize(IEnumerable<string>? keys)
        {
            var wanted = new HashSet<string>(keys ?? Enumerable.Empty<string>());
            return All
                .Where(p => wanted.Contains(p.Key) && (p.Parent == null || wanted.Contains(p.Parent)))
                .Select(p => p.Key)
                .ToHashSet();
        }

        // Saved as "pos,pos.pay,tables": the same order every time.
        public static string Join(IEnumerable<string>? keys)
        {
            var normal = Normalize(keys);
            return string.Join(",", All.Where(p => normal.Contains(p.Key)).Select(p => p.Key));
        }

        public static HashSet<string> Parse(string? text)
        {
            return Normalize((text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
    }

    // A person who can sign in. Passwords are never kept, only their hash (PasswordHash).
    public class AppUser
    {
        public int Id { get; set; }
        public string Username { get; set; } = "";
        public string PasswordHash { get; set; } = "";

        // personal details
        public string FullName { get; set; } = "";
        public string NicNumber { get; set; } = "";
        public string Phone { get; set; } = "";
        public string Address { get; set; } = "";
        public string? DateOfBirth { get; set; }          // yyyy-MM-dd, optional
        public string Email { get; set; } = "";
        public string EmergencyContact { get; set; } = "";
        public string JoinedDate { get; set; } = "";      // yyyy-MM-dd

        // access
        public bool IsAdmin { get; set; }
        public bool IsActive { get; set; } = true;
        public HashSet<string> Permissions { get; set; } = new();

        public string CreatedAt { get; set; } = "";
        public string? LastLoginAt { get; set; }

        // An administrator can open everything; anyone else only what was ticked for them.
        // An action also needs the page / part it sits in.
        public bool Can(string permission)
        {
            if (IsAdmin)
                return true;

            if (!Permissions.Contains(permission))
                return false;

            var parent = AppPermissions.ParentOf(permission);
            return parent == null || Permissions.Contains(parent);
        }

        public bool CanOpenInventory => Can(AppPermissions.InventoryDaily) || Can(AppPermissions.InventoryItems);

        public bool CanOpenManagement =>
            IsAdmin || Can(AppPermissions.Categories) || Can(AppPermissions.Products) || Can(AppPermissions.TableMaster);

        public bool CanManageUsers => IsAdmin;

        // The pages / parts, and how many extra actions were ticked: "POS, Stock Items (+2 actions)".
        public string AccessSummary
        {
            get
            {
                if (IsAdmin)
                    return "Administrator";

                var allowed = AppPermissions.All.Where(p => Can(p.Key)).ToList();
                string pages = string.Join(", ", allowed.Where(p => !p.IsAction).Select(p => p.Label));
                int actions = allowed.Count(p => p.IsAction);

                return actions == 0 ? pages : $"{pages} (+{actions} action{(actions == 1 ? "" : "s")})";
            }
        }
    }
}
