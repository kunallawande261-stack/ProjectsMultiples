using System;
using System.Diagnostics;
using System.IO;

namespace FolderComparerUI.Services
{
    /// <summary>Locates and launches Beyond Compare — no UI references.</summary>
    public class BeyondCompareService
    {
        private static readonly string[] DefaultPaths =
        {
            @"C:\Program Files\Beyond Compare 4\BCompare.exe",
            @"C:\Program Files (x86)\Beyond Compare 4\BCompare.exe",
            @"C:\Program Files\Beyond Compare 3\BCompare.exe",
            @"C:\Program Files (x86)\Beyond Compare 3\BCompare.exe",
        };

        /// <summary>
        /// Resolves BCompare.exe. Priority:
        /// 1. userPath (if set and exists)
        /// 2. Hardcoded well-known paths
        /// 3. System PATH via 'where'
        /// </summary>
        public string? FindExecutable(string? userPath = null)
        {
            if (!string.IsNullOrWhiteSpace(userPath) && File.Exists(userPath))
                return userPath;

            foreach (var p in DefaultPaths)
                if (File.Exists(p)) return p;

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
