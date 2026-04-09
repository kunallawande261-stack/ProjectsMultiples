using FolderComparerUI.Models;
using FolderComparerUI.Services;
using System.IO;
using Xunit;

namespace FolderComparerTests
{
    /// <summary>Tests for ExcelExportService — writes real .xlsx files to temp dir.</summary>
    public class ExcelExportServiceTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly ExcelExportService _svc = new();

        public ExcelExportServiceTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"ExcelTest_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }

        private string TempFile(string name = "test.xlsx") =>
            Path.Combine(_tempDir, name);

        private static ResultRow Row(string cat, string rel) =>
            new() { Category = cat, Relative = rel, LeftFull = "", RightFull = "" };

        // ── Export ────────────────────────────────────────────────────────────

        [Fact]
        public void Export_creates_file_when_it_does_not_exist()
        {
            string path = TempFile();
            _svc.Export(path, "C:\\left", "C:\\right", "All",
                new[] { Row("Same", "a.txt") },
                1, 1, 0, 0, 0, 0, 0);

            Assert.True(File.Exists(path));
        }

        [Fact]
        public void Export_returns_non_empty_sheet_name()
        {
            string sheet = _svc.Export(TempFile(), "C:\\left", "C:\\right", "All",
                new[] { Row("Same", "a.txt") },
                1, 1, 0, 0, 0, 0, 0);

            Assert.False(string.IsNullOrWhiteSpace(sheet));
        }

        [Fact]
        public void Export_uses_user_supplied_sheet_name_when_provided()
        {
            string sheet = _svc.Export(TempFile(), "C:\\left", "C:\\right", "All",
                new[] { Row("Same", "a.txt") },
                1, 1, 0, 0, 0, 0, 0,
                userSheetName: "MySheet");

            Assert.Equal("MySheet", sheet);
        }

        [Fact]
        public void Export_sanitises_sheet_name_with_forbidden_chars()
        {
            // Colon, slash, and question mark are forbidden in Excel sheet names
            string sheet = _svc.Export(TempFile(), "C:\\left", "C:\\right", "All",
                new[] { Row("Same", "a.txt") },
                1, 1, 0, 0, 0, 0, 0,
                userSheetName: "My:Sheet/Name?");

            Assert.DoesNotContain(":", sheet);
            Assert.DoesNotContain("/", sheet);
            Assert.DoesNotContain("?", sheet);
        }

        [Fact]
        public void Export_truncates_sheet_name_to_31_chars()
        {
            string longName = new string('A', 50);
            string sheet = _svc.Export(TempFile(), "C:\\left", "C:\\right", "All",
                new[] { Row("Same", "a.txt") },
                1, 1, 0, 0, 0, 0, 0,
                userSheetName: longName);

            Assert.True(sheet.Length <= 31, $"Sheet name length {sheet.Length} exceeds 31");
        }

        [Fact]
        public void Export_appends_new_sheet_to_existing_file()
        {
            string path = TempFile();

            string sheet1 = _svc.Export(path, "L", "R", "All",
                new[] { Row("Same", "a.txt") }, 1, 1, 0, 0, 0, 0, 0);

            string sheet2 = _svc.Export(path, "L", "R", "All",
                new[] { Row("Different", "b.txt") }, 1, 0, 0, 0, 1, 0, 0);

            Assert.NotEqual(sheet1, sheet2);
            // File must still be valid (no exception = success)
            Assert.True(File.Exists(path));
        }

        [Fact]
        public void Export_deduplicates_sheet_names_when_same_name_reused()
        {
            string path = TempFile();

            _svc.Export(path, "L", "R", "All",
                new[] { Row("Same", "a.txt") }, 1, 1, 0, 0, 0, 0, 0,
                userSheetName: "Results");

            string sheet2 = _svc.Export(path, "L", "R", "All",
                new[] { Row("Same", "b.txt") }, 1, 1, 0, 0, 0, 0, 0,
                userSheetName: "Results");

            // Second export must get a different name (Results_2, Results_3, etc.)
            Assert.NotEqual("Results", sheet2);
            Assert.StartsWith("Results", sheet2);
        }

        [Fact]
        public void Export_handles_empty_rows_without_throwing()
        {
            // Should not throw even with zero data rows
            var exception = Record.Exception(() =>
                _svc.Export(TempFile(), "L", "R", "All",
                    Array.Empty<ResultRow>(), 0, 0, 0, 0, 0, 0, 0));

            Assert.Null(exception);
        }

        // ── IsFileLocked ──────────────────────────────────────────────────────

        [Fact]
        public void IsFileLocked_returns_false_for_existing_unlocked_file()
        {
            string path = TempFile("unlocked.xlsx");
            File.WriteAllText(path, "dummy");

            Assert.False(_svc.IsFileLocked(path));
        }

        [Fact]
        public void IsFileLocked_returns_true_when_file_is_open_exclusively()
        {
            string path = TempFile("locked.xlsx");
            using var stream = File.Open(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);

            Assert.True(_svc.IsFileLocked(path));
        }

        [Fact]
        public void IsFileLocked_returns_false_for_nonexistent_file()
        {
            // Non-existent file is not "locked" — it simply doesn't exist
            Assert.False(_svc.IsFileLocked(TempFile("does_not_exist.xlsx")));
        }
    }
}
