using AMS.Data;
using AMS.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AMS.Api.Repositories;

public interface IEntryRepository
{
    /** All entries with their customer, newest-eligible filters applied.
        Tracked: false for reads (lists); services that mutate fetch via
        FindByIdAsync instead. */
    Task<List<Entry>> ListAsync(string? status, string? search);
    /// The header's global search (plan §3.1): item, customer name or
    /// notes contain the text; a numeric query also matches lend amounts.
    Task<List<Entry>> SearchAsync(string search, int take);
    Task<List<Entry>> ListForGroupAsync(int groupId, bool tracked = false);
    Task<Entry?> FindByIdAsync(int id, bool tracked = true);
    Task<bool> CustomerExistsAsync(int customerId);
    Task AddAsync(Entry entry);
    /** A fresh collateral: its GroupId BECOMES its own id. Two saves under
        one database transaction (insert, then stamp GroupId = Id). */
    Task<(int Id, int GroupId)> AddAsOwnGroupAsync(Entry entry);
    Task<int> SaveAsync();
}

public sealed class EntryRepository(AmsDbContext db) : IEntryRepository
{
    public Task<List<Entry>> ListAsync(string? status, string? search)
    {
        IQueryable<Entry> q = db.Entries.AsNoTracking().Include(e => e.Customer);
        if (!string.IsNullOrWhiteSpace(status))
            q = q.Where(e => e.Status == status);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            q = q.Where(e => e.ItemDescription.ToLower().Contains(s)
                          || e.Customer!.Name.ToLower().Contains(s));
        }
        return q.OrderBy(e => e.EntryDate).ThenBy(e => e.Id).ToListAsync();
    }

    public Task<List<Entry>> SearchAsync(string search, int take)
    {
        var s = search.Trim().ToLower();
        // A numeric query also finds lends by their amount ("20000").
        IQueryable<Entry> q = s.Length > 0 && decimal.TryParse(s, out var val)
            ? db.Entries.AsNoTracking().Include(e => e.Customer).Where(e =>
                  e.AmountLend == val
                  || e.ItemDescription.ToLower().Contains(s)
                  || e.Customer!.Name.ToLower().Contains(s)
                  || e.Notes.ToLower().Contains(s))
            : db.Entries.AsNoTracking().Include(e => e.Customer).Where(e =>
                  e.ItemDescription.ToLower().Contains(s)
                  || e.Customer!.Name.ToLower().Contains(s)
                  || e.Notes.ToLower().Contains(s));
        // Newest first — the recent transactions matter most in a search.
        return q.OrderByDescending(e => e.EntryDate).ThenByDescending(e => e.Id)
            .Take(take).ToListAsync();
    }

    public IQueryable<Entry> GroupQuery(int groupId, bool tracked) =>
        tracked
            ? db.Entries.Include(e => e.Customer).Where(e => e.GroupId == groupId)
            : db.Entries.AsNoTracking().Include(e => e.Customer).Where(e => e.GroupId == groupId);

    public Task<List<Entry>> ListForGroupAsync(int groupId, bool tracked = false) =>
        GroupQuery(groupId, tracked).OrderBy(e => e.EntryDate).ThenBy(e => e.Id).ToListAsync();

    public Task<Entry?> FindByIdAsync(int id, bool tracked = true) =>
        tracked
            ? db.Entries.FirstOrDefaultAsync(e => e.Id == id)
            : db.Entries.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id);

    public Task<bool> CustomerExistsAsync(int customerId) =>
        db.Customers.AnyAsync(c => c.Id == customerId);

    public Task AddAsync(Entry entry)
    {
        db.Entries.Add(entry);
        return Task.CompletedTask;
    }

    public async Task<(int Id, int GroupId)> AddAsOwnGroupAsync(Entry entry)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        db.Entries.Add(entry);
        await db.SaveChangesAsync();
        entry.GroupId = entry.Id;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return (entry.Id, entry.GroupId);
    }

    public Task<int> SaveAsync() => db.SaveChangesAsync();
}