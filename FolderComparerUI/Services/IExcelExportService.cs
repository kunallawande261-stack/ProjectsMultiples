using FolderComparerUI.Models;
using System.Collections.Generic;

namespace FolderComparerUI.Services
{
    /// <summary>
    /// Abstracts Excel export so the ViewModel can be tested without
    /// the OpenXML dependency.
    /// </summary>
    public interface IExcelExportService
    {
        /// <summary>
        /// Writes (or appends) results to an .xlsx file.
        /// Returns the sheet name that was written.
        /// </summary>
        string Export(
            string filePath,
            string leftRoot,
            string rightRoot,
            string tagDescription,
            IEnumerable<ResultRow> rows,
            int totalUnique, int sameC, int leftC, int rightC, int diffC,
            int copied, int deleted,
            string userSheetName = "");

        /// <summary>Returns true if the file is open in another process.</summary>
        bool IsFileLocked(string filePath);
    }
}
