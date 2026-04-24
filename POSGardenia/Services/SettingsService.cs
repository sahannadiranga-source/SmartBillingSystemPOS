using POSGardenia.Models;
using System;
using System.IO;
using System.Text.Json;

namespace POSGardenia.Services
{
    public class SettingsService
    {
        private readonly string _settingsFolder;
        private readonly string _settingsFile;

        public SettingsService()
        {
            _settingsFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "POSGardenia");

            _settingsFile = Path.Combine(_settingsFolder, "settings.json");
        }

        public AppSettings Load()
        {
            try
            {
                if (!Directory.Exists(_settingsFolder))
                    Directory.CreateDirectory(_settingsFolder);

                if (!File.Exists(_settingsFile))
                    return new AppSettings();

                var json = File.ReadAllText(_settingsFile);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
            catch
            {
                return new AppSettings();
            }
        }

        public void Save(AppSettings settings)
        {
            try
            {
                if (!Directory.Exists(_settingsFolder))
                    Directory.CreateDirectory(_settingsFolder);

                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                File.WriteAllText(_settingsFile, json);
            }
            catch (Exception ex)
            {
                throw new Exception("Failed to save settings. " + ex.Message, ex);
            }
        }
    }
}