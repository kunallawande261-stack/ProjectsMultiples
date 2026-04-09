using FolderComparerUI.Models;

namespace FolderComparerUI.Services
{
    public interface ISettingsService
    {
        AppSettings Load();
        void Save(AppSettings settings);
    }
}
