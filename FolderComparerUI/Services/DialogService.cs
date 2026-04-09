using Microsoft.Win32;
using Ookii.Dialogs.Wpf;
using System.Runtime.Versioning;

namespace FolderComparerUI.Services
{
    /// <summary>
    /// Abstracts file/folder dialogs so the ViewModel has no UI dependency.
    /// </summary>
    public class DialogService : IDialogService
    {
        [SupportedOSPlatform("windows7.0")]
        public string? BrowseFolder(string? currentPath = null)
        {
            var dlg = new VistaFolderBrowserDialog
            {
                Description            = "Select a folder",
                UseDescriptionForTitle = true
            };
            if (!string.IsNullOrEmpty(currentPath) && System.IO.Directory.Exists(currentPath))
                dlg.SelectedPath = currentPath;
            return dlg.ShowDialog() == true ? dlg.SelectedPath : null;
        }

        [SupportedOSPlatform("windows7.0")]
        public string? BrowseSaveFile(string title, string filter, string defaultName, string? currentPath = null)
        {
            var dlg = new SaveFileDialog { Title = title, Filter = filter, FileName = defaultName };
            if (!string.IsNullOrWhiteSpace(currentPath))
                try
                {
                    dlg.InitialDirectory = System.IO.Directory.Exists(currentPath)
                        ? currentPath
                        : System.IO.Path.GetDirectoryName(currentPath) ?? "";
                }
                catch { }
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }

        [SupportedOSPlatform("windows7.0")]
        public string? BrowseOpenFile(string title, string filter, string? initialDir = null)
        {
            var dlg = new OpenFileDialog { Title = title, Filter = filter };
            if (!string.IsNullOrWhiteSpace(initialDir))
                dlg.InitialDirectory = initialDir;
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }
    }
}
