using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace FolderComparerUI
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // ── Global exception handlers — prevent silent crashes ────────────

            // 1. Unhandled exceptions on the WPF UI thread
            DispatcherUnhandledException += OnDispatcherUnhandledException;

            // 2. Unhandled exceptions on any thread (including Task continuations
            //    that are not awaited and background threads)
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

            // 3. Unobserved exceptions from Tasks (fire-and-forget async code
            //    where the Task is never awaited and the exception is never read)
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        }

        private static void OnDispatcherUnhandledException(object sender,
            DispatcherUnhandledExceptionEventArgs e)
        {
            ShowCrashDialog(e.Exception, "UI Thread");
            e.Handled = true;   // prevent application from terminating
        }

        private static void OnUnhandledException(object sender,
            UnhandledExceptionEventArgs e)
        {
            var ex = e.ExceptionObject as Exception;
            // Cannot prevent termination here for fatal CLR exceptions,
            // but we can at least log and show a message before the crash.
            ShowCrashDialog(ex, "Background Thread");
        }

        private static void OnUnobservedTaskException(object? sender,
            UnobservedTaskExceptionEventArgs e)
        {
            // Mark as observed so the process does not crash on GC finalisation
            e.SetObserved();
            LogError(e.Exception, "Unobserved Task");
        }

        private static void ShowCrashDialog(Exception? ex, string source)
        {
            LogError(ex, source);

            string msg =
                $"An unexpected error occurred in {source}.\n\n" +
                $"{ex?.GetType().Name}: {ex?.Message}\n\n" +
                "The application will try to continue. If problems persist, " +
                "please restart the application.\n\n" +
                "A log has been written to the application folder.";

            try
            {
                MessageBox.Show(msg, "Unexpected Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch
            {
                // If even MessageBox fails (e.g. no UI thread) — ignore
            }
        }

        private static void LogError(Exception? ex, string source)
        {
            try
            {
                string logPath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    $"CrashLog_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

                string content =
                    $"[{DateTime.Now}] Unhandled exception — source: {source}\n" +
                    $"Type:    {ex?.GetType().FullName}\n" +
                    $"Message: {ex?.Message}\n" +
                    $"Stack:\n{ex?.StackTrace}\n" +
                    (ex?.InnerException != null
                        ? $"Inner:   {ex.InnerException.GetType().Name}: {ex.InnerException.Message}\n"
                        : "") +
                    new string('-', 80) + "\n";

                File.AppendAllText(logPath, content);
            }
            catch { /* never throw from an error handler */ }
        }
    }
}
