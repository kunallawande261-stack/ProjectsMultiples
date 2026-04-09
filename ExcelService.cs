using System;
using System.Collections.Generic;
using System.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

public class ExcelService
{
    public static void FindCommonFiles(string filePath)
    {
        HashSet<string> columnASet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<string> commonFiles = new List<string>();

        using (SpreadsheetDocument doc = SpreadsheetDocument.Open(filePath, true))
        {
            WorkbookPart workbookPart = doc.WorkbookPart;

            Sheet firstSheet = workbookPart.Workbook.Sheets.Elements<Sheet>().First();
            WorksheetPart worksheetPart = (WorksheetPart)workbookPart.GetPartById(firstSheet.Id);

            SheetData sheetData = worksheetPart.Worksheet.GetFirstChild<SheetData>();

            foreach (Row row in sheetData.Elements<Row>())
            {
                Cell cellA = GetCell(row, "A");
                string value = GetCellValue(doc, cellA);

                if (!string.IsNullOrWhiteSpace(value))
                    columnASet.Add(value.Trim());
            }

            foreach (Row row in sheetData.Elements<Row>())
            {
                Cell cellB = GetCell(row, "B");
                string value = GetCellValue(doc, cellB);

                if (!string.IsNullOrWhiteSpace(value) && columnASet.Contains(value.Trim()))
                {
                    commonFiles.Add(value.Trim());
                }
            }

            commonFiles = commonFiles.Distinct().ToList();

            int index = 0;

            foreach (Row row in sheetData.Elements<Row>().Skip(1))
            {
                if (index >= commonFiles.Count)
                    break;

                Cell newCell = new Cell()
                {
                    CellReference = "D" + row.RowIndex,
                    DataType = CellValues.String,
                    CellValue = new CellValue(commonFiles[index])
                };

                row.Append(newCell);

                index++;
            }

            worksheetPart.Worksheet.Save();
        }
    }

    private static Cell GetCell(Row row, string columnName)
    {
        return row.Elements<Cell>()
            .FirstOrDefault(c => c.CellReference.Value.StartsWith(columnName));
    }

    private static string GetCellValue(SpreadsheetDocument doc, Cell cell)
    {
        if (cell == null) return "";

        string value = cell.InnerText;

        if (cell.DataType != null && cell.DataType == CellValues.SharedString)
        {
            return doc.WorkbookPart.SharedStringTablePart.SharedStringTable
                .ElementAt(int.Parse(value)).InnerText;
        }

        return value;
    }
}