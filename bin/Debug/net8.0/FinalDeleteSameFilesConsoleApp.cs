using System;
using System.Collections.Generic;
using System.IO;

class Program
{
    static string logFile = "";
    static bool enableLogging = false;
    static bool keepRootFolders = true;

    static void Main(string[] args)
    {
        Console.WriteLine("Do you want to create a log file? (y/n)");
        enableLogging = Console.ReadLine()?.Trim().ToLower() == "y";

        if (enableLogging)
        {
            logFile = $"CompareDeleteLog_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            File.WriteAllText(logFile, $"Log started: {DateTime.Now}\n\n");
        }

        Console.WriteLine("Do you want to keep root folders? (y/n)");
        keepRootFolders = Console.ReadLine()?.Trim().ToLower() != "n";

        Console.WriteLine("Enter path for Folder 1:");
        string folder1 = TrimQuotes(Console.ReadLine() ?? "");
        Console.WriteLine("Enter path for Folder 2:");
        string folder2 = TrimQuotes(Console.ReadLine() ?? "");

        if (!Directory.Exists(folder1) || !Directory.Exists(folder2))
        {
            Console.WriteLine("One or both folders do not exist. Exiting.");
            return;
        }

        Console.WriteLine("\nPress Enter to start the comparison...");
        Console.ReadLine();

        int deletedFiles = CompareAndDelete(folder1, folder2);

        DeleteEmptyFolders(folder1);
        DeleteEmptyFolders(folder2);

        Console.WriteLine($"\nOperation complete. Total files deleted: {deletedFiles}");
        if (enableLogging) Console.WriteLine($"Check log file: {logFile}");
    }

    static string TrimQuotes(string path)
    {
        return path.Trim().Trim('"');
    }

    static int CompareAndDelete(string folder1, string folder2)
    {
        var files1 = Directory.GetFiles(folder1, "*", SearchOption.AllDirectories);
        var files2 = Directory.GetFiles(folder2, "*", SearchOption.AllDirectories);

        var relativeToFull1 = new Dictionary<string, string>();
        foreach (var f in files1)
            relativeToFull1[Path.GetRelativePath(folder1, f)] = f;

        var relativeToFull2 = new Dictionary<string, string>();
        foreach (var f in files2)
            relativeToFull2[Path.GetRelativePath(folder2, f)] = f;

        int deletedCount = 0;
        int totalFiles = relativeToFull1.Count;
        int processed = 0;

        Console.WriteLine("\nProcessing started...");

        foreach (var kv in relativeToFull1)
        {
            processed++;
            string rel = kv.Key;
            string path1 = kv.Value;

            if (relativeToFull2.TryGetValue(rel, out string path2))
            {
                try
                {
                    if (FilesAreEqualBinary(path1, path2))
                    {
                        File.Delete(path1);
                        File.Delete(path2);
                        deletedCount++;
                        Log($"Deleted file: {rel}");
                    }
                    else
                    {
                        Log($"Kept different file: {rel}");
                    }
                }
                catch (Exception ex)
                {
                    Log($"Error deleting {rel}: {ex.Message}");
                }
            }
            else
            {
                Log($"Only in left: {rel}");
            }

            if (processed % 100 == 0 || processed == totalFiles)
            {
                Console.Write($"\rProcessed {processed}/{totalFiles} files...");
            }
        }

        foreach (var kv in relativeToFull2)
        {
            string rel = kv.Key;
            if (!relativeToFull1.ContainsKey(rel))
            {
                Log($"Only in right: {rel}");
            }
        }

        return deletedCount;
    }

    static bool FilesAreEqualBinary(string file1, string file2)
    {
        var f1 = new FileInfo(file1);
        var f2 = new FileInfo(file2);

        if (f1.Length != f2.Length) return false;

        const int bufferSize = 1024 * 1024; // 1MB
        byte[] b1 = new byte[bufferSize];
        byte[] b2 = new byte[bufferSize];

        using var fs1 = f1.OpenRead();
        using var fs2 = f2.OpenRead();

        int bytesRead1, bytesRead2;
        do
        {
            bytesRead1 = fs1.Read(b1, 0, bufferSize);
            bytesRead2 = fs2.Read(b2, 0, bufferSize);

            if (bytesRead1 != bytesRead2) return false;

            for (int i = 0; i < bytesRead1; i++)
            {
                if (b1[i] != b2[i]) return false;
            }
        } while (bytesRead1 > 0);

        return true;
    }

    static void DeleteEmptyFolders(string folder)
    {
        foreach (var dir in Directory.GetDirectories(folder, "*", SearchOption.AllDirectories))
        {
            try
            {
                if (Directory.Exists(dir) && Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Length == 0)
                {
                    Directory.Delete(dir, true);
                    Log($"Deleted empty folder: {dir}");
                }
            }
            catch (Exception ex)
            {
                Log($"Error deleting folder {dir}: {ex.Message}");
            }
        }

        if (!keepRootFolders &&
            Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Length == 0 &&
            Directory.GetDirectories(folder, "*", SearchOption.AllDirectories).Length == 0)
        {
            try
            {
                Directory.Delete(folder, true);
                Log($"Deleted root folder: {folder}");
            }
            catch (Exception ex)
            {
                Log($"Error deleting root folder {folder}: {ex.Message}");
            }
        }
    }

    static void Log(string message)
    {
        if (!enableLogging) return;
        string logEntry = $"[{DateTime.Now}] {message}";
        File.AppendAllText(logFile, logEntry + Environment.NewLine);
    }
}
