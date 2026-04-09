namespace FolderComparerUI.Models
{
    /// <summary>One row displayed in the results ListView.</summary>
    public class ResultRow
    {
        public string LeftFull  { get; init; } = "";
        public string Category  { get; init; } = "";
        public string RightFull { get; init; } = "";
        public string Relative  { get; init; } = "";
    }
}
