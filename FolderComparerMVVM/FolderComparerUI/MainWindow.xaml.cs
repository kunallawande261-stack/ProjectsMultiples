using FolderComparerUI.Models;
using FolderComparerUI.ViewModels;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FolderComparerUI
{
    /// <summary>
    /// Code-behind is intentionally thin.
    /// Only handles things that genuinely cannot be done in XAML/bindings:
    ///   1. Window lifecycle (Loaded, Closing) → delegates to ViewModel
    ///   2. Infinite-scroll detection via ScrollViewer event
    ///   3. Double-click column detection (requires visual-tree walk)
    ///   4. ContextMenu Command wiring (WPF Style Setter limitation)
    ///   5. Drag-drop (raw WPF events — delegated to ViewModel properties)
    ///   6. Column width calculation (requires ActualWidth at runtime)
    /// </summary>
    public partial class MainWindow : MahApps.Metro.Controls.MetroWindow
    {
        private MainViewModel VM => (MainViewModel)DataContext;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel();
        }

        // ── Window lifecycle ──────────────────────────────────────────────────
        private void MetroWindow_Loaded(object sender, RoutedEventArgs e)
        {
            VM.LoadSettings();
            lvResults.AddHandler(ScrollViewer.ScrollChangedEvent,
                new ScrollChangedEventHandler(ResultsScrollChanged));
            lvResults.MouseDoubleClick += LvResults_MouseDoubleClick;
            UpdateColumnWidths();
        }

        private void MetroWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
            => VM.SaveSettings();

        private void MetroWindow_StateChanged(object sender, EventArgs e)
        {
            Topmost = VM.Topmost && WindowState != WindowState.Maximized;
        }

        private void MetroWindow_SizeChanged(object sender, SizeChangedEventArgs e)
            => UpdateColumnWidths();

        // ── Infinite scroll ───────────────────────────────────────────────────
        private void ResultsScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - 5)
                VM.OnResultsScrolledToEnd();
        }

        // ── Double-click: detect left vs right column ─────────────────────────
        private void LvResults_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (VM.SelectedRow is not ResultRow row) return;

            string path = "";
            var hit = e.OriginalSource as FrameworkElement;
            while (hit != null)
            {
                if (hit is TextBlock tb && hit.Parent is Grid)
                {
                    int col = Grid.GetColumn(tb);
                    path = col == 0 ? row.LeftFull
                         : col == 2 ? row.RightFull
                         : "";
                    break;
                }
                hit = hit.Parent as FrameworkElement;
            }
            if (string.IsNullOrEmpty(path))
                path = !string.IsNullOrEmpty(row.LeftFull) ? row.LeftFull : row.RightFull;

            if (!string.IsNullOrEmpty(path))
                MainViewModel.RevealInExplorer(path);
        }

        // ── ContextMenu: wire Commands at runtime (cannot use Command= in Style Setter) ──
        private void LvResults_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            var container = lvResults.ItemContainerGenerator
                .ContainerFromItem(lvResults.SelectedItem) as ListViewItem;
            var menu = container?.ContextMenu;
            if (menu == null || VM.WiredMenus.Contains(menu)) return;

            foreach (var obj in menu.Items)
            {
                if (obj is not MenuItem mi) continue;
                mi.Command = mi.Name switch
                {
                    "ctxOpenLeftExplorer"  => VM.OpenLeftExplorerCommand,
                    "ctxOpenRightExplorer" => VM.OpenRightExplorerCommand,
                    "ctxBcLeft"            => VM.OpenLeftBcCommand,
                    "ctxBcRight"           => VM.OpenRightBcCommand,
                    "ctxBcCompare"         => VM.CompareBcCommand,
                    _                      => mi.Command
                };
            }
            VM.WiredMenus.Add(menu);
        }

        // ── Column width calculation ──────────────────────────────────────────
        private void UpdateColumnWidths()
        {
            const double catW = 96, extra = 32, minSide = 120;
            if (resultsGrid == null) return;
            double total = resultsGrid.ActualWidth;
            if (double.IsNaN(total) || total <= catW + extra) return;
            double side = Math.Max(minSide, Math.Floor((total - catW - extra) / 2.0));
            if (btnSortLeft  != null) btnSortLeft.Width  = side;
            if (btnSortRight != null) btnSortRight.Width = side;
        }

        // ── Drag-drop ─────────────────────────────────────────────────────────
        private void TextBox_PreviewDragOver(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
                { e.Effects = DragDropEffects.None; e.Handled = true; return; }
            var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (paths == null || paths.Length == 0)
                { e.Effects = DragDropEffects.None; e.Handled = true; return; }

            string first = paths[0];
            bool isDir   = System.IO.Directory.Exists(first);
            bool isFile  = System.IO.File.Exists(first);

            if (sender is TextBox tb)
            {
                if (tb == txtLeft || tb == txtRight)
                    e.Effects = isDir ? DragDropEffects.Copy : DragDropEffects.None;
                else if (tb == txtExcelPath)
                    e.Effects = isDir || (isFile && IsExt(first, ".xlsx"))
                        ? DragDropEffects.Copy : DragDropEffects.None;
                else if (tb == txtLogPath)
                    e.Effects = isDir || (isFile && IsExt(first, ".txt"))
                        ? DragDropEffects.Copy : DragDropEffects.None;
                else
                    e.Effects = isDir || isFile ? DragDropEffects.Copy : DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void TextBox_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (paths == null || paths.Length == 0) return;
            string first = paths[0];
            if (sender is not TextBox tb) return;

            string ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            if (tb == txtLeft)
            {
                if (!System.IO.Directory.Exists(first))
                { MessageBox.Show("Please drop a folder.", "Invalid Drop", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
                VM.LeftPath = first;
            }
            else if (tb == txtRight)
            {
                if (!System.IO.Directory.Exists(first))
                { MessageBox.Show("Please drop a folder.", "Invalid Drop", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
                VM.RightPath = first;
            }
            else if (tb == txtExcelPath)
                VM.ExcelPath = System.IO.Directory.Exists(first)
                    ? System.IO.Path.Combine(first, $"CompareResults_{ts}.xlsx") : first;
            else if (tb == txtLogPath)
                VM.LogPath = System.IO.Directory.Exists(first)
                    ? System.IO.Path.Combine(first, $"CompareLog_{ts}.txt") : first;
            else if (tb == txtBcPath)
                VM.BcPath = first;
            else if (tb == txtExportPath)
            {
                if (System.IO.Directory.Exists(first)) VM.ExportPath = first;
            }
        }

        // ── Stub event handler (SelectedItem handled via binding) ─────────────
        private void lvResults_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

        private static bool IsExt(string path, string ext) =>
            string.Equals(System.IO.Path.GetExtension(path), ext,
                StringComparison.OrdinalIgnoreCase);
    }
}
