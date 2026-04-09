using System;

class Program
{
    static void Main()
    {
        try
        {
            Console.WriteLine("Enter Excel file path:");

            string path = Console.ReadLine().Trim().Trim('"');

            ExcelService.FindCommonFiles(path);

            Console.WriteLine("Task completed successfully.");
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error: " + ex.Message);
        }

        // Close application after message
        System.Threading.Thread.Sleep(2000);
        Environment.Exit(0);
    }
}