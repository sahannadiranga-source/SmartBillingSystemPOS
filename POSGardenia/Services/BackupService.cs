using Microsoft.Data.Sqlite;
using POSGardenia.Data;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace POSGardenia.Services
{
    public class BackupResult
    {
        public string Path { get; set; } = "";

        // Set when the copy was made but something needs the owner's attention
        // (no drive chosen, drive unplugged, backups on the same disk as the database).
        public string? Warning { get; set; }
    }

    // Keeps a copy of the database on an external drive (or any folder the owner chooses).
    // If that folder is not available (drive unplugged) the copy is saved on this computer instead,
    // so there is never a gap in backups, and the owner is warned.
    public class BackupService
    {
        private const string FilePrefix = "SmartBillingSystemPOS_Backup_";
        private const string TimeFormat = "yyyyMMdd_HHmmss";

        private readonly string _fallbackFolder;

        public BackupService() : this(DefaultFallbackFolder()) { }

        public BackupService(string fallbackFolder)
        {
            _fallbackFolder = fallbackFolder;
        }

        private static string DefaultFallbackFolder() =>
            Path.Combine(DatabaseHelper.DataFolder, "backups");

        // -----------------------------
        // Backup
        // -----------------------------

        public BackupResult BackupNow(string? backupFolder)
        {
            string? warning = DescribeFolderRisk(backupFolder, DatabaseHelper.GetDatabasePath());
            string fileName = $"{FilePrefix}{DateTime.Now.ToString(TimeFormat)}.db";
            bool chosen = !string.IsNullOrWhiteSpace(backupFolder);

            // 1. the folder the owner chose (external drive), when it is there
            if (chosen && IsDriveAvailable(backupFolder!))
            {
                try
                {
                    string written = WriteBackup(backupFolder!, fileName);
                    Prune(backupFolder!);
                    return new BackupResult { Path = written, Warning = warning };
                }
                catch (Exception ex)
                {
                    // drive removed mid-copy, read-only, full ... fall through to the local copy
                    warning = $"Could not write to the backup drive ({ex.Message.TrimEnd('.', ' ')}). A copy was saved on this computer instead.";
                }
            }

            // 2. safety copy on this computer
            try
            {
                string written = WriteBackup(_fallbackFolder, fileName);
                Prune(_fallbackFolder);
                return new BackupResult { Path = written, Warning = warning };
            }
            catch (Exception ex)
            {
                throw new Exception("Backup failed. " + ex.Message, ex);
            }
        }

        // Writes to a temporary name first and renames on success, so an interrupted copy
        // (drive pulled out) never leaves a broken file that looks like a real backup.
        private static string WriteBackup(string folder, string fileName)
        {
            Directory.CreateDirectory(folder);

            string finalPath = Path.Combine(folder, fileName);
            string tempPath = finalPath + ".tmp";

            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);

                using (var connection = DatabaseHelper.GetConnection())
                {
                    connection.Open();
                    using var command = connection.CreateCommand();
                    command.CommandText = $"VACUUM INTO '{tempPath.Replace("'", "''")}';";
                    command.ExecuteNonQuery();
                }

                File.Move(tempPath, finalPath, overwrite: true);
                return finalPath;
            }
            catch
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                throw;
            }
        }

        // -----------------------------
        // Where the copies go: is this folder a good place?
        // -----------------------------

        // The drive/share the folder lives on is present (a removed USB drive has no root).
        public static bool IsDriveAvailable(string folder)
        {
            try
            {
                string? root = Path.GetPathRoot(Path.GetFullPath(folder));
                return !string.IsNullOrEmpty(root) && Directory.Exists(root);
            }
            catch
            {
                return false;
            }
        }

        // null = fine. Otherwise a plain-words warning for the owner.
        public static string? DescribeFolderRisk(string? folder, string databasePath)
        {
            if (string.IsNullOrWhiteSpace(folder))
                return "No backup drive is chosen, so copies stay on this computer only. Choose an external drive in Settings.";

            if (!IsDriveAvailable(folder))
            {
                string root = SafeRoot(folder);
                return $"The backup drive ({root}) is not connected. Copies are being saved on this computer only - plug the drive in.";
            }

            string folderRoot = SafeRoot(folder);
            string dbRoot = SafeRoot(databasePath);
            if (string.Equals(folderRoot, dbRoot, StringComparison.OrdinalIgnoreCase))
                return $"The backup folder is on the same drive ({folderRoot}) as the database. If that disk fails you lose both - choose an external drive.";

            return null;
        }

        private static string SafeRoot(string path)
        {
            try { return Path.GetPathRoot(Path.GetFullPath(path)) ?? path; }
            catch { return path; }
        }

        // -----------------------------
        // Retention: 30 days, not "30 files"
        // -----------------------------
        // Keeps: the newest 10 copies, one copy per hour for the last 24 hours,
        // and the last copy of each day for 30 days. Everything older is removed.
        // Only files that look like our own backups are ever touched.

        public const int KeepNewest = 10;
        public const int KeepDays = 30;

        public static List<string> SelectFilesToDelete(IEnumerable<(string Path, DateTime Time)> backups, DateTime now)
        {
            var delete = new List<string>();
            var seenBuckets = new HashSet<string>();
            int index = 0;

            foreach (var file in backups.OrderByDescending(b => b.Time))
            {
                var age = now - file.Time;
                string bucket = age <= TimeSpan.FromHours(24)
                    ? "H" + file.Time.ToString("yyyyMMddHH", CultureInfo.InvariantCulture)
                    : "D" + file.Time.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

                bool isNewest = index++ < KeepNewest;

                if (isNewest)
                {
                    seenBuckets.Add(bucket);          // still counts as that hour's / day's copy
                    continue;
                }

                if (age > TimeSpan.FromDays(KeepDays) || !seenBuckets.Add(bucket))
                    delete.Add(file.Path);
            }

            return delete;
        }

        private static void Prune(string folder)
        {
            try
            {
                var backups = new List<(string Path, DateTime Time)>();

                foreach (var path in Directory.GetFiles(folder, FilePrefix + "*.db"))
                {
                    string name = System.IO.Path.GetFileNameWithoutExtension(path);
                    string stamp = name.Substring(FilePrefix.Length);

                    if (DateTime.TryParseExact(stamp, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
                        backups.Add((path, time));
                }

                foreach (var path in SelectFilesToDelete(backups, DateTime.Now))
                {
                    try { File.Delete(path); } catch { /* keep going */ }
                }
            }
            catch
            {
                // cleanup must never make a backup fail
            }
        }
    }
}
