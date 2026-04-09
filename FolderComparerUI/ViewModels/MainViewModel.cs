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
        private readonly FolderComparer        _comparer;
        private readonly ISettingsService      _settings;
        private readonly IDialogService        _dialogs;
        private readonly IExcelExportService   _excel;
        private readonly IBeyondCompareService _bc;

        private CancellationTokenSource? _cts;
        private const int BatchSize    = 200;
        private const int MaxLogLines  = 1000;
        private int  _currentIndex     = 0;
        private bool _isLoading        = false;

        // #3 — StringBuilder log: O(1) append, never rebuilds entire string
        private readonly System.Text.StringBuilder _logBuffer = new();
        private int _logLineCount = 0;

        // ── Result data ───────────────────────────────────────────────────────
        private List<ResultRow>                          _allResults  = new();
        private readonly ObservableCollection<ResultRow> _resultItems = new();
        public  ICollectionView ResultsView { get; }

        private CompareResult? _lastCompareResult;
        private int _lastCopied  = 0;
        private int _lastDeleted = 0;

        // WiredMenus lives in MainWindow.xaml.cs — it tracks ContextMenu instances
        // that have already had Commands wired. It is purely a view-side concern.

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
        private string _leftFilterText     = "";
        private string _rightFilterText    = "";
        private bool   _showSame          = true;
        private bool   _showDifferent     = true;
        private bool   _showLeftOrphan    = true;
        private bool   _showRightOrphan   = true;
        private string _resultCountText    = "";
        private string _logText            = "";
        private string _statusText         = "Ready";
        private string _bcStatusText       = "";
        private double _progressMax        = 1;
        private double _progressValue      = 0;
        private bool   _enableLogging      = false;
        private bool   _smartXml           = false;
        private bool   _forceReread        = false;
        private string _compareModeTag     = "Normal";
        private bool   _topmost            = false;
        private bool   _keepRoot           = false;
        private bool   _categorySubfolders = false;
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
        private string _diffBothVisibility     = "Visible";    // hidden in Copy mode
        private string _sameCopyVisibility     = "Collapsed";  // shown in Copy mode only

        public string LeftPath  { get => _leftPath;  set { if (SetField(ref _leftPath,  value)) _comparer.ClearHashCache(); } }
        public string RightPath { get => _rightPath; set { if (SetField(ref _rightPath, value)) _comparer.ClearHashCache(); } }
        public string ExcludePatterns    { get => _excludePatterns;    set { SetField(ref _excludePatterns, value);    } }
        public string LogPath            { get => _logPath;            set { SetField(ref _logPath, value);            } }
        public string ExcelPath          { get => _excelPath;          set { SetField(ref _excelPath, value);          } }
        public string SheetName          { get => _sheetName;          set { SetField(ref _sheetName, value);          } }
        public string ExportPath         { get => _exportPath;         set { SetField(ref _exportPath, value);         } }
        public string BcPath             { get => _bcPath;             set { SetField(ref _bcPath, value); UpdateBcStatus(); } }
        public string FilterText          { get => _filterText;       set { SetField(ref _filterText, value); ApplyFilter(); OnPropertyChanged(nameof(FilterHasText)); } }
        public string LeftFilterText      { get => _leftFilterText;   set { SetField(ref _leftFilterText, value); ApplyFilter(); OnPropertyChanged(nameof(FilterHasText)); } }
        public string RightFilterText     { get => _rightFilterText;  set { SetField(ref _rightFilterText, value); ApplyFilter(); OnPropertyChanged(nameof(FilterHasText)); } }
        public bool   ShowSame        { get => _showSame;        set { SetField(ref _showSame, value);        ApplyFilter(); OnPropertyChanged(nameof(FilterHasText)); } }
        public bool   ShowDifferent   { get => _showDifferent;   set { SetField(ref _showDifferent, value);   ApplyFilter(); OnPropertyChanged(nameof(FilterHasText)); } }
        public bool   ShowLeftOrphan  { get => _showLeftOrphan;  set { SetField(ref _showLeftOrphan, value);  ApplyFilter(); OnPropertyChanged(nameof(FilterHasText)); } }
        public bool   ShowRightOrphan { get => _showRightOrphan; set { SetField(ref _showRightOrphan, value); ApplyFilter(); OnPropertyChanged(nameof(FilterHasText)); } }
        public string FilterHasText       => (string.IsNullOrWhiteSpace(_filterText) &&
                                              string.IsNullOrWhiteSpace(_leftFilterText) &&
                                              string.IsNullOrWhiteSpace(_rightFilterText) &&
                                              _showSame && _showDifferent && _showLeftOrphan && _showRightOrphan)
                                                ? "Collapsed" : "Visible";  // #8
        public string ResultCountText    { get => _resultCountText; set { SetField(ref _resultCountText, value);             } }
        public string LogText            { get => _logText;         set { SetField(ref _logText, value);                    } }
        public string StatusText         { get => _statusText;      set { SetField(ref _statusText, value);                 } }
        public string BcStatusText       { get => _bcStatusText;       set { SetField(ref _bcStatusText, value);       } }
        public string BcStatusForeground { get => _bcStatusForeground; set { SetField(ref _bcStatusForeground, value); } }
        public double ProgressMax        { get => _progressMax;        set { SetField(ref _progressMax, value);        } }
        public double ProgressValue      { get => _progressValue;      set { SetField(ref _progressValue, value);      } }
        public bool   EnableLogging  { get => _enableLogging;  set { SetField(ref _enableLogging, value); UpdateLogPathVisibility(); } }
        public bool   SmartXml       { get => _smartXml;       set { SetField(ref _smartXml, value);       } }
        public bool   ForceReread    { get => _forceReread;    set { SetField(ref _forceReread, value);    } }
        /// <summary>"Normal" = size short-circuit + hash.  "HashOnly" = always hash.</summary>
        public string CompareModeTag { get => _compareModeTag; set { SetField(ref _compareModeTag, value); } }
        public bool   Topmost            { get => _topmost;            set { SetField(ref _topmost, value);            } }
        public bool   KeepRoot           { get => _keepRoot;           set { SetField(ref _keepRoot, value);           } }
        public bool   CategorySubfolders { get => _categorySubfolders; set { SetField(ref _categorySubfolders, value); } }
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
        public string DiffBothVisibility     { get => _diffBothVisibility;     set { SetField(ref _diffBothVisibility, value);     } }
        public string SameCopyVisibility     { get => _sameCopyVisibility;     set { SetField(ref _sameCopyVisibility, value);     } }

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
        public RelayCommand      ClearFilterCommand  { get; }  // #8
        // Context menu commands — navigate / BC
        public RelayCommand OpenLeftExplorerCommand   { get; }
        public RelayCommand OpenRightExplorerCommand  { get; }
        public RelayCommand OpenLeftBcCommand         { get; }
        public RelayCommand OpenRightBcCommand        { get; }
        public RelayCommand CompareBcCommand          { get; }

        // Context menu commands — quick single-file actions
        public AsyncRelayCommand QuickCopyToRightCommand { get; }
        public AsyncRelayCommand QuickCopyToLeftCommand  { get; }
        public AsyncRelayCommand QuickDeleteLeftCommand  { get; }
        public AsyncRelayCommand QuickDeleteRightCommand { get; }
        public AsyncRelayCommand QuickDeleteBothCommand  { get; }

        // ════════════════════════════════════════════════════════════════════════
        //  CONSTRUCTOR
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>Production constructor — uses real implementations.</summary>
        public MainViewModel()
            : this(new FolderComparer(), new SettingsService(),
                   new DialogService(), new ExcelExportService(),
                   new BeyondCompareService()) { }

        /// <summary>Testable constructor — all dependencies injected via interfaces.</summary>
        public MainViewModel(
            FolderComparer         comparer,
            ISettingsService       settings,
            IDialogService         dialogs,
            IExcelExportService    excel,
            IBeyondCompareService  bc)
        {
            _comparer = comparer;
            _settings = settings;
            _dialogs  = dialogs;
            _excel    = excel;
            _bc       = bc;

            ResultsView = CollectionViewSource.GetDefaultView(_resultItems);
            _comparer.ProgressChanged += OnProgressChanged;
            _comparer.LogMessage      += OnLogMessage;

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
            ClearFilterCommand  = new RelayCommand(() =>
            {
                FilterText = "";
                LeftFilterText = "";
                RightFilterText = "";
                ShowSame = true;
                ShowDifferent = true;
                ShowLeftOrphan = true;
                ShowRightOrphan = true;
            });  // #8
            OpenLeftExplorerCommand  = new RelayCommand(() => { if (SelectedRow?.LeftFull  is string p && !string.IsNullOrEmpty(p)) RevealInExplorer(p); });
            OpenRightExplorerCommand = new RelayCommand(() => { if (SelectedRow?.RightFull is string p && !string.IsNullOrEmpty(p)) RevealInExplorer(p); });
            OpenLeftBcCommand        = new RelayCommand(() => LaunchBc(SelectedRow?.LeftFull));
            OpenRightBcCommand       = new RelayCommand(() => LaunchBc(SelectedRow?.RightFull));
            CompareBcCommand         = new RelayCommand(DoCompareBc,
                                           () => !string.IsNullOrEmpty(SelectedRow?.LeftFull) &&
                                                 !string.IsNullOrEmpty(SelectedRow?.RightFull));

            // Quick single-file actions from context menu
            QuickCopyToRightCommand  = new AsyncRelayCommand(() => QuickCopyAsync(toRight: true),  () => SelectedRow != null);
            QuickCopyToLeftCommand   = new AsyncRelayCommand(() => QuickCopyAsync(toRight: false), () => SelectedRow != null);
            QuickDeleteLeftCommand   = new AsyncRelayCommand(() => QuickDeleteAsync(left: true,  right: false), () => SelectedRow != null);
            QuickDeleteRightCommand  = new AsyncRelayCommand(() => QuickDeleteAsync(left: false, right: true),  () => SelectedRow != null);
            QuickDeleteBothCommand   = new AsyncRelayCommand(() => QuickDeleteAsync(left: true,  right: true),  () => SelectedRow != null);
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
            ModeTag            = s.ModeTag        ?? "2";
            TargetTag          = s.TargetTag       ?? "5";
            ExcelCategoryTag   = s.ExcelCategoryTag ?? "5";
            CompareModeTag     = s.CompareModeTag   ?? "Normal";
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
                ModeTag            = ModeTag,
                TargetTag          = TargetTag,
                ExcelCategoryTag   = ExcelCategoryTag,
                CompareModeTag     = CompareModeTag
            });
        }

        // ════════════════════════════════════════════════════════════════════════
        //  ANALYSIS
        // ════════════════════════════════════════════════════════════════════════
        private async Task RunAnalysisAsync()
        {
            if (!ValidatePaths(LeftPath, RightPath)) return;

            _logBuffer.Clear(); _logLineCount = 0; LogText = "";

            // Clear hash cache only when ForceReread is explicitly requested.
            // Normal reloads let the cache's size+timestamp check handle
            // invalidation — only truly changed files are re-hashed.
            if (ForceReread)
            {
                _comparer.ClearHashCache();
                AppendLog("⚠ Force re-read: hash cache cleared — all files will be re-read from disk.");
            }

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
            _comparer.CompareMode     = CompareModeTag == "HashOnly"
                ? FolderComparerLib.CompareMode.HashOnly
                : FolderComparerLib.CompareMode.Normal;

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
                // #9 — persistent result count in status bar
                ResultCountText = $"{total:N0} files  ·  Same: {result.Same.Count:N0}  Left-only: {result.LeftOnly.Count:N0}  Right-only: {result.RightOnly.Count:N0}  Different: {result.Different.Count:N0}";

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
            catch (OperationCanceledException)
            {
                AppendLog("Analysis cancelled by user.");
                StatusText = "Cancelled.";
            }
            catch (Exception ex)
            {
                string msg = $"Analysis failed: {ex.GetType().Name} — {ex.Message}";
                AppendLog(msg);
                StatusText = "Analysis failed — see log.";
                MessageBox.Show(
                    $"The comparison could not complete.\n\n{ex.Message}\n\n" +
                    "Check that both folders are accessible and not being modified by another process.",
                    "Analysis Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
            _comparer.CompareMode              = CompareModeTag == "HashOnly"
                ? FolderComparerLib.CompareMode.HashOnly
                : FolderComparerLib.CompareMode.Normal;
            _comparer.CreateCategorySubfolders = CategorySubfolders;
            _comparer.SetExcludePatterns(ParsePatterns(ExcludePatterns));

            _logBuffer.Clear(); _logLineCount = 0; LogText = "";
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
            catch (OperationCanceledException)
            {
                AppendLog("Operation cancelled by user.");
                StatusText = "Cancelled.";
            }
            catch (Exception ex)
            {
                string msg = $"Operation failed: {ex.GetType().Name} — {ex.Message}";
                AppendLog(msg);
                StatusText = "Operation failed — see log.";
                MessageBox.Show(
                    $"The operation could not complete.\n\n{ex.Message}\n\n" +
                    "Check that the folders are accessible and you have the required permissions.",
                    "Operation Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { _cts = null; RelayCommand.Refresh(); }
        }

        private async Task DoCopyAsync()
        {
            string exportRoot = string.IsNullOrWhiteSpace(ExportPath)
                ? Path.Combine(Environment.CurrentDirectory, "Output") : ExportPath;

            // Result-based targets (not CL/CR) require a prior analysis
            bool needsResult = TargetTag != "CL" && TargetTag != "CR";
            if (needsResult && _lastCompareResult == null)
            {
                MessageBox.Show("Run Start Comparison first before copying.",
                    "No Analysis", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Provide an empty result for CL/CR mode so ExportAsync signature is satisfied
            var result = _lastCompareResult ?? new FolderComparerLib.CompareResult();

            AppendLog($"Starting copy to: {exportRoot}");
            StatusText = "Copying…";

            int copied = await _comparer.ExportAsync(LeftPath, RightPath, TargetTag,
                result, exportRoot, _cts!.Token);
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
            string info   = $"Target: {TargetTag}\nItems matched: {toDelete.Count}\nFiles to delete: {deletable}\n\n" +
                            "Delete = perform deletion\nDry-run = preview only\nCancel = abort";

            // Show custom dialog on the UI thread (ViewModel must not reference Window types directly)
            Views.DeleteConfirmResult dialogResult = Views.DeleteConfirmResult.Cancel;
            bool useRecycleBin = true;

            Application.Current.Dispatcher.Invoke(() =>
            {
                var dlg = new Views.DeleteConfirmDialog(info)
                {
                    Owner = Application.Current.MainWindow
                };
                dlg.ShowDialog();
                dialogResult  = dlg.Result;
                useRecycleBin = dlg.UseRecycleBin;
            });

            if (dialogResult == Views.DeleteConfirmResult.Cancel)
            {
                AppendLog("Delete cancelled.");
                StatusText = "Delete cancelled.";
                return;
            }

            if (dialogResult == Views.DeleteConfirmResult.DryRun)
            {
                AppendLog($"Dry-run: {deletable} files would be deleted.");
                var sample = toDelete.Where(r => !_comparer.IsExcluded(r)).Take(20).ToList();
                if (sample.Any()) { AppendLog("Sample:"); foreach (var r in sample) AppendLog("  " + r); }
                else AppendLog("All matched items are excluded.");
                StatusText = $"Dry-run: {deletable} file(s) would be deleted.";
                return;
            }

            // Proceed with actual deletion
            StatusText = "Deleting…";
            int deleted = await _comparer.DeleteAsync(LeftPath, RightPath, TargetTag,
                toDelete, KeepRoot, _cts!.Token, useRecycleBin);
            _lastDeleted = deleted;
            string dest  = useRecycleBin ? "Recycle Bin" : "permanently";
            AppendLog($"Delete complete. Files deleted ({dest}): {deleted}");
            StatusText = $"Deleted {deleted} file(s) → {dest}.";

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
        //  QUICK SINGLE-FILE ACTIONS  (right-click context menu)
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Copies the selected file from one side to the other, mirroring the
        /// same relative path. On success the row's category is updated in-place
        /// to "Same" — no full reload needed.
        /// </summary>
        private async Task QuickCopyAsync(bool toRight)
        {
            var row = SelectedRow;
            if (row == null) return;

            string src  = toRight ? row.LeftFull  : row.RightFull;
            string root = toRight ? RightPath      : LeftPath;

            if (string.IsNullOrEmpty(src))
            {
                MessageBox.Show(
                    $"The {(toRight ? "left" : "right")}-side file path is not available for this row.",
                    "Cannot Copy", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string dest = Path.Combine(root, row.Relative);

            try
            {
                StatusText = $"Copying {Path.GetFileName(src)}…";
                await Task.Run(() =>
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    File.Copy(src, dest, overwrite: true);
                });

                AppendLog($"Quick copy → {dest}");

                // Update the row in-place: it now exists on both sides as Same
                var updated = new ResultRow
                {
                    Relative  = row.Relative,
                    Category  = "Same",
                    LeftFull  = toRight ? row.LeftFull : dest,
                    RightFull = toRight ? dest         : row.RightFull
                };
                ReplaceRow(row, updated);

                StatusText = $"Copied to {(toRight ? "right" : "left")}: {row.Relative}";
            }
            catch (Exception ex)
            {
                AppendLog($"Quick copy failed: {ex.Message}");
                MessageBox.Show(
                    $"Copy failed.\n\n{ex.Message}",
                    "Copy Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText = "Copy failed — see log.";
            }
        }

        /// <summary>
        /// Deletes the selected file from the left side, right side, or both.
        /// Always sends to the Recycle Bin — no confirmation dialog needed.
        /// On success the row is removed from the results list entirely.
        /// </summary>
        private async Task QuickDeleteAsync(bool left, bool right)
        {
            var row = SelectedRow;
            if (row == null) return;

            string leftFile  = Path.Combine(LeftPath,  row.Relative);
            string rightFile = Path.Combine(RightPath, row.Relative);

            bool leftExists  = left  && File.Exists(leftFile);
            bool rightExists = right && File.Exists(rightFile);

            if (!leftExists && !rightExists)
            {
                MessageBox.Show("The file no longer exists on the selected side(s).",
                    "File Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string sides = left && right ? "both sides"
                         : left  ? "left side"
                         : "right side";

            try
            {
                StatusText = $"Deleting {Path.GetFileName(row.Relative)} from {sides}…";
                int deleted = 0;

                await Task.Run(() =>
                {
                    if (leftExists)
                    {
                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                            leftFile,
                            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                        deleted++;
                    }
                    if (rightExists)
                    {
                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                            rightFile,
                            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                        deleted++;
                    }
                });

                AppendLog($"Quick delete ({sides}): {row.Relative} → Recycle Bin ({deleted} file(s))");

                if (left && right)
                {
                    // Both sides deleted — remove the row entirely
                    RemoveRow(row);
                }
                else
                {
                    // One side deleted — row becomes an orphan on the remaining side
                    var updated = new ResultRow
                    {
                        Relative  = row.Relative,
                        Category  = left ? "Right-Orphan" : "Left-Orphan",
                        LeftFull  = left  ? "" : row.LeftFull,
                        RightFull = right ? "" : row.RightFull
                    };
                    ReplaceRow(row, updated);
                }

                StatusText = $"Deleted from {sides}: {row.Relative}";
            }
            catch (Exception ex)
            {
                AppendLog($"Quick delete failed: {ex.Message}");
                MessageBox.Show(
                    $"Delete failed.\n\n{ex.Message}",
                    "Delete Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText = "Delete failed — see log.";
            }
        }

        /// <summary>Replaces a row in both _allResults and _resultItems.</summary>
        private void ReplaceRow(ResultRow old, ResultRow updated)
        {
            int ai = _allResults.IndexOf(old);
            if (ai >= 0) _allResults[ai] = updated;

            int ri = _resultItems.IndexOf(old);
            if (ri >= 0) _resultItems[ri] = updated;
        }

        /// <summary>Removes a row from both _allResults and _resultItems.</summary>
        private void RemoveRow(ResultRow row)
        {
            _allResults.Remove(row);
            _resultItems.Remove(row);

            // Update the persistent count in the status bar
            int total = _allResults.Count;
            int same  = _allResults.Count(r => r.Category == "Same");
            int lonly = _allResults.Count(r => r.Category == "Left-Orphan");
            int ronly = _allResults.Count(r => r.Category == "Right-Orphan");
            int diff  = _allResults.Count(r => r.Category == "Different");
            ResultCountText =
                $"{total:N0} files  ·  Same: {same:N0}  " +
                $"Left-only: {lonly:N0}  Right-only: {ronly:N0}  Different: {diff:N0}";
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
            string global = (FilterText ?? "").Trim().ToLowerInvariant();
            string left   = (LeftFilterText ?? "").Trim().ToLowerInvariant();
            string right  = (RightFilterText ?? "").Trim().ToLowerInvariant();

            bool allCats = _showSame && _showDifferent && _showLeftOrphan && _showRightOrphan;

            if (string.IsNullOrWhiteSpace(global) &&
                string.IsNullOrWhiteSpace(left) &&
                string.IsNullOrWhiteSpace(right) &&
                allCats)
                ResultsView.Filter = null;
            else
            {
                ResultsView.Filter = obj => obj is ResultRow r &&
                    // Category checkbox filter
                    (r.Category == "Same"         ? _showSame        :
                     r.Category == "Different"    ? _showDifferent   :
                     r.Category == "Left-Orphan"  ? _showLeftOrphan  :
                     r.Category == "Right-Orphan" ? _showRightOrphan : true) &&
                    // Text filters
                    (string.IsNullOrWhiteSpace(global) ||
                        r.Relative.ToLowerInvariant().Contains(global) ||
                        r.Category.ToLowerInvariant().Contains(global)) &&
                    (string.IsNullOrWhiteSpace(left) ||
                        r.LeftFull.ToLowerInvariant().Contains(left) ||
                        r.Relative.ToLowerInvariant().Contains(left)) &&
                    (string.IsNullOrWhiteSpace(right) ||
                        r.RightFull.ToLowerInvariant().Contains(right) ||
                        r.Relative.ToLowerInvariant().Contains(right));
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
            // Guard: no analysis run yet
            if (!_allResults.Any())
            {
                MessageBox.Show("Run Start Comparison first before exporting.",
                    "No Results", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Resolve path — ask user if none set
            string path = ExcelPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                var p = _dialogs.BrowseSaveFile("Export to Excel",
                    "Excel Workbook (*.xlsx)|*.xlsx", "CompareResults.xlsx");
                if (p == null) return;
                ExcelPath = path = p;
            }

            // Check AFTER resolving path
            if (_excel.IsFileLocked(path))
            {
                MessageBox.Show($"The file is currently open in Excel. Close it and try again.\n\n{path}",
                    "File In Use", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
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
                MessageBox.Show("No items match the selected category after applying exclude patterns.",
                    "Nothing to Export", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            int totalU = filtered.Select(r => r.Relative).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            int sameC  = filtered.Count(r => r.Category == "Same");
            int leftC  = filtered.Count(r => r.Category == "Left-Orphan");
            int rightC = filtered.Count(r => r.Category == "Right-Orphan");
            int diffC  = filtered.Count(r => r.Category == "Different");

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

                if (MessageBox.Show(
                        $"Export complete.\n\nFile: {path}\nSheet: {sheet}\n\nOpen file location?",
                        "Export Complete", MessageBoxButton.YesNo, MessageBoxImage.Information)
                    == MessageBoxResult.Yes)
                {
                    string? folder = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(folder))
                    {
                        try
                        {
                            Process.Start(new ProcessStartInfo
                                { FileName = folder, UseShellExecute = true });
                        }
                        catch (Exception ex)
                        {
                            AppendLog($"Could not open folder: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppendLog($"Excel export failed: {ex.Message}");
                MessageBox.Show(
                    $"Export failed.\n\n{ex.Message}\n\nMake sure the file is not open in Excel.",
                    "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
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

        /// <summary>
        /// #3 — Appends one line to the log using a StringBuilder.
        /// Never rebuilds the whole string on each call.
        /// When the line count exceeds MaxLogLines the oldest ~10% are discarded.
        /// </summary>
        public void AppendLog(string msg)
        {
            _logBuffer.Append(msg).Append(Environment.NewLine);
            _logLineCount++;

            if (_logLineCount > MaxLogLines)
            {
                int keepLines = MaxLogLines - (MaxLogLines / 10); // keep 90%
                string full   = _logBuffer.ToString();
                int skip = 0, found = 0;
                for (int i = 0; i < full.Length; i++)
                {
                    if (full[i] == '\n') found++;
                    if (found == _logLineCount - keepLines) { skip = i + 1; break; }
                }
                _logBuffer.Clear().Append(full, skip, full.Length - skip);
                _logLineCount = keepLines;
            }

            LogText = _logBuffer.ToString();
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
            // Delete-only Same variants
            SameBothVisibility   = isDelete ? "Visible" : "Collapsed";
            SameLeftVisibility   = isDelete ? "Visible" : "Collapsed";
            SameRightVisibility  = isDelete ? "Visible" : "Collapsed";
            // Copy-only items
            CopyLeftVisibility   = isCopy   ? "Visible" : "Collapsed";
            CopyRightVisibility  = isCopy   ? "Visible" : "Collapsed";
            SameCopyVisibility   = isCopy   ? "Visible" : "Collapsed";  // "Same" in Copy mode
            // "Different (both sides)" is meaningless in Copy — hide it
            DiffBothVisibility   = isDelete ? "Visible" : "Collapsed";
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
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                MessageBox.Show("Please set both Left and Right folder paths before running.",
                    "Missing Paths", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (!Directory.Exists(left) || !Directory.Exists(right))
            {
                string missing = !Directory.Exists(left) && !Directory.Exists(right)
                    ? "Neither folder path exists."
                    : !Directory.Exists(left)
                        ? $"Left folder not found:\n{left}"
                        : $"Right folder not found:\n{right}";
                MessageBox.Show(missing, "Folder Not Found",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            try
            {
                string lf = Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar);
                string rf = Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar);

                if (string.Equals(lf, rf, StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show("Left and Right folders cannot be the same path.",
                        "Same Path", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                if (rf.StartsWith(lf + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    lf.StartsWith(rf + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show("One folder cannot be a subfolder of the other.",
                        "Nested Paths", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Invalid folder path: {ex.Message}",
                    "Invalid Path", MessageBoxButton.OK, MessageBoxImage.Warning);
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
