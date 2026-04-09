using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using FolderComparerUI.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FolderComparerUI.Services
{
    /// <summary>Handles all Excel .xlsx export logic — no UI references.</summary>
    public class ExcelExportService
    {
        public string Export(
            string filePath,
            string leftRoot, string rightRoot,
            string tagDescription,
            IEnumerable<ResultRow> rows,
            int totalUnique, int sameC, int leftC, int rightC, int diffC,
            int copied, int deleted,
            string userSheetName = "")
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("Invalid file path", nameof(filePath));

            Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? Environment.CurrentDirectory);

            var rowList = rows.ToList();
            int maxCat  = Math.Max("Category".Length,      rowList.Select(r => r.Category.Length).DefaultIfEmpty(0).Max());
            int maxRel  = Math.Max("Relative Path".Length, rowList.Select(r => r.Relative.Length).DefaultIfEmpty(0).Max());
            double wA   = Math.Min(120, Math.Max(8,  maxCat + 3));
            double wB   = Math.Min(250, Math.Max(20, maxRel + 3));

            SpreadsheetDocument? doc = null;
            try
            {
                doc = File.Exists(filePath)
                    ? SpreadsheetDocument.Open(filePath, true)
                    : CreateNewWorkbook(filePath);

                var wb = doc.WorkbookPart ?? throw new InvalidOperationException("WorkbookPart is null.");
                wb.Workbook ??= new Workbook();
                var ws = wb.AddNewPart<WorksheetPart>();
                var sd = new SheetData();
                ws.Worksheet = new Worksheet();

                var cols = new Columns();
                cols.Append(new Column { Min = 1, Max = 1, Width = wA, CustomWidth = true });
                cols.Append(new Column { Min = 2, Max = 2, Width = wB, CustomWidth = true });
                ws.Worksheet.Append(cols);
                ws.Worksheet.Append(sd);

                string sheetName = BuildSheetName(userSheetName);

                var sheets = wb.Workbook.GetFirstChild<Sheets>()
                             ?? wb.Workbook.AppendChild(new Sheets());

                var existing = sheets.Elements<Sheet>()
                    .Select(s => s.Name?.Value ?? "")
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                if (existing.Contains(sheetName))
                {
                    string b = sheetName.Length > 28 ? sheetName[..28] : sheetName;
                    int sfx  = 2;
                    while (existing.Contains($"{b}_{sfx}")) sfx++;
                    sheetName = $"{b}_{sfx}";
                }

                uint sheetId = sheets.Elements<Sheet>().Any()
                    ? sheets.Elements<Sheet>().Max(s => s.SheetId!.Value) + 1 : 1;
                sheets.Append(new Sheet { Id = wb.GetIdOfPart(ws), SheetId = sheetId, Name = sheetName });

                uint ri = 1;
                void HR(string lbl, string val)
                {
                    var r = new Row { RowIndex = ri++ };
                    r.Append(TC("A", lbl)); r.Append(TC("B", val));
                    sd.Append(r);
                }

                HR("Export Timestamp:", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                HR("Left folder:",      leftRoot);
                HR("Right folder:",     rightRoot);
                HR("Selection:",        tagDescription);
                HR("Total unique:",     totalUnique.ToString());
                HR("Same:",             sameC.ToString());
                HR("Left-only:",        leftC.ToString());
                HR("Right-only:",       rightC.ToString());
                HR("Different:",        diffC.ToString());
                HR("Last copied:",      copied.ToString());
                HR("Last deleted:",     deleted.ToString());
                sd.Append(new Row { RowIndex = ri++ });

                var hdr = new Row { RowIndex = ri++ };
                hdr.Append(TC("A", "Category")); hdr.Append(TC("B", "Relative Path"));
                sd.Append(hdr);

                foreach (var item in rowList)
                {
                    var r = new Row { RowIndex = ri++ };
                    r.Append(TC("A", item.Category)); r.Append(TC("B", item.Relative));
                    sd.Append(r);
                }

                ws.Worksheet.Save();
                wb.Workbook.Save();
                return sheetName;
            }
            finally { doc?.Dispose(); }
        }

        public bool IsFileLocked(string filePath)
        {
            try { using var s = File.Open(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None); return false; }
            catch (IOException) { return true; }
        }

        private static string BuildSheetName(string userSheetName)
        {
            if (!string.IsNullOrWhiteSpace(userSheetName))
            {
                var bad   = new[] { '\\', '/', '?', '*', '[', ']', ':' };
                string sn = new string(userSheetName.Where(c => !bad.Contains(c)).ToArray()).Trim();
                if (sn.Length > 31) sn = sn[..31];
                if (!string.IsNullOrWhiteSpace(sn)) return sn;
            }
            return "Results_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
        }

        private static SpreadsheetDocument CreateNewWorkbook(string filePath)
        {
            var d = SpreadsheetDocument.Create(filePath, SpreadsheetDocumentType.Workbook);
            var w = d.AddWorkbookPart();
            w.Workbook = new Workbook();
            w.Workbook.AppendChild(new Sheets());
            w.Workbook.Save();
            return d;
        }

        private static Cell TC(string col, string text) => new Cell
        {
            DataType     = CellValues.InlineString,
            InlineString = new InlineString(new Text(text ?? string.Empty))
        };
    }
}
