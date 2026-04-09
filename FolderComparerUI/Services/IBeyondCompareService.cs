namespace FolderComparerUI.Services
{
    public interface IBeyondCompareService
    {
        string? FindExecutable(string? userPath = null);
        void OpenFile(string bcPath, string filePath);
        void CompareFiles(string bcPath, string leftPath, string rightPath);
    }
}
