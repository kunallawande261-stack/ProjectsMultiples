using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace FolderComparerLib
{
    // ── FIX: Changed List<string> to HashSet<string> so Contains() is O(1) not O(n)
    public class CompareResult
    {
        public HashSet<string> Same      { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> LeftOnly  { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> RightOnly { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Different { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public class ProgressInfo
    {
        public int     Processed { get; set; }
        public int     Total     { get; set; }
        public string? Message   { get; set; }
    }

    public class FolderComparer
    {
        public bool    EnableLogging          { get; set; } = false;
        public string? LogFilePath            { get; set; }
        public bool    SmartXmlCompare        { get; set; } = false;
        public int     ProgressInterval       { get; set; } = 200;
        public bool    CreateCategorySubfolders { get; set; } = true;

        private readonly FileHashCache _hashCache = new();

        public event Action<ProgressInfo>? ProgressChanged;
        public event Action<string>?       LogMessage;

        // ── Unified exclude pattern storage ─────────────────────────────────────
        private readonly HashSet<string> _extPatterns  = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _namePatterns = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<Regex>     _wildcardRegexes = new();
        private readonly object          _patternsLock = new();

        public void Log(string message)
        {
            if (!EnableLogging) return;
            LogMessage?.Invoke(message);
            try
            {
                if (string.IsNullOrEmpty(LogFilePath))
                    LogFilePath = $"CompareLog_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
                File.AppendAllText(LogFilePath, $"[{DateTime.Now}] {message}{Environment.NewLine}");
            }
            catch { }
        }

        // ── FIX: Single normalisation method used by both SetExcludePatterns and CompareAsync ──
        private static void NormalizePatterns(
            IEnumerable<string>? raw,
            HashSet<string>      extSet,
            HashSet<string>      nameSet,
            List<Regex>          wildcardList)
        {
            extSet.Clear();
            nameSet.Clear();
            wildcardList.Clear();

            foreach (var pRaw in (raw ?? Enumerable.Empty<string>())
                         .Select(p => (p ?? string.Empty).Trim())
                         .Where(p => !string.IsNullOrEmpty(p))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var p = pRaw.ToLowerInvariant();

                if (p.Contains('*') || p.Contains('?'))
                {
                    var rx = "^" + Regex.Escape(p).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
                    try   { wildcardList.Add(new Regex(rx, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)); }
                    catch { nameSet.Add(p); }
                    continue;
                }

                if (p.StartsWith('.'))                                    { extSet.Add(p); continue; }
                if (p.Length <= 5 && p.All(char.IsLetterOrDigit)) { extSet.Add("." + p); }
                else                                                       { nameSet.Add(p); }
            }
        }

        /// <summary>
        /// Persists exclude patterns on the instance so ExportAsync / DeleteAsync / CountDeletableItems
        /// all use the same rules as the most recent CompareAsync call.
        /// </summary>
        public void SetExcludePatterns(IEnumerable<string>? excludePatterns)
        {
            lock (_patternsLock)
                NormalizePatterns(excludePatterns, _extPatterns, _namePatterns, _wildcardRegexes);
        }

        private bool IsExcludedByPatterns(string rel)
        {
            lock (_patternsLock)
            {
                if (_extPatterns.Count == 0 && _namePatterns.Count == 0 && _wildcardRegexes.Count == 0)
                    return false;

                var relLower = rel.Replace('\\', '/').ToLowerInvariant();
                var fileName = Path.GetFileName(relLower);

                foreach (var rx in _wildcardRegexes)
                    if (rx.IsMatch(relLower) || rx.IsMatch(fileName)) return true;

                var ext = Path.GetExtension(relLower);
                if (!string.IsNullOrEmpty(ext) && _extPatterns.Contains(ext)) return true;

                foreach (var seg in relLower.Split('/'))
                    if (_namePatterns.Contains(seg)) return true;

                return false;
            }
        }

        /// <summary>Public wrapper so the UI can test exclusion against current patterns.</summary>
        public bool IsExcluded(string relativePath) => IsExcludedByPatterns(relativePath);

        // ────────────────────────────────────────────────────────────────────────
        //  CompareAsync
        //  FIX: Now calls SetExcludePatterns so the instance patterns are always
        //  in sync with whatever the UI passed for analysis.
        // ────────────────────────────────────────────────────────────────────────
        public async Task<CompareResult> CompareAsync(
            string                    leftRoot,
            string                    rightRoot,
            IEnumerable<string>?      excludePatterns = null,
            CancellationToken         ct              = default)
        {
            // ── FIX: persist patterns so Delete/Export/Count use the same rules ──
            SetExcludePatterns(excludePatterns);

            // FIX: clear hash cache after each run so stale entries don't accumulate
            _hashCache.Clear();

            return await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                var res = new CompareResult();

                var leftMap  = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var rightMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (var f in Directory.EnumerateFiles(leftRoot,  "*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(leftRoot, f);
                    if (!IsExcludedByPatterns(rel)) leftMap[rel] = f;
                }

                foreach (var f in Directory.EnumerateFiles(rightRoot, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(rightRoot, f);
                    if (!IsExcludedByPatterns(rel)) rightMap[rel] = f;
                }

                int total     = leftMap.Count;
                int processed = 0;

                var options = new ParallelOptions
                {
                    CancellationToken        = ct,
                    MaxDegreeOfParallelism   = Environment.ProcessorCount
                };

                Parallel.ForEach(leftMap, options, kv =>
                {
                    string rel      = kv.Key;
                    string leftPath = kv.Value;

                    if (rightMap.TryGetValue(rel, out string? rightPath))
                    {
                        bool same = CompareFileSmartIfApplicable(leftPath, rightPath, rel);
                        lock (res) { if (same) res.Same.Add(rel); else res.Different.Add(rel); }
                    }
                    else
                    {
                        lock (res) { res.LeftOnly.Add(rel); }
                    }

                    int current = Interlocked.Increment(ref processed);
                    if (current % ProgressInterval == 0 || current == total)
                        ProgressChanged?.Invoke(new ProgressInfo
                        {
                            Processed = current,
                            Total     = total,
                            Message   = $"Processed {current}/{total} left files..."
                        });
                });

                foreach (var kv in rightMap)
                    if (!leftMap.ContainsKey(kv.Key))
                        res.RightOnly.Add(kv.Key);

                Log($"Compared: Left='{leftRoot}', Right='{rightRoot}'");
                Log($"Totals — Left:{leftMap.Count} Right:{rightMap.Count} Same:{res.Same.Count} Diff:{res.Different.Count} L-only:{res.LeftOnly.Count} R-only:{res.RightOnly.Count}");
                ProgressChanged?.Invoke(new ProgressInfo { Processed = total, Total = total, Message = "Compare complete" });

                return res;
            }, ct);
        }

        private bool CompareFileSmartIfApplicable(string leftPath, string rightPath, string rel)
        {
            var fi1 = new FileInfo(leftPath);
            var fi2 = new FileInfo(rightPath);

            // Fast path: identical size + timestamp → skip hashing
            if (fi1.Length == fi2.Length && fi1.LastWriteTimeUtc == fi2.LastWriteTimeUtc)
                return true;

            string ext      = Path.GetExtension(leftPath).ToLowerInvariant();
            string ext2     = Path.GetExtension(rightPath).ToLowerInvariant();
            bool bothXmlLike = (ext == ".aml" || ext == ".xml" || ext == ".svg") &&
                               (ext2 == ".aml" || ext2 == ".xml" || ext2 == ".svg");

            if (SmartXmlCompare && bothXmlLike)
            {
                try
                {
                    bool eq = AreXmlFilesEquivalent(leftPath, rightPath);
                    Log($"COMPARE (SMART XML) {rel}: {(eq ? "EQUAL" : "DIFFER")}");
                    return eq;
                }
                catch (Exception ex)
                {
                    Log($"SMART XML compare failed for {rel}: {ex.Message} — fallback to binary");
                }
            }

            bool equal = FilesAreEqualBinary(leftPath, rightPath);
            Log($"COMPARE (BINARY) {rel}: {(equal ? "EQUAL" : "DIFFER")}");
            return equal;
        }

        private static bool AreXmlFilesEquivalent(string path1, string path2)
        {
            var doc1 = XDocument.Load(path1, LoadOptions.PreserveWhitespace);
            var doc2 = XDocument.Load(path2, LoadOptions.PreserveWhitespace);
            return NormalizeXmlElement(doc1.Root!) == NormalizeXmlElement(doc2.Root!);
        }

        // ── FIX: Iterative XML normaliser — no stack overflow on deeply nested XML ──
        private static string NormalizeXmlElement(XElement root)
        {
            var sb    = new StringBuilder();
            var stack = new Stack<(XElement el, bool closing)>();
            stack.Push((root, false));

            while (stack.Count > 0)
            {
                var (el, closing) = stack.Pop();

                if (closing)
                {
                    sb.Append($"</{el.Name.LocalName}>");
                    continue;
                }

                sb.Append($"<{el.Name.LocalName}");
                foreach (var a in el.Attributes().OrderBy(a => a.Name.ToString(), StringComparer.Ordinal))
                    sb.Append($" {a.Name}=\"{a.Value.Trim()}\"");
                sb.Append('>');

                string text = (el.Nodes().OfType<XText>().Select(t => t.Value).FirstOrDefault() ?? "").Trim();
                if (!string.IsNullOrEmpty(text))
                    sb.Append(text);

                // Push closing tag first, then children in reverse so they pop in order
                stack.Push((el, true));
                foreach (var child in el.Elements().Reverse())
                    stack.Push((child, false));
            }

            return sb.ToString();
        }

        private bool FilesAreEqualBinary(string f1, string f2) =>
            _hashCache.GetHash(f1) == _hashCache.GetHash(f2);

        // ────────────────────────────────────────────────────────────────────────
        //  ExportAsync
        //  Choices: "1" Same(L) | "1L" Same-Left | "1R" Same-Right
        //           "2" LeftOnly | "3" RightOnly
        //           "4" Different(both) | "4L" Diff-Left | "4R" Diff-Right
        //           "5" All | "CL" Copy entire Left | "CR" Copy entire Right
        // ────────────────────────────────────────────────────────────────────────
        public async Task<int> ExportAsync(
            string            leftRoot,
            string            rightRoot,
            string            choice,
            CompareResult     res,
            string            exportRoot,
            CancellationToken ct = default)
        {
            return await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                int copied    = 0;
                int processed = 0;

                // Whole-folder copy (no analysis result needed)
                if (choice == "CL" || choice == "CR")
                {
                    string srcRoot = choice == "CL" ? leftRoot : rightRoot;
                    string label   = choice == "CL" ? "LeftFolder" : "RightFolder";
                    var allFiles   = Directory.EnumerateFiles(srcRoot, "*", SearchOption.AllDirectories).ToList();
                    int total2     = allFiles.Count;

                    foreach (var fullPath in allFiles)
                    {
                        ct.ThrowIfCancellationRequested();
                        processed++;
                        var rel = Path.GetRelativePath(srcRoot, fullPath);
                        if (IsExcludedByPatterns(rel)) continue;
                        try
                        {
                            string dest = CreateCategorySubfolders
                                ? Path.Combine(exportRoot, label, rel)
                                : Path.Combine(exportRoot, rel);
                            copied += SafeCopy(fullPath, dest);
                        }
                        catch (Exception ex) { Log($"Error exporting {rel}: {ex.Message}"); }

                        if (processed % ProgressInterval == 0 || processed == total2)
                            ProgressChanged?.Invoke(new ProgressInfo { Processed = processed, Total = total2, Message = $"Exporting {processed}/{total2}..." });
                    }

                    Log($"Exported total: {copied}");
                    return copied;
                }

                // Result-set based export
                IEnumerable<string> toExport = choice switch
                {
                    "1" or "1L" or "1R" => res.Same,
                    "2"                  => res.LeftOnly,
                    "3"                  => res.RightOnly,
                    "4" or "4L" or "4R" => res.Different,
                    _                    => res.Same.Concat(res.LeftOnly).Concat(res.RightOnly).Concat(res.Different)
                };

                var exportList = toExport.ToList();
                int total      = exportList.Count;

                foreach (var rel in exportList)
                {
                    ct.ThrowIfCancellationRequested();
                    processed++;
                    if (IsExcludedByPatterns(rel)) continue;

                    try
                    {
                        string src, dest;
                        switch (choice)
                        {
                            case "4":
                                if (File.Exists(src = Path.Combine(leftRoot,  rel)))
                                    copied += SafeCopy(src, CreateCategorySubfolders ? Path.Combine(exportRoot, "Diff", "Left",  rel) : Path.Combine(exportRoot, rel));
                                if (File.Exists(src = Path.Combine(rightRoot, rel)))
                                    copied += SafeCopy(src, CreateCategorySubfolders ? Path.Combine(exportRoot, "Diff", "Right", rel) : Path.Combine(exportRoot, rel));
                                break;
                            case "4L":
                                src  = Path.Combine(leftRoot,  rel);
                                dest = CreateCategorySubfolders ? Path.Combine(exportRoot, "Diff_Left",  rel) : Path.Combine(exportRoot, rel);
                                if (File.Exists(src)) copied += SafeCopy(src, dest);
                                break;
                            case "4R":
                                src  = Path.Combine(rightRoot, rel);
                                dest = CreateCategorySubfolders ? Path.Combine(exportRoot, "Diff_Right", rel) : Path.Combine(exportRoot, rel);
                                if (File.Exists(src)) copied += SafeCopy(src, dest);
                                break;
                            case "1L":
                                src  = Path.Combine(leftRoot,  rel);
                                dest = CreateCategorySubfolders ? Path.Combine(exportRoot, "Same_Left",  rel) : Path.Combine(exportRoot, rel);
                                if (File.Exists(src)) copied += SafeCopy(src, dest);
                                break;
                            case "1R":
                                src  = Path.Combine(rightRoot, rel);
                                dest = CreateCategorySubfolders ? Path.Combine(exportRoot, "Same_Right", rel) : Path.Combine(exportRoot, rel);
                                if (File.Exists(src)) copied += SafeCopy(src, dest);
                                break;
                            case "3":
                                src  = Path.Combine(rightRoot, rel);
                                dest = CreateCategorySubfolders ? Path.Combine(exportRoot, "RightOnly",  rel) : Path.Combine(exportRoot, rel);
                                if (File.Exists(src)) copied += SafeCopy(src, dest);
                                break;
                            default: // "1" Same, "2" LeftOnly, "5" All
                                src = Path.Combine(leftRoot, rel);
                                string prefix = choice == "2" ? "LeftOnly" : choice == "1" ? "Same" : "All";
                                dest = CreateCategorySubfolders ? Path.Combine(exportRoot, prefix, rel) : Path.Combine(exportRoot, rel);
                                if (File.Exists(src)) copied += SafeCopy(src, dest);
                                break;
                        }
                    }
                    catch (Exception ex) { Log($"Error exporting {rel}: {ex.Message}"); }

                    if (processed % ProgressInterval == 0 || processed == total)
                        ProgressChanged?.Invoke(new ProgressInfo { Processed = processed, Total = total, Message = $"Exported {processed}/{total} items..." });
                }

                Log($"Exported total: {copied}");
                return copied;
            }, ct);
        }

        private static int SafeCopy(string src, string dest)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(src, dest, true);
                return 1;
            }
            catch { return 0; }
        }

        // ────────────────────────────────────────────────────────────────────────
        //  DeleteAsync
        //  Choices: "1" Same-both | "1L" Same-Left | "1R" Same-Right
        //           "2" LeftOnly | "3" RightOnly
        //           "4" Diff-both | "4L" Diff-Left | "4R" Diff-Right | "5" All
        // ────────────────────────────────────────────────────────────────────────
        /// <summary>
        /// Delete choices:
        ///   "1"  = Same – both sides  | "1L" Same-Left | "1R" Same-Right
        ///   "2"  = Left-only          | "3"  Right-only
        ///   "4"  = Different – both   | "4L" Diff-Left | "4R" Diff-Right
        ///   "5"  = All sets
        /// useRecycleBin: when true, files go to the Recycle Bin instead of permanent deletion.
        /// </summary>
        public async Task<int> DeleteAsync(
            string            leftRoot,
            string            rightRoot,
            string            choice,
            List<string>      targets,
            bool              keepRoot,
            CancellationToken ct            = default,
            bool              useRecycleBin = false)
        {
            return await Task.Run(() =>
            {
                int deleted   = 0;
                int processed = 0;
                int total     = targets.Count;

                foreach (var rel in targets)
                {
                    ct.ThrowIfCancellationRequested();
                    processed++;

                    if (IsExcludedByPatterns(rel)) { Log($"Skipping excluded: {rel}"); continue; }

                    try
                    {
                        switch (choice)
                        {
                            case "1":  deleted += Del(leftRoot, rel, useRecycleBin) + Del(rightRoot, rel, useRecycleBin); break;
                            case "1L": deleted += Del(leftRoot,  rel, useRecycleBin); break;
                            case "1R": deleted += Del(rightRoot, rel, useRecycleBin); break;
                            case "2":  deleted += Del(leftRoot,  rel, useRecycleBin); break;
                            case "3":  deleted += Del(rightRoot, rel, useRecycleBin); break;
                            case "4":  deleted += Del(leftRoot, rel, useRecycleBin) + Del(rightRoot, rel, useRecycleBin); break;
                            case "4L": deleted += Del(leftRoot,  rel, useRecycleBin); break;
                            case "4R": deleted += Del(rightRoot, rel, useRecycleBin); break;
                            case "5":  deleted += Del(leftRoot, rel, useRecycleBin) + Del(rightRoot, rel, useRecycleBin); break;
                        }
                        Log($"Deleted [{choice}]: {rel}");
                    }
                    catch (Exception ex) { Log($"Error deleting {rel}: {ex.Message}"); }

                    if (processed % ProgressInterval == 0 || processed == total)
                        ProgressChanged?.Invoke(new ProgressInfo { Processed = processed, Total = total, Message = $"Deleting {processed}/{total}..." });
                }

                if (!keepRoot)
                {
                    try { DeleteEmptyFolders(leftRoot,  true); } catch { }
                    try { DeleteEmptyFolders(rightRoot, true); } catch { }
                }

                return deleted;
            }, ct);
        }

        private static int Del(string root, string rel, bool useRecycleBin)
        {
            string path = Path.Combine(root, rel);
            try
            {
                if (!File.Exists(path)) return 0;
                if (useRecycleBin)
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                        path,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                        Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                else
                    File.Delete(path);
                return 1;
            }
            catch { return 0; }
        }

        public int CountDeletableItems(string leftRoot, string rightRoot, string choice, List<string> targets)
        {
            int count = 0;
            foreach (var rel in targets)
            {
                if (IsExcludedByPatterns(rel)) continue;
                switch (choice)
                {
                    case "1":  if (File.Exists(Path.Combine(leftRoot,  rel))) count++;
                               if (File.Exists(Path.Combine(rightRoot, rel))) count++; break;
                    case "1L": if (File.Exists(Path.Combine(leftRoot,  rel))) count++; break;
                    case "1R": if (File.Exists(Path.Combine(rightRoot, rel))) count++; break;
                    case "2":  if (File.Exists(Path.Combine(leftRoot,  rel))) count++; break;
                    case "3":  if (File.Exists(Path.Combine(rightRoot, rel))) count++; break;
                    case "4":  if (File.Exists(Path.Combine(leftRoot,  rel))) count++;
                               if (File.Exists(Path.Combine(rightRoot, rel))) count++; break;
                    case "4L": if (File.Exists(Path.Combine(leftRoot,  rel))) count++; break;
                    case "4R": if (File.Exists(Path.Combine(rightRoot, rel))) count++; break;
                    case "5":  if (File.Exists(Path.Combine(leftRoot,  rel))) count++;
                               if (File.Exists(Path.Combine(rightRoot, rel))) count++; break;
                }
            }
            return count;
        }

        public static void DeleteEmptyFolders(string root, bool keepRoot)
        {
            try
            {
                foreach (var dir in Directory.GetDirectories(root, "*", SearchOption.AllDirectories)
                                             .OrderByDescending(d => d.Length))
                {
                    try
                    {
                        if (Directory.Exists(dir) &&
                            !Directory.EnumerateFileSystemEntries(dir).Any())
                            Directory.Delete(dir, false);
                    }
                    catch { }
                }

                if (!keepRoot)
                    try
                    {
                        if (Directory.Exists(root) &&
                            !Directory.EnumerateFileSystemEntries(root).Any())
                            Directory.Delete(root, false);
                    }
                    catch { }
            }
            catch { }
        }
    }
}
