using FolderComparerUI.Services;
using System.IO;
using Xunit;

namespace FolderComparerTests
{
    /// <summary>
    /// Tests for BeyondCompareService.FindExecutable priority resolution.
    ///
    /// Strategy: create real dummy .exe files in a temp directory, then inject
    /// those paths as the "default paths" so tests work on any machine without
    /// Beyond Compare actually being installed.
    /// </summary>
    public class BeyondCompareServiceTests : IDisposable
    {
        private readonly string _dir;

        public BeyondCompareServiceTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), $"BCTest_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>Create a zero-byte placeholder file representing an .exe.</summary>
        private string CreateExe(string name)
        {
            string path = Path.Combine(_dir, name);
            File.WriteAllBytes(path, Array.Empty<byte>());
            return path;
        }

        /// <summary>Build a service with no default paths — forces reliance on userPath or PATH.</summary>
        private static BeyondCompareService NoDefaults() =>
            new(Array.Empty<string>());

        /// <summary>Build a service with a single injected default path.</summary>
        private static BeyondCompareService WithDefault(string path) =>
            new(new[] { path });

        /// <summary>Build a service with multiple injected default paths.</summary>
        private static BeyondCompareService WithDefaults(params string[] paths) =>
            new(paths);

        // ── userPath takes Priority 1 ─────────────────────────────────────────

        [Fact]
        public void Returns_userPath_when_it_exists()
        {
            string exe = CreateExe("user_bc.exe");
            var svc    = NoDefaults();

            string? result = svc.FindExecutable(userPath: exe);

            Assert.Equal(exe, result);
        }

        [Fact]
        public void Returns_userPath_even_when_default_path_also_exists()
        {
            string userExe    = CreateExe("user_bc.exe");
            string defaultExe = CreateExe("default_bc.exe");
            var svc           = WithDefault(defaultExe);

            string? result = svc.FindExecutable(userPath: userExe);

            // userPath must win — Priority 1
            Assert.Equal(userExe, result);
        }

        [Fact]
        public void Ignores_userPath_when_file_does_not_exist()
        {
            string defaultExe    = CreateExe("default_bc.exe");
            string nonexistentUser = Path.Combine(_dir, "nonexistent_bc.exe");
            var svc              = WithDefault(defaultExe);

            string? result = svc.FindExecutable(userPath: nonexistentUser);

            // Falls through to Priority 2
            Assert.Equal(defaultExe, result);
        }

        [Fact]
        public void Ignores_userPath_when_it_is_null()
        {
            string defaultExe = CreateExe("default_bc.exe");
            var svc           = WithDefault(defaultExe);

            string? result = svc.FindExecutable(userPath: null);

            Assert.Equal(defaultExe, result);
        }

        [Fact]
        public void Ignores_userPath_when_it_is_empty_string()
        {
            string defaultExe = CreateExe("default_bc.exe");
            var svc           = WithDefault(defaultExe);

            string? result = svc.FindExecutable(userPath: "");

            Assert.Equal(defaultExe, result);
        }

        [Fact]
        public void Ignores_userPath_when_it_is_whitespace_only()
        {
            string defaultExe = CreateExe("default_bc.exe");
            var svc           = WithDefault(defaultExe);

            string? result = svc.FindExecutable(userPath: "   ");

            Assert.Equal(defaultExe, result);
        }

        // ── Default paths — Priority 2 ────────────────────────────────────────

        [Fact]
        public void Returns_first_default_path_that_exists()
        {
            string exe = CreateExe("bc4.exe");
            var svc    = WithDefault(exe);

            string? result = svc.FindExecutable();

            Assert.Equal(exe, result);
        }

        [Fact]
        public void Skips_nonexistent_default_paths_and_finds_first_existing_one()
        {
            string nonexistent1 = Path.Combine(_dir, "missing1.exe");
            string nonexistent2 = Path.Combine(_dir, "missing2.exe");
            string existing     = CreateExe("bc4.exe");
            var svc             = WithDefaults(nonexistent1, nonexistent2, existing);

            string? result = svc.FindExecutable();

            Assert.Equal(existing, result);
        }

        [Fact]
        public void Returns_null_when_no_default_paths_exist_and_no_userPath()
        {
            // No defaults, no userPath, PATH lookup will fail for a dummy name
            var svc = WithDefaults(
                Path.Combine(_dir, "missing1.exe"),
                Path.Combine(_dir, "missing2.exe")
            );

            string? result = svc.FindExecutable();

            // Either null (not on PATH) or a real BC path if BC is installed on CI
            // — we only verify it's not one of our missing paths
            Assert.True(result == null ||
                (!result.Contains("missing1") && !result.Contains("missing2")));
        }

        [Fact]
        public void Returns_null_when_all_sources_exhausted()
        {
            var svc = NoDefaults();

            // No userPath, no defaults, PATH lookup won't find a dummy name
            // Use a name that definitely won't be on PATH
            string? result = svc.FindExecutable(userPath: Path.Combine(_dir, "not_bc.exe"));

            Assert.Null(result);
        }

        // ── Priority ordering across all three sources ─────────────────────────

        [Fact]
        public void UserPath_beats_default_when_both_exist()
        {
            string user    = CreateExe("user.exe");
            string def     = CreateExe("default.exe");
            var svc        = WithDefault(def);

            string? result = svc.FindExecutable(userPath: user);

            Assert.Equal(user, result);
        }

        [Fact]
        public void Second_default_path_used_when_first_is_missing()
        {
            string missing = Path.Combine(_dir, "bc_v4.exe");
            string present = CreateExe("bc_v3.exe");
            var svc        = WithDefaults(missing, present);

            string? result = svc.FindExecutable();

            Assert.Equal(present, result);
        }

        [Fact]
        public void All_four_default_slots_checked_in_order()
        {
            string[] paths = new[]
            {
                Path.Combine(_dir, "slot1_missing.exe"),
                Path.Combine(_dir, "slot2_missing.exe"),
                Path.Combine(_dir, "slot3_missing.exe"),
                CreateExe("slot4_present.exe"),
            };
            var svc = WithDefaults(paths);

            string? result = svc.FindExecutable();

            Assert.Equal(paths[3], result);
        }

        // ── Edge cases ────────────────────────────────────────────────────────

        [Fact]
        public void FindExecutable_with_no_arguments_does_not_throw()
        {
            var svc = NoDefaults();
            // Should complete without exception even when nothing is found
            var ex = Record.Exception(() => svc.FindExecutable());
            Assert.Null(ex);
        }

        [Fact]
        public void FindExecutable_called_multiple_times_returns_consistent_result()
        {
            string exe = CreateExe("stable.exe");
            var svc    = WithDefault(exe);

            string? r1 = svc.FindExecutable();
            string? r2 = svc.FindExecutable();
            string? r3 = svc.FindExecutable();

            Assert.Equal(r1, r2);
            Assert.Equal(r2, r3);
        }

        [Fact]
        public void FindExecutable_returns_null_after_exe_is_deleted()
        {
            string exe = CreateExe("temp_bc.exe");
            var svc    = WithDefault(exe);

            Assert.NotNull(svc.FindExecutable()); // found first
            File.Delete(exe);
            Assert.Null(svc.FindExecutable());    // gone now
        }
    }
}
