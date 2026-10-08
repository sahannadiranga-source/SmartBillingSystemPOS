using System.ComponentModel;

namespace POSGardenia.Models
{
    // One row of the Users list.
    public class UserDisplay
    {
        [Browsable(false)]
        public int Id { get; set; }
        public string Username { get; set; } = "";
        public string FullName { get; set; } = "";
        public string Phone { get; set; } = "";
        public string Access { get; set; } = "";
        public string Status { get; set; } = "";
        public string LastSignIn { get; set; } = "";

        public static UserDisplay From(AppUser user) => new()
        {
            Id = user.Id,
            Username = user.Username,
            FullName = user.FullName,
            Phone = user.Phone,
            Access = user.AccessSummary,
            Status = user.IsActive ? "Active" : "Deactivated",
            LastSignIn = user.LastLoginAt ?? "never"
        };
    }

    // The little pieces of the signed-in badge.
    public static class UserBadge
    {
        private static readonly System.Windows.Media.Color[] Palette =
        {
            System.Windows.Media.Color.FromRgb(0x1F, 0x6F, 0xEB),   // blue
            System.Windows.Media.Color.FromRgb(0x0E, 0xA5, 0xA4),   // teal
            System.Windows.Media.Color.FromRgb(0x8B, 0x5C, 0xF6),   // purple
            System.Windows.Media.Color.FromRgb(0xEA, 0x58, 0x0C),   // orange
            System.Windows.Media.Color.FromRgb(0xDB, 0x27, 0x77),   // pink
            System.Windows.Media.Color.FromRgb(0x16, 0xA3, 0x4A)    // green
        };

        // "Nadiranga Somawardhana" -> "NS", "kamal" -> "K", "" -> "?"
        public static string Initials(string? fullName)
        {
            var words = (fullName ?? "").Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
                return "?";

            string first = words[0].Substring(0, 1);
            string last = words.Length > 1 ? words[^1].Substring(0, 1) : "";
            return (first + last).ToUpperInvariant();
        }

        // each person keeps the same colour
        public static System.Windows.Media.Color AvatarColor(int userId) => Palette[System.Math.Abs(userId) % Palette.Length];

        public static string Greeting(System.DateTime now) =>
            now.Hour < 12 ? "Good morning" : now.Hour < 17 ? "Good afternoon" : "Good evening";
    }

    // Which tabs a signed-in person sees. Kept apart from the window so it can be tested.
    public static class AccessMap
    {
        public const string Pos = "pos";
        public const string Tables = "tables";
        public const string Inventory = "inventory";
        public const string DailyStock = "inventory.daily";
        public const string StockItems = "inventory.items";
        public const string History = "history";
        public const string Reports = "reports";
        public const string Management = "management";
        public const string Categories = "management.categories";
        public const string Products = "management.products";
        public const string TableMaster = "management.tables";
        public const string Users = "management.users";
        public const string Settings = "settings";

        public static bool CanSee(AppUser user, string tab) => tab switch
        {
            Pos => user.Can(AppPermissions.Pos),
            Tables => user.Can(AppPermissions.Tables),
            Inventory => user.CanOpenInventory,
            DailyStock => user.Can(AppPermissions.InventoryDaily),
            StockItems => user.Can(AppPermissions.InventoryItems),
            History => user.Can(AppPermissions.History),
            Reports => user.Can(AppPermissions.Reports),
            Management => user.CanOpenManagement,
            Categories => user.Can(AppPermissions.Categories),
            Products => user.Can(AppPermissions.Products),
            TableMaster => user.Can(AppPermissions.TableMaster),
            Users => user.CanManageUsers,
            Settings => user.Can(AppPermissions.Settings),
            _ => false
        };
    }
}
