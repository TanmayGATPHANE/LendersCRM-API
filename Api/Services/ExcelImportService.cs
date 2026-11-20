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
    /// The sheet the yearly file keeps its already-returned loans in (§4.7.1).
    public const string ReleasedSheet = "Released";

    public ImportPreview Parse(Stream stream, string fileName)
    {
        var wb = Open(stream);
        try
        {
            var sheets = wb.Worksheets.Select(w => w.Name).ToList();

            // Sheet pick: when the workbook carries a 'Released' sheet it is
            // the already-returned book — import THAT (§4.7.1) and leave the
            // 'Active' sheet alone (it is the still-open book, which in a
            // release file is usually the very data already on the books).
            var ws = wb.Worksheets.FirstOrDefault(w => w.Name == ReleasedSheet)
                     ?? wb.Worksheets.FirstOrDefault(w => w.Name == ActiveSheet)
                     ?? wb.Worksheets.FirstOrDefault()
                     ?? throw ApiException.BadRequest("The workbook has no sheets");
            var kind = ws.Name == ReleasedSheet ? "Released" : "Active";

            var warnings = new List<string>();
            if (kind == "Released" && wb.Worksheets.Any(w => w.Name == ActiveSheet))
                warnings.Add($"This workbook also has an '{ActiveSheet}' sheet — skipped. " +
                             "The 'Released' sheet is what this file imports; the open book lives on its own file.");

            var groups = new List<ImportPreviewGroup>();

            var lastRow = ws.LastRowUsed()?.RowNumber()
                          ?? throw ApiException.BadRequest($"Sheet '{ws.Name}' is empty");

            if (kind == "Released")
            {
                ParseReleased(ws, groups, warnings, lastRow);
            }
            else
            {
                ParseActive(ws, groups, warnings, lastRow);
            }

            return new ImportPreview(fileName, sheets, ws.Name, kind, groups, warnings);
        }
        finally { wb.Dispose(); }
    }

    /** The ACTIVE lending book (§4.7): one row per lend, merged Name+Item
        blocks folding into one collateral, negative rows as returns. */
    private static void ParseActive(IXLWorksheet ws, List<ImportPreviewGroup> groups,
        List<string> warnings, int lastRow)
    {
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
    }

    /** The RELEASED book (§4.7.1): one row per collateral RETURNED in the
        year — lend Date · lent Amount · released-on · the interest the
        release brought back · Name · Item. The sheet drifts (text dates,
        a shifted column tail where the interest rides in column C and the
        release date rides in F) and multi-lend releases show as merged
        C/Name blocks — all handled here. Nothing is cash-booked (imports
        stay cash-silent per §4.7). */
    private static void ParseReleased(IXLWorksheet ws, List<ImportPreviewGroup> groups,
        List<string> warnings, int lastRow)
    {
        // Row → the first row of the vertical merge its cell belongs to,
        // for the release-date (3), interest (4) and name (5) columns.
        var mergeHead = new Dictionary<(int Row, int Col), int>();
        foreach (var range in ws.MergedRanges)
        {
            var addr = range.RangeAddress;
            if (addr.FirstAddress.ColumnNumber != addr.LastAddress.ColumnNumber)
                continue;
            var col = addr.FirstAddress.ColumnNumber;
            if (col is < 3 or > 5) continue;
            var head = addr.FirstAddress.RowNumber;
            foreach (var row in range.Rows())
                mergeHead[(row.RowNumber(), col)] = head;
        }

        // The value a merged cell reads: the anchor's content, "" when the
        // row merely rides inside the merge.
        string ValueAt(int row, int col)
        {
            var anchor = mergeHead.TryGetValue((row, col), out var head)
                ? (row != head ? head : row)
                : row;
            return anchor == row ? ws.Cell(row, col).GetString().Trim() : "";
        }
        bool IsAnchor(int row, int col) =>
            !mergeHead.TryGetValue((row, col), out var head) || head == row;

        ImportPreviewGroup? current = null;
        var headByRow = new Dictionary<int, ImportPreviewGroup>();

        for (var r = 2; r <= lastRow; r++)
        {
            if (!TryDate(ws.Cell(r, 1), out var lendDate))
            {
                var marker = ws.Cell(r, 1).GetString().Trim();
                if (marker.Length > 0 || ws.Cell(r, 2).GetString().Trim().Length > 0)
                    warnings.Add($"Row {r} skipped — no lend date ({(marker.Length > 0 ? $"\"{marker}\"" : "empty")})");
                continue;
            }
            if (!TryAmount(ws.Cell(r, 2), out var amount) || amount <= 0)
            {
                warnings.Add($"Row {r} skipped — amount \"{ws.Cell(r, 2).GetString()}\" is not a positive number");
                continue;
            }

            // The release day: column C, or F for the sheet's shifted tail;
            // both read real dates AND text dates ("23.12.2025").
            DateOnly? releaseDate = null;
            decimal? collectedInterest = null;
            var cCell = ws.Cell(r, 3);
            if (IsAnchor(r, 3))
            {
                if (TryDateCell(cCell, out var rd))
                    releaseDate = DateOnly.FromDateTime(rd);
                else if (TryAmount(cCell, out var interestCell) && interestCell != 0)
                    collectedInterest = interestCell; // the sheet's shifted tail: C = interest
            }
            var fText = ValueAt(r, 6);
            if (releaseDate is null && TryDateCell(ws.Cell(r, 6), out var fd))
                releaseDate = DateOnly.FromDateTime(fd);
            else if (releaseDate is null && TryTextDate(fText, out var fdt))
                releaseDate = DateOnly.FromDateTime(fdt);
            // Column G is the sheet's duplicate release-date column (its
            // shifted tail rows carry the day there) — the last resort.
            if (releaseDate is null && TryDateCell(ws.Cell(r, 7), out var gd))
                releaseDate = DateOnly.FromDateTime(gd);
            else if (releaseDate is null && TryTextDate(ws.Cell(r, 7).GetString().Trim(), out var gdt2))
                releaseDate = DateOnly.FromDateTime(gdt2);
            // Column E anchor (name) — the customer of the block.
            var eHead = mergeHead.TryGetValue((r, 5), out var eHeadRow) ? eHeadRow : r;
            var eAnchor = IsAnchor(r, 5) ? r : eHead;
            var name = ws.Cell(eAnchor, 5).GetString().Trim();
            var itemAnchorRow = IsAnchor(r, 6) ? r : eAnchor;
            var item = ws.Cell(itemAnchorRow, 6).GetString().Trim();

            // The sheet's SHIFTED TAIL (from ~row 291): the NAME rides in
            // column D — the interest column's usual place — with the item
            // beside it in E. A text D on a lend row is that name.
            var dText = ws.Cell(r, 4).GetString().Trim();
            if (dText.Length > 0 && !decimal.TryParse(dText, out _))
            {
                name = dText;
                item = ws.Cell(r, 5).GetString().Trim();
            }

            // Continuation FIRST: the row rides inside a merged
            // release/name block, OR stands UNMARKED directly under the
            // open block (no release day, no own interest — the bare rows
            // like 2022-07-30, and the "Shahid/Sahid Nizampur — 2,000 —

            // 13000(08.02)+2000(05.04)" second lend, whose name the sheet
            // spelled a touch differently). Continuation rows never carry
            // their own release day.
            var lastLendRow = current is not null && current.Lends.Count > 0
                ? current.Lends[^1].Row
                : -1;
            var isContinuation =
                (eAnchor != r && !IsAnchor(r, 5)) // rides the name merge
                || (current is not null
                    && !releaseDate.HasValue
                    && !collectedInterest.HasValue
                    && (name.Length == 0 || r == lastLendRow + 1));

            if (isContinuation)
            {
                if (current is null)
                {
                    warnings.Add($"Row {r} skipped — continues a release block with no collateral above");
                    continue;
                }
                // Interest rides on the block: a continuation row carries
                // none (the anchor row was collected for the whole block).
                // A note-like item text ("13000(08.02)+2000(05.04)") is
                // preserved on the lend row.
                current.Lends.Add(new ImportPreviewRow(
                    DateOnly.FromDateTime(lendDate), amount, r,
                    item.Length > 0 ? item : null, current.ReleasedDate, null));
                continue;
            }

            // A released collateral with NO release date anywhere (C, F or
            // G) cannot land in the books as returned — skip, loudly; the
            // owner completes the sheet.
            if (!releaseDate.HasValue)
            {
                warnings.Add($"Row {r} skipped — no release date for \"{(name.Length > 0 ? name : "the row")}\" " +
                             "(a returned collateral needs the day it came back)");
                continue;
            }

            // An anchor of a block, or a stand-alone row.
            var dCell = ws.Cell(r, 4);
            var interestForGroup = collectedInterest
                ?? (IsAnchor(r, 4) && TryAmount(dCell, out var dNum) ? dNum : 0m);
            var group = StartReleasedGroup(groups, name, item, r, releaseDate, interestForGroup, warnings);
            group.Lends.Add(new ImportPreviewRow(
                DateOnly.FromDateTime(lendDate), amount, r, null, releaseDate, interestForGroup));
            current = group;
        }

        // Equal division (§4.7.1 — the counter's rule): when a release's
        // collected interest was recorded OVER several lends (the merged
        // D64:D67 over four names, or a block's anchor figure), it is
        // divided in EQUAL parts across the block's lends, the odd paisa
        // landing on the last lend. Single-lend blocks keep it whole.
        for (var gi = 0; gi < groups.Count; gi++)
        {
            var g = groups[gi];
            if (g.ReleasedDate is null) continue;
            var interest = g.InterestCollected ?? 0m;
            var count = g.Lends.Count;
            if (interest <= 0 || count == 0) continue;
            if (count == 1)
            {
                groups[gi] = g with { Lends = [g.Lends[0] with { Interest = interest }] };
                continue;
            }
            var share = decimal.Round(interest / count, MidpointRounding.AwayFromZero);
            var remainder = interest - share * (count - 1); // the odd paisa
            var split = new List<ImportPreviewRow>(count);
            for (var i = 0; i < count; i++)
                split.Add(g.Lends[i] with { Interest = i == count - 1 ? remainder : share });
            groups[gi] = g with { Lends = split };
        }
    }

    /** A released collateral: status Released, released ON the sheet's
        day, the interest the release collected on the primary lend. */
    private static ImportPreviewGroup StartReleasedGroup(
        List<ImportPreviewGroup> groups, string name, string item,
        int row, DateOnly? releasedDate, decimal interest, List<string> warnings)
    {
        if (name.Length == 0)
            warnings.Add($"Row {row} — a returned collateral with no name; it imports as a blank-name borrower");
        if (item.Length == 0)
            warnings.Add($"Row {row} — a returned collateral with no item description");
        var group = new ImportPreviewGroup(name, item, row, [], [], releasedDate, interest, null);
        groups.Add(group);
        return group;
    }

    /// A date cell or a text date in the sheet's hand formats.
    private static bool TryDateCell(IXLCell cell, out DateTime date)
    {
        if (cell.TryGetValue<DateTime>(out date)) return true;
        return TryTextDate(cell.GetString().Trim(), out date);
    }

    /// Text dates as the counter writes them: dd.MM.yyyy (most common in
    /// the release book), then the separator-free and dash variants.
    private static bool TryTextDate(string text, out DateTime date)
    {
        var formats = new[] { "dd.MM.yyyy", "dd-MM-yyyy", "dd/MM/yyyy", "yyyy-MM-dd", "dd.MM.yy" };
        foreach (var f in formats)
            if (DateTime.TryParseExact(text, f, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out date))
                return true;
        return DateTime.TryParse(text, out date);
    }

    public async Task<ImportPreview> PreviewAsync(Stream stream, string fileName)
    {
        var data = Parse(stream, fileName);
        await EnsureYearsFreeAsync(data); // the gate runs on the preview too
        return data;
    }

    public async Task<ImportResult> CommitAsync(Stream stream, string fileName, decimal rate, Actor actor)
    {
        if (rate < 0 || rate >= 100)
            throw ApiException.BadRequest("The interest rate must be between 0 and 100");
        var data = Parse(stream, fileName);
        if (data.Groups.Count == 0)
            throw ApiException.BadRequest("No lend rows were found on sheet '" + data.SheetUsed + "'");
        await EnsureYearsFreeAsync(data);

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

    /** The one-import-per-year gate (§4.7): if ANY year of the sheet's
        rows already has entries in the database, the import is REFUSED —
        no partial imports, first the error. The preview runs the same
        check, so the dialog refuses before a commit is even offered.
        The gate guards the ACTIVE book only: a 'Released' sheet shares
        its lending years with the active rows already on the books —
        its re-imports are caught by the per-row stamps instead. */
    private async Task EnsureYearsFreeAsync(ImportPreview data)
    {
        if (data.Kind == "Released") return;
        var years = data.Groups
            .SelectMany(g => g.Lends.Select(l => l.Date.Year)
                .Concat(g.Returns.Select(r => r.Date.Year)))
            .Distinct()
            .ToList();
        if (years.Count == 0) return;
        var present = await imports.YearsAlreadyPresentAsync(years);
        if (present.Count > 0)
        {
            var ordered = present.OrderBy(y => y).ToList();
            var yearsText = ordered.Count == 1 ? $"Year {ordered[0]}" : $"The years {string.Join(" and ", ordered)}";
            throw ApiException.Conflict(
                $"{yearsText} already have entries in the database — a year can be imported only once. " +
                "Nothing was imported.");
        }
    }

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
