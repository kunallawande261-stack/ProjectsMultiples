using FolderComparerUI.Models;
using System;
using System.IO;
using System.Text.Json;

namespace FolderComparerUI.Services
{
    public class SettingsService : ISettingsService
    {
        // #10 — bump this whenever a new field is added to AppSettings
        private const int CurrentVersion = 1;

        public static readonly string DefaultPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FolderComparer", "settings.json");

        private readonly string _path;

        public SettingsService() : this(DefaultPath) { }
        public SettingsService(string path) => _path = path;

        public AppSettings Load()
        {
            try
            {
                if (!File.Exists(_path)) return new AppSettings();
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path));
                if (s == null) return new AppSettings();
                // #10 — migrate older settings files that are missing newer fields
                s = Migrate(s);
                return s;
            }
            catch { return new AppSettings(); }
        }

        public void Save(AppSettings settings)
        {
            try
            {
                settings.Version = CurrentVersion;
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
                File.WriteAllText(_path,
                    JsonSerializer.Serialize(settings,
                        new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        /// <summary>
        /// Apply defaults for any fields that were added after the saved version.
        /// Add a new case here each time CurrentVersion is bumped.
        /// </summary>
        private static AppSettings Migrate(AppSettings s)
        {
            // Version 0 → 1: no migration needed for initial release;
            // future versions add cases here, e.g.:
            //   if (s.Version < 2) s.SomeNewField = "default";
            s.Version = CurrentVersion;
            return s;
        }
    }
}
