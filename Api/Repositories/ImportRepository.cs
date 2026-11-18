using AMS.Data;
using AMS.Api.Services;
using AMS.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AMS.Api.Repositories;

/// The commit half of the Excel import (plan §4.7). The whole workbook
/// goes in inside ONE database transaction: customers, collateral groups
/// (sub-lends share the first lend's GroupId) and inline partial returns.
/// The import books NO CashLedger rows — the cash in/out ledger is its
/// own thing, populated only when someone actually does an entry in the
/// app (the counter's decision); the 2022 money left the drawer long ago.
/// Every imported row carries a provenance stamp in its Notes, and re-
/// importing the same file is a no-op: rows whose stamp is already on
/// file are skipped, not duplicated.
public interface IImportRepository
{
    Task<ImportResult> CommitAsync(ImportPreview data, decimal rate, Actor actor);
    Task SaveActivityAsync(Actor actor, string details);
}

public sealed class ImportRepository(AmsDbContext db, IActivityRepository activity) : IImportRepository
{
    public static string LendStamp(ImportPreview data, int row) =>
        $"Imported: {data.FileName} · {data.SheetUsed} · row {row}";

    public async Task<ImportResult> CommitAsync(ImportPreview data, decimal rate, Actor actor)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var result = new ImportResult();

        foreach (var g in data.Groups)
        {
            if (g.Lends.Count == 0)
            {
                result.GroupsSkipped++;
                result.ReturnsSkipped += g.Returns.Count;
                continue;
            }

            // Provenance + idempotency: if the group's first lend is
            // already on file with its import stamp, this file (or sheet
            // rows) were imported before — skip whole collaterals.
            var headStamp = LendStamp(data, g.Lends[0].Row);
            var headOnFile = await db.Entries
                .AnyAsync(e => e.Notes == headStamp);
            if (headOnFile)
            {
                result.GroupsSkipped++;
                foreach (var ret in g.Returns)
                    if (!await db.PartialReturns.AnyAsync(p => p.Notes == RetStamp(data, ret)))
                        result.ReturnsSkipped++;
                continue;
            }

            // Find-or-create the borrower by exact name (the same rule the
            // Add Entry dialog uses) — names repeat across collaterals but
            // must resolve to ONE customer row.
            var name = g.Name.Trim().ToLower();
            var customer = await db.Customers
                .FirstOrDefaultAsync(c => c.Name.Trim().ToLower() == name);
            if (customer is null)
            {
                customer = new Customer { Name = g.Name.Trim(), Phone = "", Address = "" };
                db.Customers.Add(customer);
                await db.SaveChangesAsync();
                result.CustomersCreated++;
            }
            else result.CustomersFound++;

            // The group: first lend FORGES the GroupId (insert → id →
            // stamp), the rest join it as sub-lends.
            var primary = new Entry
            {
                CustomerId = customer.Id,
                EntryDate = g.Lends[0].Date,
                ItemDescription = g.Item.Trim().Length > 0 ? g.Item.Trim() : "(no item in sheet)",
                AmountLend = g.Lends[0].Amount,
                InterestRate = rate > 0 ? rate : null,
                Status = "Active",
                Notes = headStamp,
                CreatedByUserId = actor.Id,
            };
            db.Entries.Add(primary);
            await db.SaveChangesAsync();
            primary.GroupId = primary.Id;

            foreach (var lend in g.Lends.Skip(1))
                db.Entries.Add(new Entry
                {
                    GroupId = primary.GroupId,
                    CustomerId = customer.Id,
                    EntryDate = lend.Date,
                    ItemDescription = primary.ItemDescription,
                    AmountLend = lend.Amount,
                    InterestRate = primary.InterestRate,
                    Status = "Active",
                    Notes = LendStamp(data, lend.Row),
                    CreatedByUserId = actor.Id,
                });
            await db.SaveChangesAsync();
            result.GroupsCreated++;
            result.LendsCreated += g.Lends.Count;

            // Inline partial returns, against the group. The FIFO/
            // outstanding invariants assume a payment never exceeds the
            // group's lends — a sheet row that overpays skips with a count
            // instead of corrupting an honest history.
            foreach (var ret in g.Returns)
            {
                var stamp = RetStamp(data, ret);
                if (await db.PartialReturns.AnyAsync(p => p.Notes == stamp))
                {
                    result.ReturnsSkipped++;
                    continue;
                }
                if (ret.Amount > g.Lends.Sum(l => l.Amount))
                {
                    result.ReturnsSkipped++;
                    continue;
                }
                db.PartialReturns.Add(new PartialReturn
                {
                    GroupId = primary.GroupId,
                    PaymentDate = ret.Date,
                    Amount = ret.Amount,
                    Notes = stamp,
                    RecordedByUserId = actor.Id,
                });
                result.ReturnsCreated++;
            }
            await db.SaveChangesAsync();
        }

        await tx.CommitAsync();
        return result;
    }

    public async Task SaveActivityAsync(Actor actor, string details)
    {
        await activity.AddAsync(actor, "Imported", "Import", 0, null, details);
        await activity.SaveAsync();
    }

    /// Re-import of the SAME money row: the stamp carries the sheet row,
    /// so a duplicate return resolves to the exact PartialReturn record.
    private static string RetStamp(ImportPreview data, ImportPreviewRow ret) =>
        (ret.Notes is null ? "" : ret.Notes + " — ")
        + $"{LendStamp(data, ret.Row)}";
}
