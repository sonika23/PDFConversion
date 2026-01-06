using System.IO;
using System.Text.Json;
using PdfTranslator.Models;
using Serilog;

namespace PdfTranslator.Services
{
    public class ConfigurationService
    {
        private readonly string _settingsPath;
        private AppSettings? _settings;

        public ConfigurationService()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PdfTranslator");
            
            Directory.CreateDirectory(appDataPath);
            _settingsPath = Path.Combine(appDataPath, "settings.json");
        }

        public AppSettings LoadSettings()
        {
            if (_settings != null)
                return _settings;

            try
            {
                if (File.Exists(_settingsPath))
                {
                    var json = File.ReadAllText(_settingsPath);
                    _settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                    Log.Information("Settings loaded from {Path}", _settingsPath);
                }
                else
                {
                    _settings = new AppSettings();
                    Log.Information("Created new settings (no existing file found)");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load settings, using defaults");
                _settings = new AppSettings();
            }

            return _settings;
        }

        public void SaveSettings(AppSettings settings)
        {
            try
            {
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                
                File.WriteAllText(_settingsPath, json);
                _settings = settings;
                Log.Information("Settings saved to {Path}", _settingsPath);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to save settings");
                throw;
            }
        }

        public void ResetSettings()
        {
            _settings = new AppSettings();
            SaveSettings(_settings);
        }
    }
}
