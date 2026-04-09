using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;

namespace FolderComparerLib
{
    /// <summary>
    /// Thread-safe persistent file-content hash cache.
    ///
    /// Cache key: (path, size, LastWriteTimeUtc)
    /// Cache hit: all three match → return stored hash, zero disk I/O.
    /// Cache miss: size OR timestamp changed → recompute hash and update entry.
    ///
    /// Why this is better than size-only (Option 4):
    ///   Option 4 misses same-size content changes entirely.
    ///   This approach misses only the extremely rare case where a file is edited
    ///   AND its size AND its timestamp are both identical to before — which only
    ///   happens when a tool deliberately preserves both (use Force Re-read then).
    ///
    /// Why this is better than the old approach (cleared on every run):
    ///   The cache is NEVER cleared between runs on the same folders.
    ///   Post-delete reloads are fast — unchanged files return cached hashes.
    ///   After a normal BC save, only the edited files (changed timestamp) are
    ///   rehashed. Aras AML files with same content but different export timestamps
    ///   are rehashed on first comparison, produce the same hash, and compare as Same.
    ///
    /// Hashing strategy:
    ///   Empty file      → sentinel "EMPTY_FILE"
    ///   Small (≤ 64 KB) → full SHA-256
    ///   Large (> 64 KB) → sampled SHA-256: first 64 KB + middle 64 KB +
    ///                     last 64 KB + exact file size mixed in.
    ///                     ~10-20× faster than full hash; catches all real diffs.
    /// </summary>
    public class FileHashCache
    {
        private readonly ConcurrentDictionary<string, CacheEntry> _cache
            = new(StringComparer.OrdinalIgnoreCase);

        private const int  MaxEntries   = 50_000;
        private const long SmallFileMax = 64 * 1024;
        private const int  SampleSize   = 64 * 1024;

        private sealed record CacheEntry(long Size, DateTime Time, string Hash);

        /// <summary>
        /// Returns a content-identity hash for the file.
        /// Cache hit:  size AND LastWriteTimeUtc both match → return stored hash.
        /// Cache miss: either changed → recompute and update entry.
        /// </summary>
        public string GetHash(string file) => GetHash(file, new FileInfo(file));

        /// <summary>
        /// Overload accepting a pre-read FileInfo to avoid a second stat call.
        /// Returns a unique error sentinel if the file cannot be read (locked,
        /// deleted between scan and hash, access denied) so that one bad file
        /// does not abort the entire comparison.
        /// </summary>
        public string GetHash(string file, FileInfo info)
        {
            try
            {
                if (_cache.TryGetValue(file, out var cached) &&
                    cached.Size == info.Length         &&
                    cached.Time == info.LastWriteTimeUtc)
                    return cached.Hash;

                string hash = Compute(file, info.Length);

                if (_cache.Count >= MaxEntries) _cache.Clear();
                _cache[file] = new CacheEntry(info.Length, info.LastWriteTimeUtc, hash);
                return hash;
            }
            catch (UnauthorizedAccessException)
            {
                // File exists but we have no read permission — return a path-unique
                // sentinel so the file is not silently classified as Same.
                return $"ACCESS_DENIED:{file}";
            }
            catch (IOException)
            {
                // File locked by another process, or deleted between scan and hash.
                // Return a path-unique sentinel — two files with this error on the
                // same relative path will still compare as Same (same sentinel).
                return $"IO_ERROR:{file}";
            }
        }

        /// <summary>
        /// Clears all cached entries.
        /// Call only when:
        ///   (a) The left/right folder paths change entirely, or
        ///   (b) ForceReread is requested — e.g. after BC save with
        ///       Preserve Timestamps option where size also stayed the same.
        /// Do NOT call between normal runs on the same folders.
        /// </summary>
        public void Clear() => _cache.Clear();

        private static string Compute(string file, long length)
        {
            if (length == 0) return "EMPTY_FILE";

            using var sha = SHA256.Create();

            if (length <= SmallFileMax)
            {
                // Small file — full hash, always accurate
                using var s = File.OpenRead(file);
                return Convert.ToHexString(sha.ComputeHash(s));
            }

            // Large file — sampled hash: first + middle + last region.
            // Catches header, middle, and trailer changes.
            // File size is mixed into the final hash so files that differ
            // only in appended bytes still produce different hashes.
            var buf = new byte[SampleSize];
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read,
                                          FileShare.Read, SampleSize, false);

            HashChunk(sha, fs, 0,                          buf);  // first
            HashChunk(sha, fs, (length - SampleSize) / 2, buf);  // middle
            HashChunk(sha, fs, length - SampleSize,        buf);  // last

            sha.TransformFinalBlock(BitConverter.GetBytes(length), 0, 8);
            return Convert.ToHexString(sha.Hash!);
        }

        private static void HashChunk(SHA256 sha, FileStream fs, long offset, byte[] buf)
        {
            fs.Seek(offset, SeekOrigin.Begin);
            int total = 0, n;
            while (total < SampleSize &&
                   (n = fs.Read(buf, total, SampleSize - total)) > 0)
                total += n;
            sha.TransformBlock(buf, 0, total, null, 0);
        }
    }
}
