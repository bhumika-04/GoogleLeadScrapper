using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using CsvHelper;
using DeepLead.Core.Contracts;

namespace DeepLead.Export;

public static class LeadExporter
{
    private const int MaxAspectSheets = 200;   // beyond this a workbook becomes unusable; "All leads" still has every row

    private static readonly string[] Headers =
    [
        "Keyword", "City", "Rank", "Company", "Category", "Phones", "Emails", "Website", "Social profiles",
        "Address", "Rating", "Reviews", "Status", "Latitude", "Longitude", "Google Maps", "Found at (UTC)",
    ];

    private static object?[] ToCells(LeadRowDto r) =>
    [
        r.Keyword, r.City, r.MapsRank, r.Name, r.Category, r.Phones, r.Emails, r.Website, r.Socials,
        r.Address, r.Rating, r.ReviewCount, r.BusinessStatus, r.Latitude, r.Longitude, r.MapsUrl, r.FoundAt,
    ];

    /// <summary>Workbook with an "All leads" sheet plus one sheet per aspect (keyword — city), as the leads are kept separately per aspect.</summary>
    public static byte[] ToExcel(string sessionName, IReadOnlyList<LeadRowDto> rows)
    {
        using var workbook = new XLWorkbook();
        WriteSheet(workbook, "All leads", rows);

        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "All leads" };
        foreach (var group in rows.GroupBy(r => r.AspectId).Take(MaxAspectSheets))
        {
            var first = group.First();
            WriteSheet(workbook, UniqueSheetName($"{first.Keyword} - {first.City}", usedNames), group.ToList());
        }

        workbook.Properties.Title = sessionName;
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public static byte[] ToCsv(IReadOnlyList<LeadRowDto> rows)
    {
        using var stream = new MemoryStream();
        // UTF-8 with BOM so Excel opens Hindi/Arabic text correctly.
        using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)))
        using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
        {
            foreach (var h in Headers)
                csv.WriteField(h);
            csv.NextRecord();

            foreach (var row in rows)
            {
                foreach (var cell in ToCells(row))
                    csv.WriteField(cell is DateTime d ? d.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : cell);
                csv.NextRecord();
            }
        }
        return stream.ToArray();
    }

    private static void WriteSheet(XLWorkbook workbook, string name, IReadOnlyList<LeadRowDto> rows)
    {
        var sheet = workbook.Worksheets.Add(name);
        for (var col = 0; col < Headers.Length; col++)
            sheet.Cell(1, col + 1).Value = Headers[col];

        for (var i = 0; i < rows.Count; i++)
        {
            var cells = ToCells(rows[i]);
            for (var col = 0; col < cells.Length; col++)
                sheet.Cell(i + 2, col + 1).Value = XLCellValue.FromObject(cells[col]);
        }

        var header = sheet.Row(1);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#1F3A5F");
        header.Style.Font.FontColor = XLColor.White;
        sheet.SheetView.FreezeRows(1);
        if (rows.Count > 0)
            sheet.Range(1, 1, rows.Count + 1, Headers.Length).SetAutoFilter();
        sheet.Column(17).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
        sheet.Columns(1, Headers.Length).AdjustToContents(1, Math.Min(rows.Count + 1, 200), 8, 60);
    }

    private static string UniqueSheetName(string raw, HashSet<string> used)
    {
        var cleaned = new string(raw.Select(ch => "[]:*?/\\".Contains(ch) ? ' ' : ch).ToArray()).Trim();
        var name = cleaned.Length > 31 ? cleaned[..31] : cleaned;
        for (var n = 2; !used.Add(name); n++)
        {
            var suffix = $" ({n})";
            name = (cleaned.Length > 31 - suffix.Length ? cleaned[..(31 - suffix.Length)] : cleaned) + suffix;
        }
        return name;
    }
}
