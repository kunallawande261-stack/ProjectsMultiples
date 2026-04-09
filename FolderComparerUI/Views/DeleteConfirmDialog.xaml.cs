using System.Windows;

namespace FolderComparerUI.Views
{
    public enum DeleteConfirmResult { Delete, DryRun, Cancel }

    public partial class DeleteConfirmDialog : Window
    {
        public DeleteConfirmResult Result    { get; private set; } = DeleteConfirmResult.Cancel;
        public bool                UseRecycleBin => chkRecycle.IsChecked == true;

        public DeleteConfirmDialog(string infoText)
        {
            InitializeComponent();
            txtInfo.Text = infoText;
        }

        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            Result = DeleteConfirmResult.Delete;
            DialogResult = true;
        }

        private void BtnDryRun_Click(object sender, RoutedEventArgs e)
        {
            Result = DeleteConfirmResult.DryRun;
            DialogResult = true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Result = DeleteConfirmResult.Cancel;
            DialogResult = false;
        }
    }
}
