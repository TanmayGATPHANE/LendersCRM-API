using AMS.Api.Repositories;
using ClosedXML.Excel;

namespace AMS.Api.Services;

/** Excel lending-book import (plan §4.7). One method parses, one commits —
    previewing IS the rehearsal: Preview and Commit parse the same bytes
    the same way, so what was shown is what is written.
    Parsing rules (the counter's yearly template — e.g. "Girwi Year 2022
    Data to Import in AMS.xlsx"):
      · Columns: A Date · B Amount · C Interest (formula, ignored) ·
        D Name · E Item — header row.
      · Vertical MERGED Name+Item cells (D18:D20 + E18:E20) mean ONE
        customer lent against ONE collateral several times — sub-lends of
        one collateral group. An unmerged row is its own collateral even
        when the same name appears elsewhere on the sheet.
      · A NEGATIVE amount inside a group is a partial return the sheet
        owner wrote inline (e.g. "−7,000" + "25th Nov ko jama kar gye");
        that row's Item cell carries the payment's note.
      · Non-date A cells ("Year End") and empty rows are skipped with a
        warning, never an error. The interest-rate column is ignored —
        the commit applies ONE rate (the dialog's, default 2 %/month).
    Nothing here touches the DbContext — parsing is the service's rule
    layer; the commit hands off to IImportRepository (§layering). */
public interface IExcelImportService
{
    ImportPreview Parse(Stream stream, string fileName);
    Task<ImportPreview> PreviewAsync(Stream stream, string fileName);
    Task<ImportResult> CommitAsync(Stream stream, string fileName, decimal rate, Actor actor);
}

public sealed class ExcelImportService(IImportRepository imports) : IExcelImportService
{
    /// The sheet every yearly file keeps its working lends in.
    public const string ActiveSheet = "Active";

    public ImportPreview Parse(Stream stream, string fileName)
    {
        var wb = Open(stream);
        try
        {
            var sheets = wb.Worksheets.Select(w => w.Name).ToList();

            // The template names its sheet "Active"; tolerate the first
            // sheet when the counter renamed the whole file's tabs.
            var ws = wb.Worksheets.FirstOrDefault(w => w.Name == ActiveSheet)
                     ?? wb.Worksheets.FirstOrDefault()
                     ?? throw ApiException.BadRequest("The workbook has no sheets");
            var lastRow = ws.LastRowUsed()?.RowNumber()
                          ?? throw ApiException.BadRequest($"Sheet '{ws.Name}' is empty");

            var warnings = new List<string>();

            // The A..E header is the parse anchor — refuse a foreign layout
            // rather than guess what the columns mean.
            var headers = new[] { "Date", "Amount", "Interest", "Name", "Item" };
            for (var c = 1; c <= 5; c++)
            {
                var text = ws.Cell(1, c).GetString().Trim();
                if (!text.Equals(headers[c - 1], StringComparison.OrdinalIgnoreCase))
                    warnings.Add($"Column {c} header is \"{text}\" — expected \"{headers[c - 1]}\"");
            }

            // Sheet row → first row of the vertical merge the row's Name
            // (col 4) cell belongs to. Rows 18..20 of a D18:D20 merge all
            // map to 18. Only single-column merges count: a horizontal one
            // is a marker like the final "Year End" note, not a collateral.
            var mergeHead = new Dictionary<int, int>();
            foreach (var range in ws.MergedRanges)
            {
                var addr = range.RangeAddress;
                if (addr.FirstAddress.ColumnNumber != addr.LastAddress.ColumnNumber)
                    continue; // horizontal — a marker like the final "Year End" note
                var col = addr.FirstAddress.ColumnNumber;
                if (col != 4 && col != 5) continue;
                var head = addr.FirstAddress.RowNumber;
                foreach (var row in range.Rows())
                    mergeHead[row.RowNumber()] = head;
            }

            var groups = new List<ImportPreviewGroup>();
            ImportPreviewGroup? current = null;

            for (var r = 2; r <= lastRow; r++)
            {
                if (!TryDate(ws.Cell(r, 1), out var entryDate))
                {
                    var marker = ws.Cell(r, 1).GetString().Trim();
                    if (marker.Length > 0 || ws.Cell(r, 2).GetString().Trim().Length > 0)
                        warnings.Add($"Row {r} skipped — no date ({(marker.Length > 0 ? $"\"{marker}\"" : "empty")})");
                    continue; // spacer and "Year End" rows are never lends
                }
                if (!TryAmount(ws.Cell(r, 2), out var amount))
                {
                    warnings.Add($"Row {r} skipped — amount \"{ws.Cell(r, 2).GetString()}\" is not a number");
                    continue;
                }

                var name = ws.Cell(r, 4).GetString().Trim();
                var item = ws.Cell(r, 5).GetString().Trim();
                var inMerge = mergeHead.TryGetValue(r, out var headRow);

                if (!inMerge)
                {
                    // Unmerged row. A payment standing right under a group's
                    // rows (Name left alone, just an amount + note) still
                    // belongs to the collateral above it.
                    if (amount < 0 && current is not null)
                    {
                        current.Returns.Add(new ImportPreviewRow(
                            DateOnly.FromDateTime(entryDate), -amount, r, item.Length > 0 ? item : null));
                        continue;
                    }
                    if (amount < 0)
                    {
                        warnings.Add($"Row {r} skipped — negative amount with no collateral above it");
                        continue;
                    }
                    if (name.Length == 0 && item.Length == 0)
                    {
                        warnings.Add($"Row {r} skipped — an amount with no name and no item");
                        continue;
                    }
                    current = StartGroup(groups, name, item, r, warnings);
                    current.Lends.Add(new ImportPreviewRow(DateOnly.FromDateTime(entryDate), amount, r));
                    continue;
                }

                if (headRow == r)
                {
                    // The merge's anchor row — always a fresh collateral.
                    if (amount < 0)
                    {
                        warnings.Add($"Row {r} skipped — a merged block may not open with a negative amount");
                        continue;
                    }
                    current = StartGroup(groups, name, item, r, warnings);
                    current.Lends.Add(new ImportPreviewRow(DateOnly.FromDateTime(entryDate), amount, r));
                    continue;
                }

                // A continuation row of the block opened at headRow — one
                // more lend, or an inline partial return, same collateral.
                if (current is null)
                {
                    warnings.Add($"Row {r} skipped — continues a merge with no collateral above");
                    continue;
                }
                var note = item.Length > 0 ? item : null;
                if (amount < 0)
                    current.Returns.Add(new ImportPreviewRow(
                        DateOnly.FromDateTime(entryDate), -amount, r, note));
                else
                    current.Lends.Add(new ImportPreviewRow(
                        DateOnly.FromDateTime(entryDate), amount, r));
            }

            return new ImportPreview(fileName, sheets, ws.Name, groups, warnings);
        }
        finally { wb.Dispose(); }
    }

    public Task<ImportPreview> PreviewAsync(Stream stream, string fileName)
        => Task.FromResult(Parse(stream, fileName));

    public async Task<ImportResult> CommitAsync(Stream stream, string fileName, decimal rate, Actor actor)
    {
        if (rate < 0 || rate >= 100)
            throw ApiException.BadRequest("The interest rate must be between 0 and 100");
        var data = Parse(stream, fileName);
        if (data.Groups.Count == 0)
            throw ApiException.BadRequest("No lend rows were found on sheet '" + data.SheetUsed + "'");

        var result = await imports.CommitAsync(data, rate, actor);

        // ONE audit row for the whole import — per-row provenance already
        // rides in the Notes stamp of every lend and payment.
        await imports.SaveActivityAsync(actor,
            $"Import from \"{fileName}\": {result.LendsCreated} lends across " +
            $"{result.GroupsCreated} collaterals, {result.ReturnsCreated} partial returns" +
            (result.GroupsSkipped > 0 ? $", {result.GroupsSkipped} collaterals already in the books" : "") +
            (result.ReturnsSkipped > 0 ? $", {result.ReturnsSkipped} returns already recorded" : ""));
        return result;
    }

    // ── helpers ───────────────────────────────────────────────────────────

    private static XLWorkbook Open(Stream stream)
    {
        try { return new XLWorkbook(stream); }
        catch (Exception)
        {
            throw ApiException.BadRequest("Could not read the file — upload an .xlsx saved from Excel");
        }
    }

    private static ImportPreviewGroup StartGroup(
        List<ImportPreviewGroup> groups, string name, string item,
        int row, List<string> warnings)
    {
        if (name.Length == 0)
            warnings.Add($"Row {row} — a collateral with no name; it imports as a blank-name borrower");
        if (item.Length == 0)
            warnings.Add($"Row {row} — a collateral with no item description");
        var group = new ImportPreviewGroup(name, item, row, [], []);
        groups.Add(group);
        return group;
    }

    /// Dates: real Excel dates; a text fallback ("25-08-2022") for sheets
    /// saved with the column as text.
    private static bool TryDate(IXLCell cell, out DateTime date)
    {
        if (cell.TryGetValue<DateTime>(out date)) return true;
        return DateTime.TryParse(cell.GetString().Trim(), out date);
    }

    /// Amounts: real numbers from Excel; a text fallback that tolerates
    /// the "5,000" / "₹5,000" formatting the counter occasionally pastes.
    private static bool TryAmount(IXLCell cell, out decimal amount)
    {
        if (cell.TryGetValue<decimal>(out amount)) return true;
        var raw = cell.GetString().Trim().Replace("₹", "").Replace(",", "");
        return raw.Length > 0 && decimal.TryParse(raw, out amount);
    }
}
