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
    // ── #1: IReadOnlySet — callers cannot mutate the result sets ─────────────
    public class CompareResult
    {
        private readonly HashSet<string> _same      = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _leftOnly  = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _rightOnly = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _different = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlySet<string> Same      => _same;
        public IReadOnlySet<string> LeftOnly  => _leftOnly;
        public IReadOnlySet<string> RightOnly => _rightOnly;
        public IReadOnlySet<string> Different => _different;

        internal void AddSame(string rel)      => _same.Add(rel);
        internal void AddLeftOnly(string rel)  => _leftOnly.Add(rel);
        internal void AddRightOnly(string rel) => _rightOnly.Add(rel);
        internal void AddDifferent(string rel) => _different.Add(rel);
    }

    /// <summary>
    /// Controls how two files are compared when they exist on both sides.
    ///
    /// Normal    — Size differs → Different immediately (zero I/O).
    ///             Size same    → hash both and compare.
    ///             Best for most folders. Fast on reload because unchanged
    ///             same-size files return a cached hash instantly.
    ///             Recommended for Aras AML / codetree (use with SmartXmlCompare).
    ///
    /// HashOnly  — Always hash both files regardless of size.
    ///             Catches the rare case where different content produces the
    ///             same byte count. Slower first run, same cache benefit on reload.
    ///             Use when correctness matters more than first-run speed.
    ///
    /// SmartXml is orthogonal — it applies on top of either mode for XML files.
    /// </summary>
    public enum CompareMode
    {
        Normal,    // size short-circuit + hash
        HashOnly   // always hash, ignore size
    }

    public class ProgressInfo
    {
        public int     Processed { get; set; }
        public int     Total     { get; set; }
        public string? Message   { get; set; }
    }

    public class FolderComparer
    {
        public bool        EnableLogging            { get; set; } = false;
        public string?     LogFilePath              { get; set; }
        public bool        SmartXmlCompare          { get; set; } = false;
        public CompareMode CompareMode              { get; set; } = CompareMode.Normal;
        public bool        CreateCategorySubfolders { get; set; } = true;

        private readonly FileHashCache _hashCache = new();

        public event Action<ProgressInfo>? ProgressChanged;
        public event Action<string>?       LogMessage;

        private readonly HashSet<string> _extPatterns     = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _namePatterns    = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<Regex>     _wildcardRegexes = new();
        private readonly object          _patternsLock    = new();

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

        private static void NormalizePatterns(
            IEnumerable<string>? raw,
            HashSet<string>      extSet,
            HashSet<string>      nameSet,
            List<Regex>          wildcardList)
        {
            extSet.Clear(); nameSet.Clear(); wildcardList.Clear();
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
                if (p.StartsWith('.')) { extSet.Add(p); continue; }
                // Everything else (folder names, file names, short tokens) → nameSet.
                // We never auto-promote to extension — user must supply the dot explicitly.
                nameSet.Add(p);
            }
        }

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

        public bool IsExcluded(string relativePath) => IsExcludedByPatterns(relativePath);

        /// <summary>
        /// Walks a directory tree and populates <paramref name="map"/> with
        /// relative→full path entries. Silently skips any subfolder that throws
        /// UnauthorizedAccessException or IOException (e.g. protected system
        /// folders, junctions pointing to inaccessible locations) so that one
        /// denied directory does not abort the entire scan.
        /// </summary>
        private void EnumerateSafe(
            string root,
            Dictionary<string, string> map,
            string rootForRelative,
            string side)
        {
            var dirs = new Stack<string>();
            dirs.Push(root);

            while (dirs.Count > 0)
            {
                string dir = dirs.Pop();
                try
                {
                    foreach (var f in Directory.EnumerateFiles(dir, "*"))
                    {
                        var rel = Path.GetRelativePath(rootForRelative, f);
                        if (!IsExcludedByPatterns(rel))
                            map[rel] = f;
                    }
                    foreach (var sub in Directory.EnumerateDirectories(dir))
                        dirs.Push(sub);
                }
                catch (UnauthorizedAccessException ex)
                {
                    Log($"Access denied scanning {side} subfolder '{dir}': {ex.Message}");
                }
                catch (IOException ex)
                {
                    Log($"I/O error scanning {side} subfolder '{dir}': {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Clears the file hash cache. Call this when the left/right folder paths
        /// change so stale entries from the previous folder pair are not reused.
        /// Do NOT call between successive compares of the same folders.
        /// </summary>
        public void ClearHashCache() => _hashCache.Clear();

        // ── CompareAsync ─────────────────────────────────────────────────────
        public async Task<CompareResult> CompareAsync(
            string               leftRoot,
            string               rightRoot,
            IEnumerable<string>? excludePatterns = null,
            CancellationToken    ct              = default)
        {
            // Only overwrite patterns when the caller explicitly provides them.
            if (excludePatterns != null)
                SetExcludePatterns(excludePatterns);

            // Cache persists between runs — entries are invalidated automatically
            // when size OR LastWriteTimeUtc changes. Only truly modified files
            // are rehashed. Call ClearHashCache() only when ForceReread is needed.

            return await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                var res = new CompareResult();

                var leftMap  = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var rightMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                // EnumerateFiles can throw UnauthorizedAccessException on protected
                // subfolders. We enumerate manually so one denied subfolder does not
                // abort the entire scan — it is logged and skipped.
                EnumerateSafe(leftRoot,  leftMap,  leftRoot,  "left");
                EnumerateSafe(rightRoot, rightMap, rightRoot, "right");

                int total     = leftMap.Count;
                int processed = 0;
                // #2 — Dynamic interval: always ~50 progress events regardless of folder size
                int interval  = Math.Max(1, Math.Min(200, total == 0 ? 1 : total / 50));

                var options = new ParallelOptions
                {
                    CancellationToken      = ct,
                    MaxDegreeOfParallelism = Environment.ProcessorCount
                };

                Parallel.ForEach(leftMap, options, kv =>
                {
                    string rel      = kv.Key;
                    string leftPath = kv.Value;
                    if (rightMap.TryGetValue(rel, out string? rightPath))
                    {
                        bool same = CompareFileSmartIfApplicable(leftPath, rightPath, rel);
                        lock (res) { if (same) res.AddSame(rel); else res.AddDifferent(rel); }
                    }
                    else { lock (res) { res.AddLeftOnly(rel); } }

                    int current = Interlocked.Increment(ref processed);
                    if (current % interval == 0 || current == total)
                        ProgressChanged?.Invoke(new ProgressInfo
                        {
                            Processed = current, Total = total,
                            Message   = $"Processed {current}/{total} left files..."
                        });
                });

                foreach (var kv in rightMap)
                    if (!leftMap.ContainsKey(kv.Key)) res.AddRightOnly(kv.Key);

                Log($"Compared: Left='{leftRoot}', Right='{rightRoot}'");
                Log($"Totals — Same:{res.Same.Count} Diff:{res.Different.Count} L-only:{res.LeftOnly.Count} R-only:{res.RightOnly.Count}");
                ProgressChanged?.Invoke(new ProgressInfo { Processed = total, Total = total, Message = "Compare complete" });
                return res;
            }, ct);
        }

        private bool CompareFileSmartIfApplicable(string leftPath, string rightPath, string rel)
        {
            // ── Smart XML takes priority for XML-family files ─────────────────
            if (SmartXmlCompare)
            {
                string ext  = Path.GetExtension(leftPath).ToLowerInvariant();
                string ext2 = Path.GetExtension(rightPath).ToLowerInvariant();
                bool bothXml = (ext == ".aml" || ext == ".xml" || ext == ".svg") &&
                               (ext2 == ".aml" || ext2 == ".xml" || ext2 == ".svg");
                if (bothXml)
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
            }

            // ── Binary comparison — mode controls size short-circuit ──────────
            return CompareMode switch
            {
                // Normal (Option 4):
                //   Size differs  → Different immediately, zero hash I/O
                //   Size same     → hash both (cache keyed by size + LastWriteTimeUtc)
                CompareMode.Normal => CompareNormal(leftPath, rightPath, rel),

                // HashOnly:
                //   Always hash both files regardless of size.
                //   Catches content changes that happen to preserve byte count.
                CompareMode.HashOnly => CompareHashOnly(leftPath, rightPath, rel),

                _ => CompareNormal(leftPath, rightPath, rel)
            };
        }

        private bool CompareNormal(string leftPath, string rightPath, string rel)
        {
            var fi1 = new FileInfo(leftPath);
            var fi2 = new FileInfo(rightPath);

            // Size short-circuit — different sizes → Different with zero I/O
            if (fi1.Length != fi2.Length)
            {
                Log($"COMPARE (NORMAL) {rel}: DIFFER (size {fi1.Length} vs {fi2.Length})");
                return false;
            }

            // Same size → hash both. Pass the FileInfo we already read so the
            // cache doesn't need to stat the file a second time.
            bool equal = _hashCache.GetHash(leftPath,  fi1)
                      == _hashCache.GetHash(rightPath, fi2);
            Log($"COMPARE (NORMAL) {rel}: {(equal ? "EQUAL" : "DIFFER")} (size match, hash checked)");
            return equal;
        }

        private bool CompareHashOnly(string leftPath, string rightPath, string rel)
        {
            // Hash both files unconditionally — size is not consulted.
            // Still pass FileInfo so the cache avoids a second stat call.
            var fi1   = new FileInfo(leftPath);
            var fi2   = new FileInfo(rightPath);
            bool equal = _hashCache.GetHash(leftPath,  fi1)
                      == _hashCache.GetHash(rightPath, fi2);
            Log($"COMPARE (HASH ONLY) {rel}: {(equal ? "EQUAL" : "DIFFER")}");
            return equal;
        }

        private static bool AreXmlFilesEquivalent(string path1, string path2)
        {
            var doc1 = XDocument.Load(path1, LoadOptions.PreserveWhitespace);
            var doc2 = XDocument.Load(path2, LoadOptions.PreserveWhitespace);

            // Guard against XML files with no root element (empty file, declaration only)
            if (doc1.Root == null && doc2.Root == null) return true;
            if (doc1.Root == null || doc2.Root == null) return false;

            return NormalizeXmlElement(doc1.Root) == NormalizeXmlElement(doc2.Root);
        }

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
                    // Always emit explicit closing tag regardless of whether
                    // the source used <Item/> or <Item></Item> — both normalise
                    // to the same <Item></Item> form so they compare as equal.
                    sb.Append($"</{el.Name.LocalName}>");
                    continue;
                }

                sb.Append($"<{el.Name.LocalName}");

                // Sort attributes for canonical order — makes attribute-reordered
                // files compare as equal. Use local name only (ignore namespace prefix).
                foreach (var a in el.Attributes()
                                    .Where(a => !a.IsNamespaceDeclaration) // skip xmlns= decls
                                    .OrderBy(a => a.Name.LocalName, StringComparer.Ordinal))
                    sb.Append($" {a.Name.LocalName}=\"{a.Value.Trim()}\"");

                sb.Append('>');

                // Include direct text content (trim whitespace).
                // Skip XComment and XProcessingInstruction nodes — comments are
                // irrelevant to semantic equality.
                string text = string.Concat(
                    el.Nodes()
                      .OfType<XText>()
                      .Select(t => t.Value.Trim())
                      .Where(t => t.Length > 0));
                if (text.Length > 0) sb.Append(text);

                // Push closing tag first, then children in reverse so they
                // are popped and processed in document order.
                stack.Push((el, true));
                foreach (var child in el.Elements().Reverse())
                    stack.Push((child, false));
            }

            return sb.ToString();
        }

        // ── ExportAsync ──────────────────────────────────────────────────────
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
                int copied = 0, processed = 0;

                if (choice == "CL" || choice == "CR")
                {
                    string srcRoot = choice == "CL" ? leftRoot : rightRoot;
                    string label   = choice == "CL" ? "LeftFolder" : "RightFolder";
                    var allFiles   = Directory.EnumerateFiles(srcRoot, "*", SearchOption.AllDirectories).ToList();
                    int total      = allFiles.Count;
                    // #2 — dynamic interval
                    int interval   = Math.Max(1, Math.Min(200, total == 0 ? 1 : total / 50));

                    foreach (var fullPath in allFiles)
                    {
                        if (ct.IsCancellationRequested) { Log("Export cancelled — stopping early."); break; }
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
                        if (processed % interval == 0 || processed == total)
                            ProgressChanged?.Invoke(new ProgressInfo { Processed = processed, Total = total, Message = $"Exporting {processed}/{total}..." });
                    }
                    Log($"Exported total: {copied}");
                    return copied;
                }

                IEnumerable<string> toExport = choice switch
                {
                    "1" or "1L" or "1R" => res.Same,
                    "2"                  => res.LeftOnly,
                    "3"                  => res.RightOnly,
                    "4" or "4L" or "4R" => res.Different,
                    _                    => res.Same.Concat(res.LeftOnly).Concat(res.RightOnly).Concat(res.Different)
                };

                var exportList = toExport.ToList();
                int total2     = exportList.Count;
                // #2 — dynamic interval
                int interval2  = Math.Max(1, Math.Min(200, total2 == 0 ? 1 : total2 / 50));

                foreach (var rel in exportList)
                {
                    if (ct.IsCancellationRequested) { Log("Export cancelled — stopping early."); break; }
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
                                src = Path.Combine(leftRoot, rel); dest = CreateCategorySubfolders ? Path.Combine(exportRoot, "Diff_Left",  rel) : Path.Combine(exportRoot, rel);
                                if (File.Exists(src)) copied += SafeCopy(src, dest); break;
                            case "4R":
                                src = Path.Combine(rightRoot, rel); dest = CreateCategorySubfolders ? Path.Combine(exportRoot, "Diff_Right", rel) : Path.Combine(exportRoot, rel);
                                if (File.Exists(src)) copied += SafeCopy(src, dest); break;
                            case "1L":
                                src = Path.Combine(leftRoot, rel); dest = CreateCategorySubfolders ? Path.Combine(exportRoot, "Same_Left",  rel) : Path.Combine(exportRoot, rel);
                                if (File.Exists(src)) copied += SafeCopy(src, dest); break;
                            case "1R":
                                src = Path.Combine(rightRoot, rel); dest = CreateCategorySubfolders ? Path.Combine(exportRoot, "Same_Right", rel) : Path.Combine(exportRoot, rel);
                                if (File.Exists(src)) copied += SafeCopy(src, dest); break;
                            case "3":
                                src = Path.Combine(rightRoot, rel); dest = CreateCategorySubfolders ? Path.Combine(exportRoot, "RightOnly", rel) : Path.Combine(exportRoot, rel);
                                if (File.Exists(src)) copied += SafeCopy(src, dest); break;
                            default:
                                src = Path.Combine(leftRoot, rel);
                                string prefix = choice == "2" ? "LeftOnly" : choice == "1" ? "Same" : "All";
                                dest = CreateCategorySubfolders ? Path.Combine(exportRoot, prefix, rel) : Path.Combine(exportRoot, rel);
                                if (File.Exists(src)) copied += SafeCopy(src, dest); break;
                        }
                    }
                    catch (Exception ex) { Log($"Error exporting {rel}: {ex.Message}"); }
                    if (processed % interval2 == 0 || processed == total2)
                        ProgressChanged?.Invoke(new ProgressInfo { Processed = processed, Total = total2, Message = $"Exported {processed}/{total2} items..." });
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
            catch (UnauthorizedAccessException) { return 0; }  // no write permission — skip silently
            catch (IOException)                 { return 0; }  // locked, path too long, etc — skip silently
            catch                               { return 0; }  // any other unexpected error — skip silently
        }

        // ── DeleteAsync ──────────────────────────────────────────────────────
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
                int deleted = 0, processed = 0;
                int total   = targets.Count;
                // #2 — dynamic interval
                int interval = Math.Max(1, Math.Min(200, total == 0 ? 1 : total / 50));
                bool cancelled = false;

                foreach (var rel in targets)
                {
                    if (ct.IsCancellationRequested)
                    { Log("Delete cancelled — stopping early."); cancelled = true; break; }

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

                    if (processed % interval == 0 || processed == total)
                        ProgressChanged?.Invoke(new ProgressInfo { Processed = processed, Total = total, Message = $"Deleting {processed}/{total}..." });
                }

                // #6 — only prune empty folders when full run completed (not cancelled)
                if (!keepRoot && !cancelled)
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
                        if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                            Directory.Delete(dir, false);
                    }
                    catch { }
                }
                if (!keepRoot)
                    try
                    {
                        if (Directory.Exists(root) && !Directory.EnumerateFileSystemEntries(root).Any())
                            Directory.Delete(root, false);
                    }
                    catch { }
            }
            catch { }
        }
    }
}
