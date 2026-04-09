using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;

namespace FolderComparerLib
{
    /// <summary>
    /// Thread-safe SHA-256 hash cache keyed by full file path.
    /// Evicts all entries when size exceeds MaxEntries to prevent unbounded memory growth.
    /// </summary>
    public class FileHashCache
    {
        private readonly ConcurrentDictionary<string, (long size, DateTime time, string hash)> _cache = new();
        private const int MaxEntries = 50_000;

        public string GetHash(string file)
        {
            var info = new FileInfo(file);

            if (_cache.TryGetValue(file, out var entry))
            {
                if (entry.size == info.Length && entry.time == info.LastWriteTimeUtc)
                    return entry.hash;
            }

            string hash = ComputeHash(file);

            if (_cache.Count >= MaxEntries)
                _cache.Clear();

            _cache[file] = (info.Length, info.LastWriteTimeUtc, hash);
            return hash;
        }

        /// <summary>Clears all cached hashes. Call after each compare run to free memory.</summary>
        public void Clear() => _cache.Clear();

        private static string ComputeHash(string file)
        {
            using var stream = File.OpenRead(file);
            using var sha    = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(stream));
        }
    }
}
