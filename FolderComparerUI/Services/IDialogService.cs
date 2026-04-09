namespace FolderComparerUI.Services
{
    /// <summary>
    /// Abstracts all file/folder picker dialogs.
    /// Inject this interface into ViewModels so they can be tested without
    /// popping real OS dialogs.
    /// </summary>
    public interface IDialogService
    {
        /// <summary>Opens a folder browser. Returns null if user cancels.</summary>
        string? BrowseFolder(string? currentPath = null);

        /// <summary>Opens a Save-As dialog. Returns null if user cancels.</summary>
        string? BrowseSaveFile(string title, string filter, string defaultName, string? currentPath = null);

        /// <summary>Opens a file-open dialog. Returns null if user cancels.</summary>
        string? BrowseOpenFile(string title, string filter, string? initialDir = null);
    }
}
