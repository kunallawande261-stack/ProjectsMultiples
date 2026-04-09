using System;
using System.IO;
using System.Xml;
using System.Collections.Generic;

namespace XmlValidatorApp
{
    class Program
    {
        static void Main(string[] args)
        {
            string folderPath;

            // 1. Get folder path (from args or user input)
            if (args.Length > 0)
            {
                folderPath = args[0];
            }
            else
            {
                Console.Write("Enter folder path: ");
                folderPath = Console.ReadLine();
            }

            folderPath = folderPath.Trim('"'); // remove quotes

            string logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "XmlValidationLog.txt");

            List<string> errorList = new List<string>();

            using (StreamWriter logWriter = new StreamWriter(logFilePath, false))
            {
                string[] xmlFiles = Directory.GetFiles(folderPath, "*.xml", SearchOption.AllDirectories);

                if (xmlFiles.Length == 0)
                {
                    Console.WriteLine("[WARN] No XML files found.");
                    logWriter.WriteLine("No XML files found in folder.");
                    return;
                }

                foreach (string xmlFile in xmlFiles)
                {
                    Console.WriteLine($"\nChecking: {xmlFile}");
                    string errorMessage = ValidateXml(xmlFile, logWriter);

                    if (errorMessage != null)
                        errorList.Add(errorMessage);
                }

                // Summary
                Console.WriteLine("\n====================== SUMMARY ======================");

                if (errorList.Count == 0)
                {
                    Console.WriteLine("[OK] All XML files are valid.");
                    logWriter.WriteLine("All XML files are valid.");
                }
                else
                {
                    Console.WriteLine("[ERROR] Invalid XML files found:\n");

                    int index = 1;
                    foreach (string err in errorList)
                    {
                        Console.WriteLine($"{index}. {err}\n");
                        index++;
                    }

                    Console.WriteLine($"Total Invalid Files: {errorList.Count}");
                    Console.WriteLine($"Log file: {logFilePath}");
                }
            }

            Console.WriteLine("\nValidation completed. Press any key to exit.");
            Console.ReadKey();
        }

        static string ValidateXml(string filePath, StreamWriter logWriter)
        {
            try
            {
                using (XmlReader reader = XmlReader.Create(filePath))
                {
                    while (reader.Read()) { }
                }

                Console.WriteLine("   [OK] Valid");
                return null;
            }
            catch (XmlException ex)
            {
                string error =
                    $"File: {filePath}\n   Line {ex.LineNumber}, Pos {ex.LinePosition} | {ex.Message}";

                Console.WriteLine("   [ERROR] Invalid");
                Console.WriteLine($"      {error}\n");  // <-- Added blank line after error

                logWriter.WriteLine(error);
                logWriter.WriteLine();                  // <-- Blank line in log file
                logWriter.WriteLine("------------------------------------------------------");

                return error;
            }
            catch (Exception ex)
            {
                string error =
                    $"Unexpected error in file: {filePath}\n   {ex.Message}";

                Console.WriteLine("   [WARN] Unexpected Error");
                Console.WriteLine($"      {error}\n");  // <-- Added blank line

                logWriter.WriteLine(error);
                logWriter.WriteLine();                  // <-- Blank line in log file
                logWriter.WriteLine("------------------------------------------------------");

                return error;
            }
        }
    }
}
