using FolderComparerUI.Models;
using System;
using System.IO;
using System.Text.Json;

namespace FolderComparerUI.Services
{
    public class SettingsService
    {
        private static readonly string Path = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FolderComparer", "settings.json");

        public AppSettings Load()
        {
            try
            {
                if (!File.Exists(Path)) return new AppSettings();
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path));
                return s ?? new AppSettings();
            }
            catch { return new AppSettings(); }
        }

        public void Save(AppSettings settings)
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                File.WriteAllText(Path,
                    JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }
    }
}
