using POSGardenia.Data;
using POSGardenia.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace POSGardenia.Services
{
    // What the administrator types in the Users tab.
    public class UserInput
    {
        public string FullName { get; set; } = "";
        public string NicNumber { get; set; } = "";
        public string Phone { get; set; } = "";
        public string Address { get; set; } = "";
        public string? DateOfBirth { get; set; }
        public string Email { get; set; } = "";
        public string EmergencyContact { get; set; } = "";
        public string JoinedDate { get; set; } = DateTime.Today.ToString("yyyy-MM-dd");
        public string Username { get; set; } = "";
        public bool IsAdmin { get; set; }
        public IEnumerable<string> Permissions { get; set; } = Array.Empty<string>();
    }

    // Accounts, sign-in and who may change them. Only an administrator can create or change accounts
    // (checked here, not just by hiding the screen).
    public class UserService
    {
        public const int MinPasswordLength = 6;
        public const int MaxWrongAttempts = 5;
        public static readonly TimeSpan LockTime = TimeSpan.FromSeconds(60);

        private static readonly Regex UsernamePattern = new(@"^[A-Za-z0-9._-]{3,30}$", RegexOptions.Compiled);

        private readonly UserRepository _users = new();
        private readonly Func<DateTime> _now;

        // Wrong-password counting, per username (even for names that do not exist, so nothing is given away).
        private static readonly Dictionary<string, (int Fails, DateTime LockedUntil)> Attempts = new();

        public UserService() : this(() => DateTime.Now) { }
        public UserService(Func<DateTime> now) { _now = now; }

        public bool HasUsers() => _users.Count() > 0;

        public List<AppUser> GetAll() => _users.GetAll();

        public AppUser? GetById(int id) => _users.GetById(id);

        // ----- the very first account -----

        // The app starts with no accounts, so the first one is made on the sign-in screen and is an administrator.
        public AppUser CreateFirstAdmin(string fullName, string username, string password)
        {
            if (HasUsers())
                throw new Exception("An administrator already exists. Sign in instead.");

            fullName = (fullName ?? "").Trim();
            if (fullName.Length == 0)
                throw new Exception("Enter the full name.");

            ValidateUsername(username);
            ValidatePassword(password);

            var user = new AppUser
            {
                Username = username.Trim(),
                PasswordHash = PasswordHasher.Hash(password),
                FullName = fullName,
                JoinedDate = _now().ToString("yyyy-MM-dd"),
                IsAdmin = true
            };

            user.Id = _users.Add(user);
            return _users.GetById(user.Id)!;
        }

        // ----- accounts (administrator only) -----

        public AppUser CreateUser(UserInput input, string password, AppUser? actingUser)
        {
            RequireAdmin(actingUser);

            ValidateInput(input, isNew: true);
            ValidatePassword(password);

            if (_users.GetByUsername(input.Username.Trim()) != null)
                throw new Exception("That username is already taken.");

            var user = new AppUser { Username = input.Username.Trim(), PasswordHash = PasswordHasher.Hash(password) };
            Fill(user, input);

            user.Id = _users.Add(user);
            return _users.GetById(user.Id)!;
        }

        // newPassword blank = the password stays. The username cannot be changed.
        public AppUser UpdateUser(int id, UserInput input, string? newPassword, AppUser? actingUser)
        {
            RequireAdmin(actingUser);

            var user = _users.GetById(id) ?? throw new Exception("User not found.");

            ValidateInput(input, isNew: false);

            if (!string.IsNullOrEmpty(newPassword))
                ValidatePassword(newPassword);

            // the last administrator cannot be turned into an ordinary account
            if (user.IsAdmin && user.IsActive && !input.IsAdmin && _users.CountActiveAdmins() <= 1)
                throw new Exception("This is the only administrator. Make another administrator first.");

            Fill(user, input);
            _users.UpdateProfile(user);

            if (!string.IsNullOrEmpty(newPassword))
                _users.SetPasswordHash(user.Id, PasswordHasher.Hash(newPassword));

            return _users.GetById(id)!;
        }

        public void SetActive(int id, bool active, AppUser? actingUser)
        {
            RequireAdmin(actingUser);

            var user = _users.GetById(id) ?? throw new Exception("User not found.");

            if (!active)
            {
                if (actingUser!.Id == user.Id)
                    throw new Exception("You cannot deactivate your own account.");

                if (user.IsAdmin && user.IsActive && _users.CountActiveAdmins() <= 1)
                    throw new Exception("This is the only administrator and cannot be deactivated.");
            }

            _users.SetActive(id, active);
        }

        // ----- signing in -----

        public (AppUser? User, string Error) Authenticate(string username, string password)
        {
            username = (username ?? "").Trim();
            string key = username.ToLowerInvariant();

            if (username.Length == 0 || string.IsNullOrEmpty(password))
                return (null, "Enter your username and password.");

            var now = _now();

            lock (Attempts)
            {
                if (Attempts.TryGetValue(key, out var state) && state.LockedUntil > now)
                {
                    int seconds = (int)Math.Ceiling((state.LockedUntil - now).TotalSeconds);
                    return (null, $"Too many wrong attempts. Try again in {seconds} seconds.");
                }
            }

            var user = _users.GetByUsername(username);

            // say what is wrong: there is no such username, or the password is wrong
            if (user == null)
                return (null, "Username not found.");

            if (!PasswordHasher.Verify(password, user.PasswordHash))
            {
                int fails;
                lock (Attempts)
                {
                    Attempts.TryGetValue(key, out var state);
                    fails = state.Fails + 1;
                    Attempts[key] = fails >= MaxWrongAttempts ? (0, now + LockTime) : (fails, DateTime.MinValue);
                }

                if (fails >= MaxWrongAttempts)
                    return (null, $"Wrong password. This account is locked for {(int)LockTime.TotalSeconds} seconds.");

                int left = MaxWrongAttempts - fails;
                return (null, $"Wrong password ({left} {(left == 1 ? "try" : "tries")} left).");
            }

            if (!user!.IsActive)
                return (null, "This account is deactivated. Ask an administrator.");

            lock (Attempts)
                Attempts.Remove(key);

            _users.TouchLastLogin(user.Id);
            return (_users.GetById(user.Id), "");
        }

        // ----- checks -----

        private static void RequireAdmin(AppUser? actingUser)
        {
            if (actingUser == null || !actingUser.IsAdmin || !actingUser.IsActive)
                throw new Exception("Only an administrator can manage users.");
        }

        private static void ValidateUsername(string username)
        {
            if (!UsernamePattern.IsMatch((username ?? "").Trim()))
                throw new Exception("Username: 3 to 30 letters, numbers, dot, dash or underscore (no spaces).");
        }

        private static void ValidatePassword(string password)
        {
            if ((password ?? "").Length < MinPasswordLength)
                throw new Exception($"Password must be at least {MinPasswordLength} characters.");
        }

        private static void ValidateInput(UserInput input, bool isNew)
        {
            if (string.IsNullOrWhiteSpace(input.FullName))
                throw new Exception("Enter the full name.");
            if (string.IsNullOrWhiteSpace(input.NicNumber))
                throw new Exception("Enter the NIC / ID number.");
            if (string.IsNullOrWhiteSpace(input.Phone))
                throw new Exception("Enter the phone number.");
            if (string.IsNullOrWhiteSpace(input.Address))
                throw new Exception("Enter the address.");

            if (!string.IsNullOrWhiteSpace(input.Email) && !input.Email.Contains('@'))
                throw new Exception("Email: enter a valid address, or leave it blank.");

            if (isNew)
                ValidateUsername(input.Username);

            if (!input.IsAdmin && AppPermissions.Normalize(input.Permissions).Count == 0)
                throw new Exception("Tick at least one page this person can open (or make them an administrator).");
        }

        private static void Fill(AppUser user, UserInput input)
        {
            user.FullName = input.FullName.Trim();
            user.NicNumber = input.NicNumber.Trim();
            user.Phone = input.Phone.Trim();
            user.Address = input.Address.Trim();
            user.DateOfBirth = string.IsNullOrWhiteSpace(input.DateOfBirth) ? null : input.DateOfBirth;
            user.Email = (input.Email ?? "").Trim();
            user.EmergencyContact = (input.EmergencyContact ?? "").Trim();
            user.JoinedDate = string.IsNullOrWhiteSpace(input.JoinedDate) ? DateTime.Today.ToString("yyyy-MM-dd") : input.JoinedDate;
            user.IsAdmin = input.IsAdmin;
            user.Permissions = input.IsAdmin ? new HashSet<string>() : AppPermissions.Normalize(input.Permissions);
        }
    }
}
