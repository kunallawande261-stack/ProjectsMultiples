using FolderComparerUI.Models;
using FolderComparerUI.Services;
using System.IO;
using System.Text.Json;
using Xunit;

namespace FolderComparerTests
{
    /// <summary>
    /// Tests for SettingsService save/load round-trips.
    /// Each test gets its own temp directory so tests are fully isolated.
    /// </summary>
    public class SettingsServiceTests : IDisposable
    {
        private readonly string _dir;
        private readonly string _path;
        private readonly SettingsService _svc;

        public SettingsServiceTests()
        {
            _dir  = Path.Combine(Path.GetTempPath(), $"SettingsTest_{Guid.NewGuid():N}");
            _path = Path.Combine(_dir, "settings.json");
            Directory.CreateDirectory(_dir);
            _svc  = new SettingsService(_path);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { }
        }

        // ── Load ──────────────────────────────────────────────────────────────

        [Fact]
        public void Load_returns_default_AppSettings_when_file_does_not_exist()
        {
            var s = _svc.Load();

            Assert.NotNull(s);
            Assert.Null(s.LeftPath);
            Assert.Null(s.RightPath);
        }

        [Fact]
        public void Load_returns_default_AppSettings_when_file_is_empty()
        {
            File.WriteAllText(_path, "");

            var s = _svc.Load();

            Assert.NotNull(s);
        }

        [Fact]
        public void Load_returns_default_AppSettings_when_file_contains_invalid_json()
        {
            File.WriteAllText(_path, "{ this is not valid json }}}");

            var s = _svc.Load();

            // Must not throw — graceful fallback
            Assert.NotNull(s);
        }

        // ── Save ──────────────────────────────────────────────────────────────

        [Fact]
        public void Save_creates_file_when_it_does_not_exist()
        {
            _svc.Save(new AppSettings { LeftPath = "C:\\left" });

            Assert.True(File.Exists(_path));
        }

        [Fact]
        public void Save_creates_intermediate_directories_if_missing()
        {
            string deep = Path.Combine(_dir, "a", "b", "c", "settings.json");
            var svc     = new SettingsService(deep);

            svc.Save(new AppSettings { LeftPath = "C:\\test" });

            Assert.True(File.Exists(deep));
        }

        [Fact]
        public void Save_writes_valid_json()
        {
            _svc.Save(new AppSettings { LeftPath = "C:\\left" });

            string json = File.ReadAllText(_path);
            // Must not throw
            var parsed  = JsonSerializer.Deserialize<AppSettings>(json);
            Assert.NotNull(parsed);
        }

        [Fact]
        public void Save_overwrites_existing_file()
        {
            _svc.Save(new AppSettings { LeftPath = "C:\\original" });
            _svc.Save(new AppSettings { LeftPath = "C:\\updated" });

            var loaded = _svc.Load();
            Assert.Equal("C:\\updated", loaded.LeftPath);
        }

        // ── Round-trip ────────────────────────────────────────────────────────

        [Fact]
        public void Save_then_Load_preserves_all_string_fields()
        {
            var original = new AppSettings
            {
                LeftPath          = @"C:\left",
                RightPath         = @"C:\right",
                ExcludePatterns   = ".log,.tmp",
                LogPath           = @"C:\log\out.txt",
                ExcelPath         = @"C:\reports\results.xlsx",
                SheetName         = "MySheet",
                ExportPath        = @"C:\export",
                BeyondComparePath = @"C:\BC4\BCompare.exe",
            };

            _svc.Save(original);
            var loaded = _svc.Load();

            Assert.Equal(original.LeftPath,          loaded.LeftPath);
            Assert.Equal(original.RightPath,         loaded.RightPath);
            Assert.Equal(original.ExcludePatterns,   loaded.ExcludePatterns);
            Assert.Equal(original.LogPath,            loaded.LogPath);
            Assert.Equal(original.ExcelPath,          loaded.ExcelPath);
            Assert.Equal(original.SheetName,          loaded.SheetName);
            Assert.Equal(original.ExportPath,         loaded.ExportPath);
            Assert.Equal(original.BeyondComparePath,  loaded.BeyondComparePath);
        }

        [Fact]
        public void Save_then_Load_preserves_all_bool_fields_true()
        {
            var original = new AppSettings
            {
                EnableLogging      = true,
                SmartXml           = true,
                Topmost            = true,
                KeepRoot           = true,
                CategorySubfolders = true,
            };

            _svc.Save(original);
            var loaded = _svc.Load();

            Assert.True(loaded.EnableLogging);
            Assert.True(loaded.SmartXml);
            Assert.True(loaded.Topmost);
            Assert.True(loaded.KeepRoot);
            Assert.True(loaded.CategorySubfolders);
        }

        [Fact]
        public void Save_then_Load_preserves_all_bool_fields_false()
        {
            var original = new AppSettings
            {
                EnableLogging      = false,
                SmartXml           = false,
                Topmost            = false,
                KeepRoot           = false,
                CategorySubfolders = false,
            };

            _svc.Save(original);
            var loaded = _svc.Load();

            Assert.False(loaded.EnableLogging);
            Assert.False(loaded.SmartXml);
            Assert.False(loaded.Topmost);
            Assert.False(loaded.KeepRoot);
            Assert.False(loaded.CategorySubfolders);
        }

        [Fact]
        public void Save_then_Load_preserves_mode_and_target_tags()
        {
            var original = new AppSettings
            {
                ModeTag          = "3",
                TargetTag        = "4L",
                ExcelCategoryTag = "2",
            };

            _svc.Save(original);
            var loaded = _svc.Load();

            Assert.Equal("3",  loaded.ModeTag);
            Assert.Equal("4L", loaded.TargetTag);
            Assert.Equal("2",  loaded.ExcelCategoryTag);
        }

        [Fact]
        public void Save_then_Load_preserves_null_string_fields_as_null()
        {
            var original = new AppSettings
            {
                LeftPath  = null,
                RightPath = null,
                LogPath   = null,
            };

            _svc.Save(original);
            var loaded = _svc.Load();

            Assert.Null(loaded.LeftPath);
            Assert.Null(loaded.RightPath);
            Assert.Null(loaded.LogPath);
        }

        [Fact]
        public void Save_then_Load_preserves_empty_string_fields()
        {
            var original = new AppSettings
            {
                LeftPath  = "",
                RightPath = "",
                SheetName = "",
            };

            _svc.Save(original);
            var loaded = _svc.Load();

            // Empty string may round-trip as null via JSON — either is acceptable
            Assert.True(loaded.LeftPath  is null or "");
            Assert.True(loaded.RightPath is null or "");
            Assert.True(loaded.SheetName is null or "");
        }

        [Fact]
        public void Multiple_save_load_cycles_stay_consistent()
        {
            // Simulate multiple app restarts
            for (int i = 0; i < 5; i++)
            {
                _svc.Save(new AppSettings { LeftPath = $"C:\\cycle{i}" });
                var loaded = _svc.Load();
                Assert.Equal($"C:\\cycle{i}", loaded.LeftPath);
            }
        }

        // ── DefaultPath ───────────────────────────────────────────────────────

        [Fact]
        public void DefaultPath_is_under_AppData()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            Assert.StartsWith(appData, SettingsService.DefaultPath,
                StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void DefaultPath_ends_with_settings_json()
        {
            Assert.EndsWith("settings.json", SettingsService.DefaultPath,
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
