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
            var hit = e.OriginalSource as DependencyObject;

            // Walk up the visual tree — TextBlock is now inside Border inside Grid
            // so we need to find the Grid ancestor and read the column from there.
            while (hit != null)
            {
                if (hit is TextBlock tb)
                {
                    // Walk up past Border(s) to find the Grid
                    var parent = System.Windows.Media.VisualTreeHelper.GetParent(tb);
                    while (parent != null && parent is not Grid)
                        parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);

                    if (parent is Grid)
                    {
                        // Column index is on the Border or TextBlock — check both
                        int col = Grid.GetColumn(tb);
                        if (col == 0 || col == 2)
                        {
                            path = col == 0 ? row.LeftFull : row.RightFull;
                            break;
                        }
                        // Try the parent Border
                        var border = System.Windows.Media.VisualTreeHelper.GetParent(tb);
                        if (border is Border b)
                        {
                            int bcol = Grid.GetColumn(b);
                            path = bcol == 0 ? row.LeftFull
                                 : bcol == 2 ? row.RightFull
                                 : "";
                            break;
                        }
                    }
                }
                hit = System.Windows.Media.VisualTreeHelper.GetParent(hit);
            }

            if (string.IsNullOrEmpty(path))
                path = !string.IsNullOrEmpty(row.LeftFull) ? row.LeftFull : row.RightFull;

            if (!string.IsNullOrEmpty(path))
                MainViewModel.RevealInExplorer(path);
        }

        // ── ContextMenu: wire Commands + control visibility per category ──────
        private void LvResults_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            var container = lvResults.ItemContainerGenerator
                .ContainerFromItem(lvResults.SelectedItem) as ListViewItem;
            var menu = container?.ContextMenu;
            if (menu == null) return;

            // Wire commands every time (not once) so the correct SelectedRow
            // is captured — the row changes on each right-click.
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
                    "ctxCopyToRight"       => VM.QuickCopyToRightCommand,
                    "ctxCopyToLeft"        => VM.QuickCopyToLeftCommand,
                    "ctxDeleteLeft"        => VM.QuickDeleteLeftCommand,
                    "ctxDeleteRight"       => VM.QuickDeleteRightCommand,
                    "ctxDeleteBoth"        => VM.QuickDeleteBothCommand,
                    _                      => mi.Command
                };
            }

            // Show/hide action items based on the selected row's category.
            // Only show actions that make sense — avoid confusing users with
            // greyed-out options that can never succeed.
            string cat = (VM.SelectedRow?.Category) ?? "";
            bool hasLeft  = !string.IsNullOrEmpty(VM.SelectedRow?.LeftFull);
            bool hasRight = !string.IsNullOrEmpty(VM.SelectedRow?.RightFull);

            // Copy to Right: only when left file exists (Left-Orphan or Different)
            SetMenuItemVisibility(menu, "ctxCopyToRight",  hasLeft && !hasRight || cat == "Different");
            // Copy to Left: only when right file exists (Right-Orphan or Different)
            SetMenuItemVisibility(menu, "ctxCopyToLeft",   hasRight && !hasLeft || cat == "Different");
            // Delete Left: only when left file exists
            SetMenuItemVisibility(menu, "ctxDeleteLeft",   hasLeft);
            // Delete Right: only when right file exists
            SetMenuItemVisibility(menu, "ctxDeleteRight",  hasRight);
            // Delete Both: only when both sides exist
            SetMenuItemVisibility(menu, "ctxDeleteBoth",   hasLeft && hasRight);

            // Show the action separator only when at least one action is visible
            bool anyAction = hasLeft || hasRight;
            SetSeparatorVisibility(menu, "ctxActionSep",  anyAction);
            SetSeparatorVisibility(menu, "ctxDeleteSep",  hasLeft || hasRight);
        }

        private static void SetMenuItemVisibility(ContextMenu menu, string name, bool visible)
        {
            foreach (var obj in menu.Items)
                if (obj is MenuItem mi && mi.Name == name)
                    mi.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        private static void SetSeparatorVisibility(ContextMenu menu, string name, bool visible)
        {
            foreach (var obj in menu.Items)
                if (obj is Separator sep && sep.Name == name)
                    sep.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        // ── Column width calculation ──────────────────────────────────────────
        // The header buttons live inside a Grid with * / 110 / * columns so
        // they size themselves responsively. Nothing to compute manually here —
        // the method is retained so SizeChanged still triggers a layout pass.
        private void UpdateColumnWidths()
        {
            // Intentionally empty — responsive layout handled by Grid column definitions.
            // Kept to preserve the SizeChanged → UpdateColumnWidths call chain.
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
