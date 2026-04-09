using FolderComparerLib;
using FolderComparerUI.Models;
using FolderComparerUI.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;

namespace FolderComparerUI.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        // ── Services ─────────────────────────────────────────────────────────
        private readonly FolderComparer      _comparer  = new();
        private readonly SettingsService     _settings  = new();
        private readonly DialogService       _dialogs   = new();
        private readonly ExcelExportService  _excel     = new();
        private readonly BeyondCompareService _bc       = new();

        private CancellationTokenSource? _cts;
        private const int BatchSize    = 200;
        private const int MaxLogLines  = 1000;
        private int  _currentIndex     = 0;
        private bool _isLoading        = false;

        // ── Result data ───────────────────────────────────────────────────────
        private List<ResultRow>                          _allResults  = new();
        private readonly ObservableCollection<ResultRow> _resultItems = new();
        public  ICollectionView ResultsView { get; }

        private CompareResult? _lastCompareResult;
        private int _lastCopied  = 0;
        private int _lastDeleted = 0;

        // ── Wired context menus (runtime-wired in code-behind) ────────────────
        public readonly HashSet<ContextMenu> WiredMenus = new();

        // ════════════════════════════════════════════════════════════════════════
        //  BINDABLE PROPERTIES
        // ════════════════════════════════════════════════════════════════════════

        private string _leftPath           = "";
        private string _rightPath          = "";
        private string _excludePatterns    = "";
        private string _logPath            = "";
        private string _excelPath          = "";
        private string _sheetName          = "";
        private string _exportPath         = "";
        private string _bcPath             = "";
        private string _filterText         = "";
        private string _logText            = "";
        private string _statusText         = "Ready";
        private string _bcStatusText       = "";
        private double _progressMax        = 1;
        private double _progressValue      = 0;
        private bool   _enableLogging      = false;
        private bool   _smartXml           = false;
        private bool   _topmost            = false;
        private bool   _keepRoot           = false;
        private bool   _categorySubfolders = false;
        private bool   _recycleBin         = true;
        private string _modeTag            = "2";   // "2"=Delete "3"=Copy
        private string _targetTag          = "5";
        private string _excelCategoryTag   = "5";
        private string _sortLeftHeader     = "Left file";
        private string _sortCategoryHeader = "Category";
        private string _sortRightHeader    = "Right file";
        private ListSortDirection _sortDir = ListSortDirection.Ascending;
        private string? _lastSortProp;
        private string _bcStatusForeground = "Gray";

        // Visibility strings for UI elements driven by mode
        private string _copyGroupVisibility    = "Collapsed";
        private string _keepRootVisibility     = "Collapsed";
        private string _logPathVisibility      = "Collapsed";
        // Target item visibility (Delete-only vs Copy-only)
        private string _sameBothVisibility     = "Collapsed";
        private string _sameLeftVisibility     = "Collapsed";
        private string _sameRightVisibility    = "Collapsed";
        private string _copyLeftVisibility     = "Collapsed";
        private string _copyRightVisibility    = "Collapsed";

        public string LeftPath           { get => _leftPath;           set { SetField(ref _leftPath, value);           } }
        public string RightPath          { get => _rightPath;          set { SetField(ref _rightPath, value);          } }
        public string ExcludePatterns    { get => _excludePatterns;    set { SetField(ref _excludePatterns, value);    } }
        public string LogPath            { get => _logPath;            set { SetField(ref _logPath, value);            } }
        public string ExcelPath          { get => _excelPath;          set { SetField(ref _excelPath, value);          } }
        public string SheetName          { get => _sheetName;          set { SetField(ref _sheetName, value);          } }
        public string ExportPath         { get => _exportPath;         set { SetField(ref _exportPath, value);         } }
        public string BcPath             { get => _bcPath;             set { SetField(ref _bcPath, value); UpdateBcStatus(); } }
        public string FilterText         { get => _filterText;         set { SetField(ref _filterText, value); ApplyFilter(); } }
        public string LogText            { get => _logText;            set { SetField(ref _logText, value);            } }
        public string StatusText         { get => _statusText;         set { SetField(ref _statusText, value);         } }
        public string BcStatusText       { get => _bcStatusText;       set { SetField(ref _bcStatusText, value);       } }
        public string BcStatusForeground { get => _bcStatusForeground; set { SetField(ref _bcStatusForeground, value); } }
        public double ProgressMax        { get => _progressMax;        set { SetField(ref _progressMax, value);        } }
        public double ProgressValue      { get => _progressValue;      set { SetField(ref _progressValue, value);      } }
        public bool   EnableLogging      { get => _enableLogging;      set { SetField(ref _enableLogging, value); UpdateLogPathVisibility(); } }
        public bool   SmartXml           { get => _smartXml;           set { SetField(ref _smartXml, value);           } }
        public bool   Topmost            { get => _topmost;            set { SetField(ref _topmost, value);            } }
        public bool   KeepRoot           { get => _keepRoot;           set { SetField(ref _keepRoot, value);           } }
        public bool   CategorySubfolders { get => _categorySubfolders; set { SetField(ref _categorySubfolders, value); } }
        public bool   RecycleBin         { get => _recycleBin;         set { SetField(ref _recycleBin, value);         } }
        public string SortLeftHeader     { get => _sortLeftHeader;     set { SetField(ref _sortLeftHeader, value);     } }
        public string SortCategoryHeader { get => _sortCategoryHeader; set { SetField(ref _sortCategoryHeader, value); } }
        public string SortRightHeader    { get => _sortRightHeader;    set { SetField(ref _sortRightHeader, value);    } }
        public string CopyGroupVisibility    { get => _copyGroupVisibility;    set { SetField(ref _copyGroupVisibility, value);    } }
        public string KeepRootVisibility     { get => _keepRootVisibility;     set { SetField(ref _keepRootVisibility, value);     } }
        public string LogPathVisibility      { get => _logPathVisibility;      set { SetField(ref _logPathVisibility, value);      } }
        public string SameBothVisibility     { get => _sameBothVisibility;     set { SetField(ref _sameBothVisibility, value);     } }
        public string SameLeftVisibility     { get => _sameLeftVisibility;     set { SetField(ref _sameLeftVisibility, value);     } }
        public string SameRightVisibility    { get => _sameRightVisibility;    set { SetField(ref _sameRightVisibility, value);    } }
        public string CopyLeftVisibility     { get => _copyLeftVisibility;     set { SetField(ref _copyLeftVisibility, value);     } }
        public string CopyRightVisibility    { get => _copyRightVisibility;    set { SetField(ref _copyRightVisibility, value);    } }

        public string ModeTag
        {
            get => _modeTag;
            set { if (SetField(ref _modeTag, value)) UpdateModeVisibility(); }
        }

        public string TargetTag
        {
            get => _targetTag;
            set { SetField(ref _targetTag, value); }
        }

        public string ExcelCategoryTag
        {
            get => _excelCategoryTag;
            set { SetField(ref _excelCategoryTag, value); }
        }

        // Expose observable collection for ListView binding
        public ObservableCollection<ResultRow> ResultItems => _resultItems;

        // Selected row (set by View, used by context menu commands)
        private ResultRow? _selectedRow;
        public ResultRow? SelectedRow
        {
            get => _selectedRow;
            set { SetField(ref _selectedRow, value); RelayCommand.Refresh(); }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  COMMANDS
        // ════════════════════════════════════════════════════════════════════════
        public AsyncRelayCommand AnalyzeCommand      { get; }
        public AsyncRelayCommand ReloadCommand       { get; }
        public AsyncRelayCommand StartCommand        { get; }
        public RelayCommand      CancelCommand       { get; }
        public RelayCommand      BrowseLeftCommand   { get; }
        public RelayCommand      BrowseRightCommand  { get; }
        public RelayCommand      BrowseExportCommand { get; }
        public RelayCommand      BrowseLogCommand    { get; }
        public RelayCommand      BrowseExcelCommand  { get; }
        public RelayCommand      BrowseBcPathCommand { get; }
        public RelayCommand      SaveBcPathCommand   { get; }
        public RelayCommand      ClearBcPathCommand  { get; }
        public AsyncRelayCommand ExportExcelCommand  { get; }
        public RelayCommand      SortLeftCommand     { get; }
        public RelayCommand      SortCategoryCommand { get; }
        public RelayCommand      SortRightCommand    { get; }
        // Context menu commands
        public RelayCommand OpenLeftExplorerCommand   { get; }
        public RelayCommand OpenRightExplorerCommand  { get; }
        public RelayCommand OpenLeftBcCommand         { get; }
        public RelayCommand OpenRightBcCommand        { get; }
        public RelayCommand CompareBcCommand          { get; }

        // ════════════════════════════════════════════════════════════════════════
        //  CONSTRUCTOR
        // ════════════════════════════════════════════════════════════════════════
        public MainViewModel()
        {
            // Collection view for filter + sort
            ResultsView = CollectionViewSource.GetDefaultView(_resultItems);

            // Wire comparer events
            _comparer.ProgressChanged += OnProgressChanged;
            _comparer.LogMessage      += OnLogMessage;

            // Commands
            AnalyzeCommand      = new AsyncRelayCommand(RunAnalysisAsync);
            ReloadCommand       = new AsyncRelayCommand(RunAnalysisAsync,
                                      () => _lastCompareResult != null);
            StartCommand        = new AsyncRelayCommand(RunStartAsync,
                                      () => _lastCompareResult != null);
            CancelCommand       = new RelayCommand(DoCancel, () => _cts != null);
            BrowseLeftCommand   = new RelayCommand(() => LeftPath   = _dialogs.BrowseFolder(LeftPath)   ?? LeftPath);
            BrowseRightCommand  = new RelayCommand(() => RightPath  = _dialogs.BrowseFolder(RightPath)  ?? RightPath);
            BrowseExportCommand = new RelayCommand(() => ExportPath = _dialogs.BrowseFolder(ExportPath) ?? ExportPath);
            BrowseLogCommand    = new RelayCommand(DoBrowseLog);
            BrowseExcelCommand  = new RelayCommand(DoBrowseExcel);
            BrowseBcPathCommand = new RelayCommand(DoBrowseBcPath);
            SaveBcPathCommand   = new RelayCommand(DoSaveBcPath);
            ClearBcPathCommand  = new RelayCommand(() => { BcPath = ""; SaveSettings(); AppendLog("BC path cleared — will auto-detect."); });
            ExportExcelCommand  = new AsyncRelayCommand(RunExportExcelAsync);
            SortLeftCommand     = new RelayCommand(() => ToggleSort("LeftFull"));
            SortCategoryCommand = new RelayCommand(() => ToggleSort("Category"));
            SortRightCommand    = new RelayCommand(() => ToggleSort("RightFull"));
            OpenLeftExplorerCommand  = new RelayCommand(() => { if (SelectedRow?.LeftFull  is string p && !string.IsNullOrEmpty(p)) RevealInExplorer(p); });
            OpenRightExplorerCommand = new RelayCommand(() => { if (SelectedRow?.RightFull is string p && !string.IsNullOrEmpty(p)) RevealInExplorer(p); });
            OpenLeftBcCommand        = new RelayCommand(() => LaunchBc(SelectedRow?.LeftFull));
            OpenRightBcCommand       = new RelayCommand(() => LaunchBc(SelectedRow?.RightFull));
            CompareBcCommand         = new RelayCommand(DoCompareBc,
                                           () => !string.IsNullOrEmpty(SelectedRow?.LeftFull) && !string.IsNullOrEmpty(SelectedRow?.RightFull));

            UpdateModeVisibility();
            UpdateLogPathVisibility();
        }

        // ════════════════════════════════════════════════════════════════════════
        //  SETTINGS
        // ════════════════════════════════════════════════════════════════════════
        public void LoadSettings()
        {
            var s = _settings.Load();
            LeftPath           = s.LeftPath        ?? "";
            RightPath          = s.RightPath       ?? "";
            ExcludePatterns    = s.ExcludePatterns ?? "";
            LogPath            = s.LogPath         ?? "";
            ExcelPath          = s.ExcelPath       ?? "";
            SheetName          = s.SheetName       ?? "";
            ExportPath         = s.ExportPath      ?? "";
            BcPath             = s.BeyondComparePath ?? "";
            EnableLogging      = s.EnableLogging;
            SmartXml           = s.SmartXml;
            Topmost            = s.Topmost;
            KeepRoot           = s.KeepRoot;
            CategorySubfolders = s.CategorySubfolders;
            RecycleBin         = s.RecycleBin;
            ModeTag            = s.ModeTag   ?? "2";
            TargetTag          = s.TargetTag ?? "5";
            ExcelCategoryTag   = s.ExcelCategoryTag ?? "5";
            UpdateBcStatus();
        }

        public void SaveSettings()
        {
            _settings.Save(new AppSettings
            {
                LeftPath           = LeftPath,
                RightPath          = RightPath,
                ExcludePatterns    = ExcludePatterns,
                LogPath            = LogPath,
                ExcelPath          = ExcelPath,
                SheetName          = SheetName,
                ExportPath         = ExportPath,
                BeyondComparePath  = BcPath,
                EnableLogging      = EnableLogging,
                SmartXml           = SmartXml,
                Topmost            = Topmost,
                KeepRoot           = KeepRoot,
                CategorySubfolders = CategorySubfolders,
                RecycleBin         = RecycleBin,
                ModeTag            = ModeTag,
                TargetTag          = TargetTag,
                ExcelCategoryTag   = ExcelCategoryTag
            });
        }

        // ════════════════════════════════════════════════════════════════════════
        //  ANALYSIS
        // ════════════════════════════════════════════════════════════════════════
        private async Task RunAnalysisAsync()
        {
            if (!ValidatePaths(LeftPath, RightPath)) return;

            LogText = "";
            AppendLog("Starting folder comparison…");
            StatusText  = "Analysing…";
            ProgressValue = 0;
            _resultItems.Clear();
            _lastCopied = _lastDeleted = 0;

            _comparer.EnableLogging   = EnableLogging;
            _comparer.LogFilePath     = string.IsNullOrWhiteSpace(LogPath)
                ? Path.Combine(Environment.CurrentDirectory, $"CompareLog_{DateTime.Now:yyyyMMdd_HHmmss}.txt")
                : LogPath;
            _comparer.SmartXmlCompare = SmartXml;

            _cts = new CancellationTokenSource();
            RelayCommand.Refresh();
            try
            {
                var result = await _comparer.CompareAsync(LeftPath, RightPath,
                    ParsePatterns(ExcludePatterns), _cts.Token);
                _lastCompareResult = result;

                await BuildResultsAsync(result);

                int total   = result.Same.Count + result.LeftOnly.Count + result.RightOnly.Count + result.Different.Count;
                string summ = $"Done — Total: {total} | Same: {result.Same.Count} | Left-only: {result.LeftOnly.Count} | Right-only: {result.RightOnly.Count} | Different: {result.Different.Count}";
                AppendLog(summ);
                StatusText = summ;

                // Show summary dialog (dispatched so async chain completes cleanly)
                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    MessageBox.Show(
                        $"Analysis complete.\n\nSame: {result.Same.Count}\nLeft-only: {result.LeftOnly.Count}\n" +
                        $"Right-only: {result.RightOnly.Count}\nDifferent: {result.Different.Count}\n\n" +
                        "Use Start to Delete or Copy.",
                        "Analysis Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                });
            }
            catch (OperationCanceledException) { AppendLog("Analysis cancelled."); StatusText = "Cancelled."; }
            catch (Exception ex)               { AppendLog($"Analysis failed: {ex.Message}"); StatusText = "Error."; }
            finally { _cts = null; RelayCommand.Refresh(); }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  START (Delete / Copy)
        // ════════════════════════════════════════════════════════════════════════
        private async Task RunStartAsync()
        {
            if (!ValidatePaths(LeftPath, RightPath)) return;
            if (_lastCompareResult == null && ModeTag != "3")
            {
                MessageBox.Show("Run analysis first.", "No Analysis", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _comparer.EnableLogging            = EnableLogging;
            _comparer.LogFilePath              = string.IsNullOrWhiteSpace(LogPath)
                ? Path.Combine(Environment.CurrentDirectory, $"CompareLog_{DateTime.Now:yyyyMMdd_HHmmss}.txt") : LogPath;
            _comparer.SmartXmlCompare          = SmartXml;
            _comparer.CreateCategorySubfolders = CategorySubfolders;
            _comparer.SetExcludePatterns(ParsePatterns(ExcludePatterns));

            LogText   = "";
            _lastCopied = _lastDeleted = 0;
            _cts = new CancellationTokenSource();
            RelayCommand.Refresh();

            try
            {
                if (ModeTag == "3")
                    await DoCopyAsync();
                else if (ModeTag == "2")
                    await DoDeleteAsync();
            }
            catch (OperationCanceledException) { AppendLog("Operation cancelled."); StatusText = "Cancelled."; }
            catch (Exception ex)               { AppendLog($"Fatal: {ex.Message}"); StatusText = "Error."; }
            finally { _cts = null; RelayCommand.Refresh(); }
        }

        private async Task DoCopyAsync()
        {
            string exportRoot = string.IsNullOrWhiteSpace(ExportPath)
                ? Path.Combine(Environment.CurrentDirectory, "Output") : ExportPath;

            AppendLog($"Starting copy to: {exportRoot}");
            StatusText = "Copying…";

            int copied = await _comparer.ExportAsync(LeftPath, RightPath, TargetTag,
                _lastCompareResult!, exportRoot, _cts!.Token);
            _lastCopied = copied;
            AppendLog($"Copy complete. Files copied: {copied}");
            StatusText = $"Copy complete — {copied} file(s) copied.";

            MessageBox.Show($"Copy completed.\n\nFiles copied: {copied}",
                "Copy Completed", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async Task DoDeleteAsync()
        {
            if (_lastCompareResult == null) return;

            var toDelete = TargetTag switch
            {
                "1" or "1L" or "1R" => _lastCompareResult.Same.ToList(),
                "2"                  => _lastCompareResult.LeftOnly.ToList(),
                "3"                  => _lastCompareResult.RightOnly.ToList(),
                "4" or "4L" or "4R" => _lastCompareResult.Different.ToList(),
                _                    => _lastCompareResult.Same
                                            .Concat(_lastCompareResult.LeftOnly)
                                            .Concat(_lastCompareResult.RightOnly)
                                            .Concat(_lastCompareResult.Different).ToList()
            };

            int deletable = _comparer.CountDeletableItems(LeftPath, RightPath, TargetTag, toDelete);
            var answer    = MessageBox.Show(
                $"Delete preview:\nTarget: {TargetTag}\nItems matched: {toDelete.Count}\n" +
                $"Files to delete: {deletable}\n\nYes = delete  |  No = dry-run  |  Cancel = abort",
                "Confirm Delete", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);

            if (answer == MessageBoxResult.Cancel)
            { AppendLog("Delete cancelled."); StatusText = "Delete cancelled."; return; }

            if (answer == MessageBoxResult.No)
            {
                AppendLog($"Dry-run: {deletable} files would be deleted.");
                var sample = toDelete.Where(r => !_comparer.IsExcluded(r)).Take(20).ToList();
                if (sample.Any()) { AppendLog("Sample:"); foreach (var r in sample) AppendLog("  " + r); }
                else AppendLog("All matched items are excluded.");
                StatusText = $"Dry-run: {deletable} file(s) would be deleted.";
                return;
            }

            StatusText = "Deleting…";
            bool recycle = RecycleBin;
            int deleted  = await _comparer.DeleteAsync(LeftPath, RightPath, TargetTag,
                toDelete, KeepRoot, _cts!.Token, recycle);
            _lastDeleted = deleted;
            string dest  = recycle ? "Recycle Bin" : "permanently";
            AppendLog($"Delete complete. Files deleted ({dest}): {deleted}");
            StatusText = $"Deleted {deleted} file(s) to {dest}.";

            MessageBox.Show(
                $"Delete completed.\n\nFiles deleted: {deleted}\nDestination: {dest}\n\nResults will now refresh.",
                "Delete Completed", MessageBoxButton.OK, MessageBoxImage.Information);

            AppendLog("Refreshing analysis after delete…");
            await RunAnalysisAsync();
        }

        private void DoCancel()
        {
            _cts?.Cancel();
            RelayCommand.Refresh();
        }

        // ════════════════════════════════════════════════════════════════════════
        //  BUILD RESULTS
        // ════════════════════════════════════════════════════════════════════════
        private async Task BuildResultsAsync(CompareResult result)
        {
            var tempResults = await Task.Run(() =>
            {
                var list = new List<ResultRow>();
                var all  = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                all.UnionWith(result.Same);
                all.UnionWith(result.LeftOnly);
                all.UnionWith(result.RightOnly);
                all.UnionWith(result.Different);

                foreach (var rel in all)
                {
                    string cat =
                        result.Same.Contains(rel)      ? "Same"         :
                        result.Different.Contains(rel) ? "Different"    :
                        result.LeftOnly.Contains(rel)  ? "Left-Orphan"  :
                        result.RightOnly.Contains(rel) ? "Right-Orphan" : "Unknown";

                    string lf = Path.Combine(LeftPath,  rel);
                    string rf = Path.Combine(RightPath, rel);
                    list.Add(new ResultRow
                    {
                        Relative  = rel,
                        Category  = cat,
                        LeftFull  = File.Exists(lf) ? lf : "",
                        RightFull = File.Exists(rf) ? rf : ""
                    });
                }
                return list;
            });

            _allResults   = tempResults;
            _currentIndex = 0;
            _resultItems.Clear();
            ApplyFilter();
            await LoadNextBatchAsync();
        }

        private async Task LoadNextBatchAsync()
        {
            if (_allResults == null || _currentIndex >= _allResults.Count) return;
            int count = Math.Min(BatchSize, _allResults.Count - _currentIndex);
            var slice = _allResults.Skip(_currentIndex).Take(count).ToList();
            _currentIndex += count;

            foreach (var row in slice)
            {
                _resultItems.Add(row);
                if (_resultItems.Count % 50 == 0)
                    await Application.Current.Dispatcher.InvokeAsync(
                        () => { }, DispatcherPriority.Background);
            }
        }

        public void OnResultsScrolledToEnd()
        {
            if (_isLoading || _currentIndex >= _allResults.Count) return;
            _isLoading = true;
            _ = LoadNextBatchAsync().ContinueWith(_ => _isLoading = false);
        }

        // ════════════════════════════════════════════════════════════════════════
        //  FILTER + SORT
        // ════════════════════════════════════════════════════════════════════════
        private void ApplyFilter()
        {
            if (ResultsView == null) return;
            if (string.IsNullOrWhiteSpace(FilterText))
                ResultsView.Filter = null;
            else
            {
                string lower   = FilterText.ToLowerInvariant();
                ResultsView.Filter = obj => obj is ResultRow r &&
                    (r.Relative.ToLowerInvariant().Contains(lower) ||
                     r.Category.ToLowerInvariant().Contains(lower));
            }
        }

        private void ToggleSort(string property)
        {
            var dir = (_lastSortProp == property && _sortDir == ListSortDirection.Ascending)
                ? ListSortDirection.Descending : ListSortDirection.Ascending;

            ResultsView.SortDescriptions.Clear();
            ResultsView.SortDescriptions.Add(new SortDescription(property, dir));
            ResultsView.Refresh();

            _lastSortProp = property;
            _sortDir      = dir;
            string arrow  = dir == ListSortDirection.Ascending ? " ▲" : " ▼";

            SortLeftHeader     = "Left file"  + (property == "LeftFull"  ? arrow : "");
            SortCategoryHeader = "Category"   + (property == "Category"  ? arrow : "");
            SortRightHeader    = "Right file"  + (property == "RightFull" ? arrow : "");
        }

        // ════════════════════════════════════════════════════════════════════════
        //  BROWSE HANDLERS
        // ════════════════════════════════════════════════════════════════════════
        private void DoBrowseLog()
        {
            var p = _dialogs.BrowseSaveFile("Select log file",
                "Text files (*.txt)|*.txt|All files (*.*)|*.*", "CompareLog.txt", LogPath);
            if (p != null) LogPath = p;
        }

        private void DoBrowseExcel()
        {
            var p = _dialogs.BrowseSaveFile("Select Excel file",
                "Excel Workbook (*.xlsx)|*.xlsx", "CompareResults.xlsx", ExcelPath);
            if (p != null) ExcelPath = p;
        }

        private void DoBrowseBcPath()
        {
            string? startDir = null;
            if (!string.IsNullOrWhiteSpace(BcPath))
                try { startDir = Path.GetDirectoryName(BcPath); } catch { }
            startDir ??= @"C:\Program Files\Beyond Compare 4";

            var p = _dialogs.BrowseOpenFile("Locate BCompare.exe",
                "BCompare.exe|BCompare.exe|All executables (*.exe)|*.exe", startDir);
            if (p != null) { BcPath = p; UpdateBcStatus(); }
        }

        private void DoSaveBcPath()
        {
            string path = BcPath.Trim();
            if (!string.IsNullOrEmpty(path) && !File.Exists(path))
            {
                MessageBox.Show($"File not found:\n{path}", "Invalid Path",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            SaveSettings();
            UpdateBcStatus();
            AppendLog(string.IsNullOrEmpty(path)
                ? "BC path cleared — will auto-detect."
                : $"BC path saved: {path}");
        }

        // ════════════════════════════════════════════════════════════════════════
        //  BEYOND COMPARE HELPERS
        // ════════════════════════════════════════════════════════════════════════
        private void LaunchBc(string? path)
        {
            if (string.IsNullOrEmpty(path)) return;
            string? exe = _bc.FindExecutable(BcPath);
            if (exe == null) { NoBc(); return; }
            try { _bc.OpenFile(exe, path); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void DoCompareBc()
        {
            if (SelectedRow == null) return;
            string? exe = _bc.FindExecutable(BcPath);
            if (exe == null) { NoBc(); return; }
            if (string.IsNullOrEmpty(SelectedRow.LeftFull) || string.IsNullOrEmpty(SelectedRow.RightFull))
            {
                MessageBox.Show("Both left and right paths must exist to compare.", "Compare",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            try { _bc.CompareFiles(exe, SelectedRow.LeftFull, SelectedRow.RightFull); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        public void UpdateBcStatus()
        {
            string path  = BcPath.Trim();
            string? found = _bc.FindExecutable(path);
            if (string.IsNullOrEmpty(path))
            {
                BcStatusText       = found != null ? $"Auto: {Path.GetDirectoryName(found)}" : "Not found";
                BcStatusForeground = found != null ? "Green" : "OrangeRed";
            }
            else if (File.Exists(path))
            {
                BcStatusText       = "✓ Found";
                BcStatusForeground = "Green";
            }
            else
            {
                BcStatusText       = "✗ Not found";
                BcStatusForeground = "OrangeRed";
            }
        }

        private static void NoBc() =>
            MessageBox.Show("Beyond Compare was not found.\n\nInstall it or set the path in Options.",
                "Beyond Compare Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);

        // ════════════════════════════════════════════════════════════════════════
        //  EXCEL EXPORT
        // ════════════════════════════════════════════════════════════════════════
        private async Task RunExportExcelAsync()
        {
            string path = ExcelPath;
            if (_excel.IsFileLocked(path))
            {
                MessageBox.Show("The Excel file is open. Close it and try again.",
                    "File In Use", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(path))
            {
                var p = _dialogs.BrowseSaveFile("Export to Excel",
                    "Excel Workbook (*.xlsx)|*.xlsx", "CompareResults.xlsx");
                if (p == null) return;
                ExcelPath = path = p;
            }

            _comparer.SetExcludePatterns(ParsePatterns(ExcludePatterns));

            string tagDesc = ExcelCategoryTag switch
            {
                "1" => "Same", "2" => "Left-only", "3" => "Right-only", "4" => "Different", _ => "All"
            };

            IEnumerable<ResultRow> toExport = ExcelCategoryTag switch
            {
                "1" => _allResults.Where(r => r.Category == "Same"),
                "2" => _allResults.Where(r => r.Category == "Left-Orphan"),
                "3" => _allResults.Where(r => r.Category == "Right-Orphan"),
                "4" => _allResults.Where(r => r.Category == "Different"),
                _   => _allResults
            };

            var filtered = toExport.Where(r => !_comparer.IsExcluded(r.Relative)).ToList();
            if (!filtered.Any())
            {
                MessageBox.Show("No items after applying exclude patterns.", "Export",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            int totalU  = filtered.Select(r => r.Relative).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            int sameC   = filtered.Count(r => r.Category == "Same");
            int leftC   = filtered.Count(r => r.Category == "Left-Orphan");
            int rightC  = filtered.Count(r => r.Category == "Right-Orphan");
            int diffC   = filtered.Count(r => r.Category == "Different");

            try
            {
                AppendLog($"Exporting {filtered.Count:N0} rows to Excel…");
                StatusText = "Exporting to Excel…";
                string sheet = await Task.Run(() => _excel.Export(path,
                    LeftPath, RightPath, tagDesc, filtered,
                    totalU, sameC, leftC, rightC, diffC,
                    _lastCopied, _lastDeleted, SheetName));

                AppendLog($"Excel exported: {path} (sheet: {sheet})");
                StatusText = $"Excel exported — sheet: {sheet}";

                if (MessageBox.Show($"Export complete:\n{path}\nSheet: {sheet}\n\nOpen file location?",
                        "Export Complete", MessageBoxButton.YesNo, MessageBoxImage.Information)
                    == MessageBoxResult.Yes)
                {
                    string? folder = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(folder))
                        Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                AppendLog($"Excel export failed: {ex.Message}");
                MessageBox.Show($"Export failed: {ex.Message}", "Export Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  EXPLORER REVEAL
        // ════════════════════════════════════════════════════════════════════════
        public static void RevealInExplorer(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName        = "explorer.exe",
                    Arguments       = $"/select,\"{path}\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open Explorer: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  PROGRESS + LOG
        // ════════════════════════════════════════════════════════════════════════
        private void OnProgressChanged(ProgressInfo info)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (info.Total > 0)
                {
                    ProgressMax   = info.Total;
                    ProgressValue = Math.Min(info.Processed, info.Total);
                }
                StatusText = info.Message ?? "";
            });
        }

        private void OnLogMessage(string msg)
        {
            Application.Current.Dispatcher.Invoke(() => AppendLog(msg));
        }

        public void AppendLog(string msg)
        {
            var lines = LogText.Split('\n');
            string trimmed = lines.Length > MaxLogLines
                ? string.Join('\n', lines.Skip(lines.Length - MaxLogLines))
                : LogText;
            LogText = trimmed + msg + Environment.NewLine;
        }

        // ════════════════════════════════════════════════════════════════════════
        //  VISIBILITY HELPERS
        // ════════════════════════════════════════════════════════════════════════
        private void UpdateModeVisibility()
        {
            bool isCopy   = ModeTag == "3";
            bool isDelete = !isCopy;
            CopyGroupVisibility  = isCopy   ? "Visible" : "Collapsed";
            KeepRootVisibility   = isDelete ? "Visible" : "Collapsed";
            SameBothVisibility   = isDelete ? "Visible" : "Collapsed";
            SameLeftVisibility   = isDelete ? "Visible" : "Collapsed";
            SameRightVisibility  = isDelete ? "Visible" : "Collapsed";
            CopyLeftVisibility   = isCopy   ? "Visible" : "Collapsed";
            CopyRightVisibility  = isCopy   ? "Visible" : "Collapsed";
        }

        private void UpdateLogPathVisibility()
        {
            LogPathVisibility = EnableLogging ? "Visible" : "Collapsed";
        }

        // ════════════════════════════════════════════════════════════════════════
        //  VALIDATION + HELPERS
        // ════════════════════════════════════════════════════════════════════════
        private bool ValidatePaths(string left, string right)
        {
            if (!Directory.Exists(left) || !Directory.Exists(right))
            {
                MessageBox.Show("Left or right folder path is invalid.", "Invalid Paths",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            string lf = Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar);
            string rf = Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar);
            if (string.Equals(lf, rf, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("Left and Right folders cannot be the same path.", "Same Path",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            if (rf.StartsWith(lf + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                lf.StartsWith(rf + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("One folder cannot be a subfolder of the other.", "Nested Paths",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            return true;
        }

        private static IEnumerable<string> ParsePatterns(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return Enumerable.Empty<string>();
            return text.Split(',', StringSplitOptions.RemoveEmptyEntries)
                       .Select(s => s.Trim().ToLowerInvariant())
                       .Where(s => !string.IsNullOrEmpty(s));
        }
    }
}
