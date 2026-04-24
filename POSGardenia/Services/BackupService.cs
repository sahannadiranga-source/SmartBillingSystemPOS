using Microsoft.Data.Sqlite;
using POSGardenia.Data;
using System;
using System.IO;
using System.Linq;

namespace POSGardenia.Services
{
    public class BackupService
    {
        public string BackupNow(string backupFolder)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(backupFolder))
                    throw new Exception("Backup folder is not selected.");

                if (!Directory.Exists(backupFolder))
                    Directory.CreateDirectory(backupFolder);

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string backupFileName = $"SmartBillingSystemPOS_Backup_{timestamp}.db";
                string backupPath = Path.Combine(backupFolder, backupFileName);

                using var connection = DatabaseHelper.GetConnection();
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = $"VACUUM INTO '{backupPath.Replace("'", "''")}';";
                command.ExecuteNonQuery();

                DeleteOldBackups(backupFolder, 30);

                return backupPath;
            }
            catch (Exception ex)
            {
                throw new Exception("Backup failed. " + ex.Message, ex);
            }
        }

        private void DeleteOldBackups(string backupFolder, int keepCount)
        {
            try
            {
                var files = Directory.GetFiles(backupFolder, "SmartBillingSystemPOS_Backup_*.db")
                    .Select(x => new FileInfo(x))
                    .OrderByDescending(x => x.CreationTime)
                    .ToList();

                foreach (var file in files.Skip(keepCount))
                {
                    try
                    {
                        file.Delete();
                    }
                    catch
                    {
                        // ignore old backup delete failure
                    }
                }
            }
            catch
            {
                // ignore cleanup failure
            }
        }
    }
}