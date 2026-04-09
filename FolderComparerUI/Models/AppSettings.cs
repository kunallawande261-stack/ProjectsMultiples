namespace FolderComparerUI.Models
{
    /// <summary>
    /// Persisted user preferences — serialised to %AppData%\FolderComparer\settings.json.
    /// Increment CurrentVersion and add migration logic in SettingsService.Load()
    /// whenever a new field is added.
    /// </summary>
    public class AppSettings
    {
        // #10 — Version field for forward-compatible migration
        public int     Version            { get; set; } = 1;

        public string? LeftPath           { get; set; }
        public string? RightPath          { get; set; }
        public string? ExcludePatterns    { get; set; }
        public string? LogPath            { get; set; }
        public string? ExcelPath          { get; set; }
        public string? SheetName          { get; set; }
        public string? ExportPath         { get; set; }
        public string? BeyondComparePath  { get; set; }
        public bool    EnableLogging      { get; set; }
        public bool    SmartXml           { get; set; }
        public bool    Topmost            { get; set; }
        public bool    KeepRoot           { get; set; }
        public bool    CategorySubfolders { get; set; }
        public string? ModeTag            { get; set; } = "2";
        public string? TargetTag          { get; set; } = "5";
        public string? ExcelCategoryTag   { get; set; } = "5";
        public string? CompareModeTag     { get; set; } = "Normal";
    }
}
