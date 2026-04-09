using System;
using System.Diagnostics;
using System.IO;

namespace FolderComparerUI.Services
{
    /// <summary>Locates and launches Beyond Compare — no UI references.</summary>
    public class BeyondCompareService : IBeyondCompareService
    {
        private static readonly string[] ProductionPaths =
        {
            @"C:\Program Files\Beyond Compare 4\BCompare.exe",
            @"C:\Program Files (x86)\Beyond Compare 4\BCompare.exe",
            @"C:\Program Files\Beyond Compare 3\BCompare.exe",
            @"C:\Program Files (x86)\Beyond Compare 3\BCompare.exe",
        };

        private readonly string[] _defaultPaths;

        /// <summary>Production constructor — uses standard install locations.</summary>
        public BeyondCompareService() : this(ProductionPaths) { }

        /// <summary>
        /// Testable constructor — supply custom paths so unit tests can create
        /// real temp files and verify priority resolution without needing BC installed.
        /// </summary>
        public BeyondCompareService(string[] defaultPaths) => _defaultPaths = defaultPaths;

        /// <summary>
        /// Resolves BCompare.exe. Priority order:
        ///   1. userPath — if non-empty and the file exists, use it immediately
        ///   2. Default paths — standard install locations (or injected paths in tests)
        ///   3. System PATH — via 'where BCompare.exe'
        /// Returns null if not found anywhere.
        /// </summary>
        public string? FindExecutable(string? userPath = null)
        {
            // Priority 1: explicit user-supplied path
            if (!string.IsNullOrWhiteSpace(userPath) && File.Exists(userPath))
                return userPath;

            // Priority 2: well-known / injected install locations
            foreach (var p in _defaultPaths)
                if (File.Exists(p)) return p;

            // Priority 3: system PATH
            try
            {
                var proc = Process.Start(new ProcessStartInfo
                {
                    FileName               = "where",
                    Arguments              = "BCompare.exe",
                    RedirectStandardOutput = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true
                });
                string? line = proc?.StandardOutput.ReadLine();
                proc?.WaitForExit();
                if (!string.IsNullOrEmpty(line) && File.Exists(line)) return line;
            }
            catch { }

            return null;
        }

        public void OpenFile(string bcPath, string filePath)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName        = bcPath,
                Arguments       = $"\"{filePath}\"",
                UseShellExecute = true
            });
        }

        public void CompareFiles(string bcPath, string leftPath, string rightPath)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName        = bcPath,
                Arguments       = $"\"{leftPath}\" \"{rightPath}\"",
                UseShellExecute = true
            });
        }
    }
}
