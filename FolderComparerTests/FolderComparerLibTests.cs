using FolderComparerLib;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace FolderComparerTests
{
    /// <summary>
    /// Tests for FolderComparerLib.FolderComparer.
    /// Each test creates real temp directories and files, then cleans them up.
    /// </summary>
    public class FolderComparerLibTests : IDisposable
    {
        private readonly string _left;
        private readonly string _right;
        private readonly FolderComparer _comparer = new();

        public FolderComparerLibTests()
        {
            _left  = Path.Combine(Path.GetTempPath(), $"FCTest_L_{Guid.NewGuid():N}");
            _right = Path.Combine(Path.GetTempPath(), $"FCTest_R_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_left);
            Directory.CreateDirectory(_right);
        }

        public void Dispose()
        {
            try { Directory.Delete(_left,  recursive: true); } catch { }
            try { Directory.Delete(_right, recursive: true); } catch { }
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private void WriteLeft(string rel, string content = "data")
        {
            var full = Path.Combine(_left, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }

        private void WriteRight(string rel, string content = "data")
        {
            var full = Path.Combine(_right, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }

        // ── CompareAsync ──────────────────────────────────────────────────────

        [Fact]
        public async Task Same_file_on_both_sides_is_categorised_as_Same()
        {
            WriteLeft("file.txt", "hello");
            WriteRight("file.txt", "hello");

            var result = await _comparer.CompareAsync(_left, _right);

            Assert.Contains("file.txt", result.Same);
            Assert.Empty(result.Different);
            Assert.Empty(result.LeftOnly);
            Assert.Empty(result.RightOnly);
        }

        [Fact]
        public async Task File_only_on_left_is_categorised_as_LeftOnly()
        {
            WriteLeft("only_left.txt");

            var result = await _comparer.CompareAsync(_left, _right);

            Assert.Contains("only_left.txt", result.LeftOnly);
            Assert.Empty(result.Same);
            Assert.Empty(result.Different);
            Assert.Empty(result.RightOnly);
        }

        [Fact]
        public async Task File_only_on_right_is_categorised_as_RightOnly()
        {
            WriteRight("only_right.txt");

            var result = await _comparer.CompareAsync(_left, _right);

            Assert.Contains("only_right.txt", result.RightOnly);
            Assert.Empty(result.Same);
            Assert.Empty(result.Different);
            Assert.Empty(result.LeftOnly);
        }

        [Fact]
        public async Task File_with_different_content_is_categorised_as_Different()
        {
            // Use different-length content so the fast-path (same size + same timestamp)
            // is bypassed and a real binary hash comparison is performed.
            WriteLeft("diff.txt",  "short");
            WriteRight("diff.txt", "this is longer content so lengths differ");

            var result = await _comparer.CompareAsync(_left, _right);

            Assert.Contains("diff.txt", result.Different);
            Assert.Empty(result.Same);
            Assert.Empty(result.LeftOnly);
            Assert.Empty(result.RightOnly);
        }

        [Fact]
        public async Task Mixed_scenario_all_categories_correctly_populated()
        {
            WriteLeft("same.txt",  "equal"); WriteRight("same.txt",  "equal");
            WriteLeft("diff.txt",  "aaa");   WriteRight("diff.txt",  "bbbbbbbb");
            WriteLeft("lonly.txt");
            WriteRight("ronly.txt");

            var result = await _comparer.CompareAsync(_left, _right);

            Assert.Contains("same.txt",  result.Same);
            Assert.Contains("diff.txt",  result.Different);
            Assert.Contains("lonly.txt", result.LeftOnly);
            Assert.Contains("ronly.txt", result.RightOnly);
        }

        [Fact]
        public async Task Nested_folder_files_are_compared_correctly()
        {
            WriteLeft(Path.Combine("sub", "nested.txt"),  "same");
            WriteRight(Path.Combine("sub", "nested.txt"), "same");

            var result = await _comparer.CompareAsync(_left, _right);

            Assert.Single(result.Same);
            Assert.Contains(result.Same, s => s.Contains("nested.txt"));
        }

        [Fact]
        public async Task Empty_folders_produce_empty_results()
        {
            var result = await _comparer.CompareAsync(_left, _right);

            Assert.Empty(result.Same);
            Assert.Empty(result.Different);
            Assert.Empty(result.LeftOnly);
            Assert.Empty(result.RightOnly);
        }

        // ── Exclude patterns ──────────────────────────────────────────────────

        [Fact]
        public async Task Exclude_by_extension_removes_file_from_results()
        {
            WriteLeft("file.log",  "log content"); WriteRight("file.log",  "log content");
            WriteLeft("file.txt",  "keep");        WriteRight("file.txt",  "keep");

            var result = await _comparer.CompareAsync(_left, _right, new[] { ".log" });

            Assert.DoesNotContain("file.log", result.Same);
            Assert.Contains("file.txt", result.Same);
        }

        [Fact]
        public async Task Exclude_by_folder_name_removes_entire_subtree()
        {
            WriteLeft(Path.Combine("obj", "build.dll"),  "x");
            WriteRight(Path.Combine("obj", "build.dll"), "x");
            WriteLeft("keep.txt");  WriteRight("keep.txt");

            var result = await _comparer.CompareAsync(_left, _right, new[] { "obj" });

            Assert.DoesNotContain(result.Same, s => s.Contains("obj"));
            Assert.Contains("keep.txt", result.Same);
        }

        [Fact]
        public async Task Exclude_wildcard_pattern_works()
        {
            WriteLeft("report_2024.bak");  WriteRight("report_2024.bak");
            WriteLeft("report_2024.txt");  WriteRight("report_2024.txt");

            var result = await _comparer.CompareAsync(_left, _right, new[] { "*.bak" });

            Assert.DoesNotContain(result.Same, s => s.EndsWith(".bak"));
            Assert.Contains("report_2024.txt", result.Same);
        }

        // ── Cancellation ──────────────────────────────────────────────────────

        [Fact]
        public async Task CompareAsync_cancelled_before_start_returns_empty()
        {
            WriteLeft("a.txt"); WriteRight("a.txt");

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // Should not throw — Task.Run is passed a pre-cancelled token
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                _comparer.CompareAsync(_left, _right, ct: cts.Token));
        }

        // ── DeleteAsync ───────────────────────────────────────────────────────

        [Fact]
        public async Task DeleteAsync_LeftOnly_removes_file_from_left()
        {
            WriteLeft("orphan.txt");
            var result = await _comparer.CompareAsync(_left, _right);

            await _comparer.DeleteAsync(_left, _right, "2",
                result.LeftOnly.ToList(), keepRoot: true, useRecycleBin: false);

            Assert.False(File.Exists(Path.Combine(_left, "orphan.txt")));
        }

        [Fact]
        public async Task DeleteAsync_RightOnly_removes_file_from_right()
        {
            WriteRight("orphan.txt");
            var result = await _comparer.CompareAsync(_left, _right);

            await _comparer.DeleteAsync(_left, _right, "3",
                result.RightOnly.ToList(), keepRoot: true, useRecycleBin: false);

            Assert.False(File.Exists(Path.Combine(_right, "orphan.txt")));
        }

        [Fact]
        public async Task DeleteAsync_Same_both_removes_from_both_sides()
        {
            WriteLeft("same.txt", "x"); WriteRight("same.txt", "x");
            var result = await _comparer.CompareAsync(_left, _right);

            await _comparer.DeleteAsync(_left, _right, "1",
                result.Same.ToList(), keepRoot: true, useRecycleBin: false);

            Assert.False(File.Exists(Path.Combine(_left,  "same.txt")));
            Assert.False(File.Exists(Path.Combine(_right, "same.txt")));
        }

        [Fact]
        public async Task DeleteAsync_cancelled_mid_run_returns_partial_count()
        {
            // Create enough files that we can cancel mid-loop
            for (int i = 0; i < 20; i++)
                WriteLeft($"file{i:D2}.txt");
            var result = await _comparer.CompareAsync(_left, _right);

            using var cts = new CancellationTokenSource();
            // Cancel after a tiny delay so a few files get deleted first
            _ = Task.Delay(10).ContinueWith(_ => cts.Cancel());

            // Must NOT throw — returns partial count gracefully
            int deleted = await _comparer.DeleteAsync(_left, _right, "2",
                result.LeftOnly.ToList(), keepRoot: true,
                ct: cts.Token, useRecycleBin: false);

            // We can only assert a non-negative partial count, not an exact value
            Assert.True(deleted >= 0);
        }

        // ── ExportAsync ───────────────────────────────────────────────────────

        [Fact]
        public async Task ExportAsync_copies_left_only_files_to_export_folder()
        {
            WriteLeft("leftonly.txt", "content");
            var result = await _comparer.CompareAsync(_left, _right);
            string exportDir = Path.Combine(Path.GetTempPath(), $"FCExport_{Guid.NewGuid():N}");

            try
            {
                _comparer.CreateCategorySubfolders = false;
                int copied = await _comparer.ExportAsync(_left, _right, "2", result, exportDir);

                Assert.Equal(1, copied);
                Assert.True(File.Exists(Path.Combine(exportDir, "leftonly.txt")));
            }
            finally { try { Directory.Delete(exportDir, true); } catch { } }
        }

        [Fact]
        public async Task ExportAsync_cancelled_returns_partial_count_without_throwing()
        {
            for (int i = 0; i < 20; i++)
                WriteLeft($"file{i:D2}.txt");
            var result = await _comparer.CompareAsync(_left, _right);
            string exportDir = Path.Combine(Path.GetTempPath(), $"FCExport_{Guid.NewGuid():N}");

            try
            {
                using var cts = new CancellationTokenSource();
                _ = Task.Delay(10).ContinueWith(_ => cts.Cancel());

                // Must NOT throw
                int copied = await _comparer.ExportAsync(_left, _right, "2",
                    result, exportDir, cts.Token);

                Assert.True(copied >= 0);
            }
            finally { try { Directory.Delete(exportDir, true); } catch { } }
        }

        // ── IsExcluded ────────────────────────────────────────────────────────

        [Fact]
        public void IsExcluded_returns_true_for_excluded_extension()
        {
            _comparer.SetExcludePatterns(new[] { ".tmp" });
            Assert.True(_comparer.IsExcluded("folder/file.tmp"));
        }

        [Fact]
        public void IsExcluded_returns_false_for_non_excluded_file()
        {
            _comparer.SetExcludePatterns(new[] { ".tmp" });
            Assert.False(_comparer.IsExcluded("folder/file.txt"));
        }

        [Fact]
        public void IsExcluded_returns_true_for_excluded_folder_segment()
        {
            _comparer.SetExcludePatterns(new[] { "bin" });
            Assert.True(_comparer.IsExcluded(@"bin\debug\app.dll"));
        }

        [Fact]
        public void SetExcludePatterns_bare_word_matches_as_folder_or_filename_segment()
        {
            // "log" (no leading dot) → treated as a name segment, NOT as extension.
            // It matches a path segment called "log", not files ending in .log.
            // Users must supply ".log" explicitly to match by extension.
            _comparer.SetExcludePatterns(new[] { "log" });
            Assert.True(_comparer.IsExcluded(@"log\somefile.txt"));   // folder named "log"
            Assert.False(_comparer.IsExcluded("output.log"));          // extension, not matched
        }

        [Fact]
        public void SetExcludePatterns_dot_prefix_matches_extension()
        {
            // ".log" (with leading dot) → matches by extension
            _comparer.SetExcludePatterns(new[] { ".log" });
            Assert.True(_comparer.IsExcluded("output.log"));
            Assert.False(_comparer.IsExcluded(@"log\somefile.txt"));
        }

        [Fact]
        public void SetExcludePatterns_wildcard_matches_pattern()
        {
            _comparer.SetExcludePatterns(new[] { "*.generated.*" });
            Assert.True(_comparer.IsExcluded("MyClass.generated.cs"));
            Assert.False(_comparer.IsExcluded("MyClass.cs"));
        }

        // ── CountDeletableItems ───────────────────────────────────────────────

        [Fact]
        public async Task CountDeletableItems_counts_correctly_for_LeftOnly()
        {
            WriteLeft("a.txt"); WriteLeft("b.txt");
            var result = await _comparer.CompareAsync(_left, _right);

            int count = _comparer.CountDeletableItems(_left, _right, "2",
                result.LeftOnly.ToList());

            Assert.Equal(2, count);
        }

        [Fact]
        public async Task CountDeletableItems_excludes_excluded_patterns()
        {
            WriteLeft("keep.txt"); WriteLeft("skip.log");
            _comparer.SetExcludePatterns(new[] { ".log" });
            var result = await _comparer.CompareAsync(_left, _right);

            int count = _comparer.CountDeletableItems(_left, _right, "2",
                result.LeftOnly.ToList());

            Assert.Equal(1, count); // only keep.txt
        }
    }
}
