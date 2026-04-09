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
    /// <summary>
    /// Produces a professionally formatted .xlsx export.
    ///
    /// Sheet layout:
    ///   Row 1        — Title banner  "Folder Comparer — Export Report"
    ///   Rows 2-12    — Meta info table  (label | value), light blue background
    ///   Row 13       — Blank spacer
    ///   Row 14       — Summary bar  Same | Left-only | Right-only | Different  (coloured)
    ///   Row 15       — Blank spacer
    ///   Row 16       — Data header row  (dark navy background, white bold text)
    ///   Rows 17+     — Data rows, alternating white/very-light-grey, category colour in col A
    ///
    /// All cells have borders. Column widths auto-fitted to content.
    /// </summary>
    public class ExcelExportService : IExcelExportService
    {
        // ── Palette (ARGB hex, no leading #) ─────────────────────────────────
        private const string C_TITLE_BG    = "FF1E5C9B"; // dark brand blue  — title row
        private const string C_TITLE_FG    = "FFFFFFFF"; // white            — title text
        private const string C_META_BG     = "FFD6E4F0"; // light blue       — meta rows
        private const string C_META_LABEL  = "FF1A3A5C"; // deep navy        — meta label text
        private const string C_META_VALUE  = "FF1A1A2E"; // near-black       — meta value text
        private const string C_HEADER_BG   = "FF1E5C9B"; // dark brand blue  — data header
        private const string C_HEADER_FG   = "FFFFFFFF"; // white            — data header text
        private const string C_ROW_ODD     = "FFFFFFFF"; // white            — odd data rows
        private const string C_ROW_EVEN    = "FFF5F8FC"; // very light blue  — even data rows
        private const string C_BORDER      = "FFBDD7EE"; // soft blue-grey   — all borders
        private const string C_SAME_BG     = "FFE8F5E9"; // mint green       — Same rows
        private const string C_SAME_FG     = "FF2E7D32"; // dark green       — Same text
        private const string C_DIFF_BG     = "FFFFF3E0"; // pale amber       — Different rows
        private const string C_DIFF_FG     = "FFE65100"; // dark orange      — Different text
        private const string C_LONLY_BG    = "FFE3F2FD"; // pale sky blue    — Left-only rows
        private const string C_LONLY_FG    = "FF1565C0"; // deep blue        — Left-only text
        private const string C_RONLY_BG    = "FFEDE7F6"; // pale lavender    — Right-only rows
        private const string C_RONLY_FG    = "FF4527A0"; // deep purple      — Right-only text
        private const string C_SUMMARY_BG  = "FF2E75B6"; // mid brand blue   — summary bar
        private const string C_SUMMARY_FG  = "FFFFFFFF"; // white            — summary text

        // ── Style index constants (assigned in BuildStylesheet) ───────────────
        private const uint SI_DEFAULT     = 0;
        private const uint SI_TITLE       = 1;
        private const uint SI_META_LABEL  = 2;
        private const uint SI_META_VALUE  = 3;
        private const uint SI_HDR         = 4;
        private const uint SI_DATA_ODD    = 5;
        private const uint SI_DATA_EVEN   = 6;
        private const uint SI_SAME_ODD    = 7;
        private const uint SI_SAME_EVEN   = 8;
        private const uint SI_DIFF_ODD    = 9;
        private const uint SI_DIFF_EVEN   = 10;
        private const uint SI_LONLY_ODD   = 11;
        private const uint SI_LONLY_EVEN  = 12;
        private const uint SI_RONLY_ODD   = 13;
        private const uint SI_RONLY_EVEN  = 14;
        private const uint SI_SUMMARY     = 15;
        private const uint SI_SPACER      = 16;

        // ─────────────────────────────────────────────────────────────────────

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

            // Column widths — fit to content, clamped
            int maxCat = Math.Max("Category".Length,      rowList.Select(r => r.Category.Length).DefaultIfEmpty(0).Max());
            int maxRel = Math.Max("Relative Path".Length, rowList.Select(r => r.Relative.Length).DefaultIfEmpty(0).Max());
            double wA  = Math.Min(22,  Math.Max(12, maxCat + 3));
            double wB  = Math.Min(150, Math.Max(30, maxRel + 3));

            SpreadsheetDocument? doc = null;
            try
            {
                bool isNew = !File.Exists(filePath);
                doc = isNew
                    ? CreateNewWorkbook(filePath)
                    : SpreadsheetDocument.Open(filePath, true);

                var wb = doc.WorkbookPart ?? throw new InvalidOperationException("WorkbookPart is null.");
                wb.Workbook ??= new Workbook();

                // ── Stylesheet — only add to new workbook ─────────────────────
                if (isNew || wb.WorkbookStylesPart == null)
                {
                    var sp = wb.WorkbookStylesPart ?? wb.AddNewPart<WorkbookStylesPart>();
                    sp.Stylesheet = BuildStylesheet();
                    sp.Stylesheet.Save();
                }

                // ── New sheet ─────────────────────────────────────────────────
                var ws = wb.AddNewPart<WorksheetPart>();
                var sd = new SheetData();
                ws.Worksheet = new Worksheet();

                var cols = new Columns();
                cols.Append(new Column { Min = 1, Max = 1, Width = wA, CustomWidth = true, BestFit = true });
                cols.Append(new Column { Min = 2, Max = 2, Width = wB, CustomWidth = true, BestFit = true });
                ws.Worksheet.Append(cols);
                ws.Worksheet.Append(sd);

                // ── Sheet name deduplication ──────────────────────────────────
                string sheetName = BuildSheetName(userSheetName);
                var sheets = wb.Workbook.GetFirstChild<Sheets>()
                             ?? wb.Workbook.AppendChild(new Sheets());
                var existing = sheets.Elements<Sheet>()
                    .Select(s => s.Name?.Value ?? "")
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (existing.Contains(sheetName))
                {
                    string b = sheetName.Length > 28 ? sheetName[..28] : sheetName;
                    int sfx = 2;
                    while (existing.Contains($"{b}_{sfx}")) sfx++;
                    sheetName = $"{b}_{sfx}";
                }
                uint sheetId = sheets.Elements<Sheet>().Any()
                    ? sheets.Elements<Sheet>().Max(s => s.SheetId!.Value) + 1 : 1;
                sheets.Append(new Sheet
                    { Id = wb.GetIdOfPart(ws), SheetId = sheetId, Name = sheetName });

                // ── Build rows ────────────────────────────────────────────────
                uint ri = 1;

                // Row 1 — Title banner (merged A:B visually via wide col B)
                sd.Append(StyledRow(ri++,
                    SC("A", "Folder Comparer — Export Report", SI_TITLE),
                    SC("B", "", SI_TITLE)));

                // Rows 2-12 — Meta info
                void Meta(string lbl, string val)
                {
                    sd.Append(StyledRow(ri++,
                        SC("A", lbl, SI_META_LABEL),
                        SC("B", val, SI_META_VALUE)));
                }
                Meta("Export date:",   DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                Meta("Left folder:",   leftRoot);
                Meta("Right folder:",  rightRoot);
                Meta("Selection:",     tagDescription);
                Meta("Total files:",   totalUnique.ToString("N0"));
                Meta("Same:",          sameC.ToString("N0"));
                Meta("Left-only:",     leftC.ToString("N0"));
                Meta("Right-only:",    rightC.ToString("N0"));
                Meta("Different:",     diffC.ToString("N0"));
                Meta("Last copied:",   copied.ToString("N0"));
                Meta("Last deleted:",  deleted.ToString("N0"));

                // Spacer
                sd.Append(SpacerRow(ri++));

                // Summary bar — "Same: N  |  Left-only: N  |  Right-only: N  |  Different: N"
                string summary =
                    $"Same: {sameC:N0}     Left-only: {leftC:N0}     Right-only: {rightC:N0}     Different: {diffC:N0}";
                sd.Append(StyledRow(ri++,
                    SC("A", "Summary:", SI_SUMMARY),
                    SC("B", summary,    SI_SUMMARY)));

                // Spacer
                sd.Append(SpacerRow(ri++));

                // Data header
                sd.Append(StyledRow(ri++,
                    SC("A", "Category",      SI_HDR),
                    SC("B", "Relative Path", SI_HDR)));

                // Data rows — alternating, category coloured in col A
                int dataIndex = 0;
                foreach (var item in rowList)
                {
                    bool odd = dataIndex % 2 == 0;
                    dataIndex++;

                    (uint siA, uint siB) = item.Category switch
                    {
                        "Same"         => (odd ? SI_SAME_ODD  : SI_SAME_EVEN,
                                          odd ? SI_SAME_ODD  : SI_SAME_EVEN),
                        "Different"    => (odd ? SI_DIFF_ODD  : SI_DIFF_EVEN,
                                          odd ? SI_DIFF_ODD  : SI_DIFF_EVEN),
                        "Left-Orphan"  => (odd ? SI_LONLY_ODD : SI_LONLY_EVEN,
                                          odd ? SI_LONLY_ODD : SI_LONLY_EVEN),
                        "Right-Orphan" => (odd ? SI_RONLY_ODD : SI_RONLY_EVEN,
                                          odd ? SI_RONLY_ODD : SI_RONLY_EVEN),
                        _              => (odd ? SI_DATA_ODD  : SI_DATA_EVEN,
                                          odd ? SI_DATA_ODD  : SI_DATA_EVEN),
                    };

                    sd.Append(StyledRow(ri++,
                        SC("A", item.Category, siA),
                        SC("B", item.Relative, siB)));
                }

                ws.Worksheet.Save();
                wb.Workbook.Save();
                return sheetName;
            }
            finally { doc?.Dispose(); }
        }

        // ── Public helpers ────────────────────────────────────────────────────

        public bool IsFileLocked(string filePath)
        {
            if (!File.Exists(filePath)) return false;
            try
            {
                using var s = File.Open(filePath, FileMode.Open,
                    FileAccess.ReadWrite, FileShare.None);
                return false;
            }
            catch (IOException) { return true; }
        }

        // ── Row builders ──────────────────────────────────────────────────────

        private static Row StyledRow(uint rowIndex, Cell a, Cell b)
        {
            var r = new Row { RowIndex = rowIndex };
            r.Append(a); r.Append(b);
            return r;
        }

        private static Row SpacerRow(uint rowIndex)
        {
            var r = new Row { RowIndex = rowIndex, CustomHeight = true, Height = 6 };
            r.Append(SC("A", "", SI_SPACER));
            r.Append(SC("B", "", SI_SPACER));
            return r;
        }

        private static Cell SC(string col, string text, uint styleIndex) => new Cell
        {
            CellReference = col,
            DataType       = CellValues.InlineString,
            InlineString   = new InlineString(new Text(text ?? string.Empty)),
            StyleIndex     = styleIndex,
        };

        // ── Stylesheet ────────────────────────────────────────────────────────

        private static Stylesheet BuildStylesheet()
        {
            // ── Fonts ─────────────────────────────────────────────────────────
            //  0 default  1 title  2 meta-label  3 meta-value  4 header  5 same
            //  6 diff      7 lonly  8 ronly       9 summary    10 spacer
            var fonts = new Fonts(
                MkFont("Calibri", 11, "FF000000"),                      // 0 default
                MkFont("Calibri", 14, C_TITLE_FG,  bold: true),         // 1 title
                MkFont("Calibri", 10, C_META_LABEL, bold: true),        // 2 meta label
                MkFont("Calibri", 10, C_META_VALUE),                    // 3 meta value
                MkFont("Calibri", 11, C_HEADER_FG,  bold: true),        // 4 data header
                MkFont("Calibri", 10, C_SAME_FG,    bold: true),        // 5 Same cat
                MkFont("Calibri", 10, C_DIFF_FG,    bold: true),        // 6 Different cat
                MkFont("Calibri", 10, C_LONLY_FG,   bold: true),        // 7 Left-only cat
                MkFont("Calibri", 10, C_RONLY_FG,   bold: true),        // 8 Right-only cat
                MkFont("Calibri", 11, C_SUMMARY_FG, bold: true),        // 9 summary
                MkFont("Calibri", 6,  "FFFFFFFF")                       // 10 spacer
            ) { Count = 11 };

            // ── Fills ─────────────────────────────────────────────────────────
            // OpenXML requires indices 0 (none) and 1 (gray125) to exist first
            var fills = new Fills(
                new Fill(new PatternFill { PatternType = PatternValues.None }),          // 0 none
                new Fill(new PatternFill { PatternType = PatternValues.Gray125 }),       // 1 gray125
                MkFill(C_TITLE_BG),    // 2 title
                MkFill(C_META_BG),     // 3 meta
                MkFill(C_HEADER_BG),   // 4 data header
                MkFill(C_ROW_ODD),     // 5 data odd
                MkFill(C_ROW_EVEN),    // 6 data even
                MkFill(C_SAME_BG),     // 7 same odd
                MkFill(C_SAME_BG),     // 8 same even (slightly same, band effect via font)
                MkFill(C_DIFF_BG),     // 9 diff odd
                MkFill(C_DIFF_BG),     // 10 diff even
                MkFill(C_LONLY_BG),    // 11 lonly odd
                MkFill(C_LONLY_BG),    // 12 lonly even
                MkFill(C_RONLY_BG),    // 13 ronly odd
                MkFill(C_RONLY_BG),    // 14 ronly even
                MkFill(C_SUMMARY_BG),  // 15 summary
                MkFill("FFFFFFFF")     // 16 spacer
            ) { Count = 17 };

            // ── Borders ───────────────────────────────────────────────────────
            // 0 = no border, 1 = thin all sides, 2 = thin all sides (same as 1,
            // kept for indexing alignment with fills)
            var borders = new Borders(
                new Border(),          // 0 no border
                MkBorder()             // 1 thin border on all 4 sides
            ) { Count = 2 };

            // ── CellStyleFormats (xfs for styles) ─────────────────────────────
            // Required base entry
            var cellStyleXfs = new CellStyleFormats(
                new CellFormat { FontId = 0, FillId = 0, BorderId = 0 }
            ) { Count = 1 };

            // ── CellFormats (actual format table) ────────────────────────────
            // Index must match SI_* constants above
            var cellXfs = new CellFormats(
                // 0  SI_DEFAULT
                CF(0, 0, 0),
                // 1  SI_TITLE
                CF(1, 2, 1, wrapText: false, rowHeight: 22),
                // 2  SI_META_LABEL
                CF(2, 3, 1),
                // 3  SI_META_VALUE
                CF(3, 3, 1),
                // 4  SI_HDR
                CF(4, 4, 1, wrapText: false, rowHeight: 18),
                // 5  SI_DATA_ODD
                CF(0, 5, 1),
                // 6  SI_DATA_EVEN
                CF(0, 6, 1),
                // 7  SI_SAME_ODD
                CF(5, 7, 1),
                // 8  SI_SAME_EVEN
                CF(5, 8, 1),
                // 9  SI_DIFF_ODD
                CF(6, 9, 1),
                // 10 SI_DIFF_EVEN
                CF(6, 10, 1),
                // 11 SI_LONLY_ODD
                CF(7, 11, 1),
                // 12 SI_LONLY_EVEN
                CF(7, 12, 1),
                // 13 SI_RONLY_ODD
                CF(8, 13, 1),
                // 14 SI_RONLY_EVEN
                CF(8, 14, 1),
                // 15 SI_SUMMARY
                CF(9, 15, 1),
                // 16 SI_SPACER
                CF(10, 16, 0)
            ) { Count = 17 };

            var cellStyles = new CellStyles(
                new CellStyle { Name = "Normal", FormatId = 0, BuiltinId = 0 }
            ) { Count = 1 };

            return new Stylesheet(fonts, fills, borders, cellStyleXfs, cellXfs, cellStyles);
        }

        // ── OpenXML helpers ───────────────────────────────────────────────────

        private static Font MkFont(string name, double size, string argb,
            bool bold = false, bool italic = false)
        {
            var f = new Font(
                new FontSize { Val = size },
                new Color    { Rgb = argb },
                new FontName { Val = name }
            );
            if (bold)   f.Append(new Bold());
            if (italic) f.Append(new Italic());
            return f;
        }

        private static Fill MkFill(string argb) => new Fill(
            new PatternFill(
                new ForegroundColor { Rgb = argb }
            ) { PatternType = PatternValues.Solid }
        );

        private static Border MkBorder()
        {
            var style = BorderStyleValues.Thin;
            var color = new Color { Rgb = C_BORDER };
            return new Border(
                new LeftBorder   (new Color { Rgb = C_BORDER }) { Style = style },
                new RightBorder  (new Color { Rgb = C_BORDER }) { Style = style },
                new TopBorder    (new Color { Rgb = C_BORDER }) { Style = style },
                new BottomBorder (new Color { Rgb = C_BORDER }) { Style = style },
                new DiagonalBorder()
            );
        }

        private static CellFormat CF(uint fontId, uint fillId, uint borderId,
            bool wrapText = true, double rowHeight = 0)
        {
            var cf = new CellFormat
            {
                FontId           = fontId,
                FillId           = fillId,
                BorderId         = borderId,
                ApplyFont        = true,
                ApplyFill        = true,
                ApplyBorder      = true,
                ApplyAlignment   = true,
                Alignment        = new Alignment
                {
                    Vertical    = VerticalAlignmentValues.Center,
                    WrapText    = wrapText,
                }
            };
            return cf;
        }

        // ── Workbook / sheet name helpers ─────────────────────────────────────

        private static string BuildSheetName(string userSheetName)
        {
            if (!string.IsNullOrWhiteSpace(userSheetName))
            {
                var bad   = new[] { '\\', '/', '?', '*', '[', ']', ':' };
                string sn = new string(userSheetName
                    .Where(c => !bad.Contains(c)).ToArray()).Trim();
                if (sn.Length > 31) sn = sn[..31];
                if (!string.IsNullOrWhiteSpace(sn)) return sn;
            }
            return "Results_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
        }

        private static SpreadsheetDocument CreateNewWorkbook(string filePath)
        {
            var d = SpreadsheetDocument.Create(filePath,
                SpreadsheetDocumentType.Workbook);
            var w = d.AddWorkbookPart();
            w.Workbook = new Workbook();
            w.Workbook.AppendChild(new Sheets());
            w.Workbook.Save();
            return d;
        }
    }
}
