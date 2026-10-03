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
        "Keyword", "City", "Rank", "Company", "Category", "Owner", "Core team", "Phones", "Emails", "Website", "Social profiles",
        "Team size", "Turnover", "GSTIN", "Address", "Rating", "Reviews", "Status", "Latitude", "Longitude", "Google Maps", "Found at (UTC)",
    ];

    private static readonly string[] PeopleHeaders =
    [
        "Keyword", "City", "Company", "Name", "Designation", "Owner", "Decision maker", "Phone", "Email",
        "LinkedIn", "Facebook", "Instagram", "Found via", "Source page",
    ];

    private static object?[] ToCells(LeadRowDto r) =>
    [
        r.Keyword, r.City, r.MapsRank, r.Name, r.Category, r.OwnerName, r.People, r.Phones, r.Emails, r.Website, r.Socials,
        r.TeamSize, r.Turnover, r.Gstin, r.Address, r.Rating, r.ReviewCount, r.BusinessStatus, r.Latitude, r.Longitude, r.MapsUrl, r.FoundAt,
    ];

    private static object?[] ToCells(PersonExportRow p) =>
    [
        p.Keyword, p.City, p.Company, p.FullName, p.Designation, p.IsOwner ? "Yes" : "", p.IsDecisionMaker ? "Yes" : "", p.Phone, p.Email,
        p.LinkedInUrl, p.FacebookUrl, p.InstagramUrl, p.Source, p.SourceUrl,
    ];

    /// <summary>
    /// Workbook: "All leads", "People" (one row per owner/team member), then one sheet per aspect (keyword — city),
    /// as leads are kept separately per aspect.
    /// </summary>
    public static byte[] ToExcel(string sessionName, IReadOnlyList<LeadRowDto> rows, IReadOnlyList<PersonExportRow> people)
    {
        using var workbook = new XLWorkbook();
        WriteSheet(workbook, "All leads", Headers, rows.Select(ToCells).ToList());
        WriteSheet(workbook, "People", PeopleHeaders, people.Select(ToCells).ToList());

        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "All leads", "People" };
        foreach (var group in rows.GroupBy(r => r.AspectId).Take(MaxAspectSheets))
        {
            var first = group.First();
            WriteSheet(workbook, UniqueSheetName($"{first.Keyword} - {first.City}", usedNames), Headers, group.Select(ToCells).ToList());
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

    private static void WriteSheet(XLWorkbook workbook, string name, string[] headers, IReadOnlyList<object?[]> rows)
    {
        var sheet = workbook.Worksheets.Add(name);
        for (var col = 0; col < headers.Length; col++)
            sheet.Cell(1, col + 1).Value = headers[col];

        for (var i = 0; i < rows.Count; i++)
        {
            var cells = rows[i];
            for (var col = 0; col < cells.Length; col++)
            {
                var cell = sheet.Cell(i + 2, col + 1);
                cell.Value = XLCellValue.FromObject(cells[col]);
                if (cells[col] is DateTime)
                    cell.Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
            }
        }

        var header = sheet.Row(1);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#1F3A5F");
        header.Style.Font.FontColor = XLColor.White;
        sheet.SheetView.FreezeRows(1);
        if (rows.Count > 0)
            sheet.Range(1, 1, rows.Count + 1, headers.Length).SetAutoFilter();
        sheet.Columns(1, headers.Length).AdjustToContents(1, Math.Min(rows.Count + 1, 200), 8, 60);
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
