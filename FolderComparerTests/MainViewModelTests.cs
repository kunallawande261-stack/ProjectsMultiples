using FolderComparerLib;
using FolderComparerUI.Models;
using FolderComparerUI.Services;
using FolderComparerUI.ViewModels;
using Moq;
using System.ComponentModel;
using Xunit;

namespace FolderComparerTests
{
    /// <summary>
    /// Tests for MainViewModel using mocked services.
    /// No UI, no dialogs, no file system access for dialog-driven paths.
    /// </summary>
    public class MainViewModelTests
    {
        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Build a ViewModel with all four service dependencies mocked.
        /// Concrete FolderComparer is kept real so comparison logic is not stubbed out.
        /// </summary>
        private static (MainViewModel vm,
                        Mock<IDialogService>       dialogs,
                        Mock<IExcelExportService>  excel,
                        Mock<ISettingsService>     settings,
                        Mock<IBeyondCompareService> bc)
            BuildVm()
        {
            var dialogs  = new Mock<IDialogService>();
            var excel    = new Mock<IExcelExportService>();
            var settings = new Mock<ISettingsService>();
            var bc       = new Mock<IBeyondCompareService>();
            var comparer = new FolderComparer();

            // Sensible defaults so LoadSettings() doesn't fail
            settings.Setup(s => s.Load()).Returns(new AppSettings());

            var vm = new MainViewModel(comparer, settings.Object,
                                       dialogs.Object, excel.Object, bc.Object);
            return (vm, dialogs, excel, settings, bc);
        }

        // ── INotifyPropertyChanged ────────────────────────────────────────────

        [Fact]
        public void Setting_LeftPath_raises_PropertyChanged()
        {
            var (vm, _, _, _, _) = BuildVm();
            string? raised = null;
            vm.PropertyChanged += (_, e) => raised = e.PropertyName;

            vm.LeftPath = "C:\\left";

            Assert.Equal(nameof(MainViewModel.LeftPath), raised);
        }

        [Fact]
        public void Setting_same_value_does_not_raise_PropertyChanged()
        {
            var (vm, _, _, _, _) = BuildVm();
            vm.LeftPath = "C:\\path";
            int count = 0;
            vm.PropertyChanged += (_, _) => count++;

            vm.LeftPath = "C:\\path"; // same value

            Assert.Equal(0, count);
        }

        // ── Mode visibility ───────────────────────────────────────────────────

        [Fact]
        public void CopyGroupVisibility_is_Collapsed_when_Delete_mode_selected()
        {
            var (vm, _, _, _, _) = BuildVm();
            vm.ModeTag = "2"; // Delete
            Assert.Equal("Collapsed", vm.CopyGroupVisibility);
        }

        [Fact]
        public void CopyGroupVisibility_is_Visible_when_Copy_mode_selected()
        {
            var (vm, _, _, _, _) = BuildVm();
            vm.ModeTag = "3"; // Copy
            Assert.Equal("Visible", vm.CopyGroupVisibility);
        }

        [Fact]
        public void KeepRootVisibility_is_Visible_only_in_Delete_mode()
        {
            var (vm, _, _, _, _) = BuildVm();

            vm.ModeTag = "2";
            Assert.Equal("Visible", vm.KeepRootVisibility);

            vm.ModeTag = "3";
            Assert.Equal("Collapsed", vm.KeepRootVisibility);
        }

        [Fact]
        public void SameBothVisibility_is_visible_in_Delete_collapsed_in_Copy()
        {
            var (vm, _, _, _, _) = BuildVm();

            vm.ModeTag = "2";
            Assert.Equal("Visible", vm.SameBothVisibility);

            vm.ModeTag = "3";
            Assert.Equal("Collapsed", vm.SameBothVisibility);
        }

        [Fact]
        public void DiffBothVisibility_is_Visible_in_Delete_Collapsed_in_Copy()
        {
            var (vm, _, _, _, _) = BuildVm();

            vm.ModeTag = "2";
            Assert.Equal("Visible", vm.DiffBothVisibility);

            vm.ModeTag = "3";
            Assert.Equal("Collapsed", vm.DiffBothVisibility);
        }

        [Fact]
        public void SameCopyVisibility_is_Collapsed_in_Delete_Visible_in_Copy()
        {
            var (vm, _, _, _, _) = BuildVm();

            vm.ModeTag = "2";
            Assert.Equal("Collapsed", vm.SameCopyVisibility);

            vm.ModeTag = "3";
            Assert.Equal("Visible", vm.SameCopyVisibility);
        }

        [Fact]
        public void CopyLeft_and_CopyRight_visible_only_in_Copy_mode()
        {
            var (vm, _, _, _, _) = BuildVm();

            vm.ModeTag = "3";
            Assert.Equal("Visible",   vm.CopyLeftVisibility);
            Assert.Equal("Visible",   vm.CopyRightVisibility);

            vm.ModeTag = "2";
            Assert.Equal("Collapsed", vm.CopyLeftVisibility);
            Assert.Equal("Collapsed", vm.CopyRightVisibility);
        }

        // ── Log path visibility ───────────────────────────────────────────────

        [Fact]
        public void LogPathVisibility_is_Collapsed_when_logging_disabled()
        {
            var (vm, _, _, _, _) = BuildVm();
            vm.EnableLogging = false;
            Assert.Equal("Collapsed", vm.LogPathVisibility);
        }

        [Fact]
        public void LogPathVisibility_is_Visible_when_logging_enabled()
        {
            var (vm, _, _, _, _) = BuildVm();
            vm.EnableLogging = true;
            Assert.Equal("Visible", vm.LogPathVisibility);
        }

        // ── Browse commands ───────────────────────────────────────────────────

        [Fact]
        public void BrowseLeft_updates_LeftPath_when_dialog_returns_a_path()
        {
            var (vm, dialogs, _, _, _) = BuildVm();
            dialogs.Setup(d => d.BrowseFolder(It.IsAny<string>())).Returns("C:\\NewLeft");

            vm.BrowseLeftCommand.Execute(null);

            Assert.Equal("C:\\NewLeft", vm.LeftPath);
        }

        [Fact]
        public void BrowseLeft_keeps_existing_LeftPath_when_dialog_cancelled()
        {
            var (vm, dialogs, _, _, _) = BuildVm();
            vm.LeftPath = "C:\\Original";
            dialogs.Setup(d => d.BrowseFolder(It.IsAny<string>())).Returns((string?)null);

            vm.BrowseLeftCommand.Execute(null);

            Assert.Equal("C:\\Original", vm.LeftPath);
        }

        [Fact]
        public void BrowseRight_updates_RightPath_when_dialog_returns_a_path()
        {
            var (vm, dialogs, _, _, _) = BuildVm();
            dialogs.Setup(d => d.BrowseFolder(It.IsAny<string>())).Returns("C:\\NewRight");

            vm.BrowseRightCommand.Execute(null);

            Assert.Equal("C:\\NewRight", vm.RightPath);
        }

        [Fact]
        public void BrowseExport_updates_ExportPath_when_dialog_returns_a_path()
        {
            var (vm, dialogs, _, _, _) = BuildVm();
            dialogs.Setup(d => d.BrowseFolder(It.IsAny<string>())).Returns("C:\\Export");

            vm.BrowseExportCommand.Execute(null);

            Assert.Equal("C:\\Export", vm.ExportPath);
        }

        // ── FilterText ────────────────────────────────────────────────────────

        [Fact]
        public void Setting_FilterText_raises_PropertyChanged()
        {
            var (vm, _, _, _, _) = BuildVm();
            bool raised = false;
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.FilterText)) raised = true;
            };

            vm.FilterText = "search";

            Assert.True(raised);
        }

        // ── Sort headers ──────────────────────────────────────────────────────

        [Fact]
        public void SortLeft_command_appends_arrow_to_SortLeftHeader()
        {
            var (vm, _, _, _, _) = BuildVm();
            vm.SortLeftCommand.Execute(null);
            Assert.Contains("▲", vm.SortLeftHeader);
        }

        [Fact]
        public void SortLeft_twice_toggles_direction_to_descending()
        {
            var (vm, _, _, _, _) = BuildVm();
            vm.SortLeftCommand.Execute(null); // ascending
            vm.SortLeftCommand.Execute(null); // descending
            Assert.Contains("▼", vm.SortLeftHeader);
        }

        // ── BC path status ────────────────────────────────────────────────────

        [Fact]
        public void BcStatusText_is_not_found_when_path_set_to_nonexistent_file()
        {
            var (vm, _, _, _, _) = BuildVm();
            vm.BcPath = @"C:\DoesNotExist\BCompare.exe";
            // UpdateBcStatus is called automatically by the BcPath setter
            Assert.Contains("Not found", vm.BcStatusText);
        }

        [Fact]
        public void ClearBcPath_resets_BcPath_to_empty()
        {
            var (vm, _, _, _, _) = BuildVm();
            vm.BcPath = @"C:\SomePath\BCompare.exe";

            vm.ClearBcPathCommand.Execute(null);

            Assert.Equal("", vm.BcPath);
        }

        // ── AppendLog ─────────────────────────────────────────────────────────

        [Fact]
        public void AppendLog_adds_message_to_LogText()
        {
            var (vm, _, _, _, _) = BuildVm();
            vm.AppendLog("hello world");
            Assert.Contains("hello world", vm.LogText);
        }

        [Fact]
        public void AppendLog_trims_log_when_exceeding_MaxLogLines()
        {
            var (vm, _, _, _, _) = BuildVm();
            for (int i = 0; i < 1100; i++)
                vm.AppendLog($"line {i}");

            int lineCount = vm.LogText.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
            Assert.True(lineCount <= 1010, $"Expected ≤1010 lines but got {lineCount}");
        }

        // ── #8 ClearFilterCommand ─────────────────────────────────────────────

        [Fact]
        public void ClearFilterCommand_sets_FilterText_to_empty()
        {
            var (vm, _, _, _, _) = BuildVm();
            vm.FilterText = "search term";

            vm.ClearFilterCommand.Execute(null);

            Assert.Equal("", vm.FilterText);
        }

        [Fact]
        public void FilterHasText_is_Collapsed_when_FilterText_is_empty()
        {
            var (vm, _, _, _, _) = BuildVm();
            vm.FilterText = "";
            Assert.Equal("Collapsed", vm.FilterHasText);
        }

        [Fact]
        public void FilterHasText_is_Visible_when_FilterText_has_content()
        {
            var (vm, _, _, _, _) = BuildVm();
            vm.FilterText = "some text";
            Assert.Equal("Visible", vm.FilterHasText);
        }

        [Fact]
        public void FilterHasText_returns_to_Collapsed_after_ClearFilterCommand()
        {
            var (vm, _, _, _, _) = BuildVm();
            vm.FilterText = "search";
            Assert.Equal("Visible", vm.FilterHasText);

            vm.ClearFilterCommand.Execute(null);

            Assert.Equal("Collapsed", vm.FilterHasText);
        }

        // ── #9 ResultCountText ────────────────────────────────────────────────

        [Fact]
        public void ResultCountText_is_empty_on_fresh_ViewModel()
        {
            var (vm, _, _, _, _) = BuildVm();
            Assert.Equal("", vm.ResultCountText);
        }

        [Fact]
        public void Setting_ResultCountText_raises_PropertyChanged()
        {
            var (vm, _, _, _, _) = BuildVm();
            bool raised = false;
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.ResultCountText)) raised = true;
            };

            vm.ResultCountText = "1,234 files";

            Assert.True(raised);
        }

        // ── #3 StringBuilder log ──────────────────────────────────────────────

        [Fact]
        public void AppendLog_does_not_lose_earlier_lines_below_cap()
        {
            var (vm, _, _, _, _) = BuildVm();
            vm.AppendLog("first line");
            vm.AppendLog("second line");
            vm.AppendLog("third line");

            Assert.Contains("first line",  vm.LogText);
            Assert.Contains("second line", vm.LogText);
            Assert.Contains("third line",  vm.LogText);
        }

        [Fact]
        public void AppendLog_multiple_calls_accumulate_all_lines()
        {
            var (vm, _, _, _, _) = BuildVm();
            for (int i = 1; i <= 10; i++)
                vm.AppendLog($"msg {i}");

            for (int i = 1; i <= 10; i++)
                Assert.Contains($"msg {i}", vm.LogText);
        }
    }
}
