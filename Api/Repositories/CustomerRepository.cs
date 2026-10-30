using AMS.Data;
using AMS.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AMS.Api.Repositories;

public interface ICustomerRepository
{
    Task<List<Customer>> ListAsync(string? search);
    /** Active entry count + outstanding (Σ active lends − their groups'
        partial returns) per customer id — computed, never stored (§5). */
    Task<Dictionary<int, (int ActiveCount, decimal Outstanding)>> ActiveStatsAsync();
    Task<Customer?> FindByNameAsync(string name);
    Task<Customer?> FindByIdAsync(int id);
    Task<bool> ExistsAsync(int id);
    Task AddAsync(Customer customer);
    Task<int> SaveAsync();
}

public sealed class CustomerRepository(AmsDbContext db) : ICustomerRepository
{
    public Task<List<Customer>> ListAsync(string? search)
    {
        var q = db.Customers.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            q = q.Where(c => c.Name.ToLower().Contains(s)
                          || c.Phone.Contains(s)
                          || c.Address.ToLower().Contains(s));
        }
        return q.OrderBy(c => c.Name).ToListAsync();
    }

    public async Task<Dictionary<int, (int ActiveCount, decimal Outstanding)>> ActiveStatsAsync()
    {
        var lends = await db.Entries.AsNoTracking()
            .Where(e => e.Status == "Active")
            .Select(e => new { e.CustomerId, e.GroupId, e.AmountLend })
            .ToListAsync();
        var paidByGroup = await db.PartialReturns.AsNoTracking()
            .GroupBy(p => p.GroupId)
            .Select(g => new { GroupId = g.Key, Paid = g.Sum(x => x.Amount) })
            .ToListAsync();
        var paid = paidByGroup.ToDictionary(x => x.GroupId, x => x.Paid);

        return lends
            .GroupBy(l => l.CustomerId)
            .ToDictionary(
                g => g.Key,
                g => (g.Count(), g.Sum(x => x.AmountLend) - g.Sum(x => paid.GetValueOrDefault(x.GroupId))));
    }

    public Task<Customer?> FindByNameAsync(string name)
    {
        var normalized = name.Trim().ToLower();
        return db.Customers.FirstOrDefaultAsync(c => c.Name.Trim().ToLower() == normalized);
    }

    public Task<Customer?> FindByIdAsync(int id) =>
        db.Customers.FirstOrDefaultAsync(c => c.Id == id);

    public Task<bool> ExistsAsync(int id) => db.Customers.AnyAsync(c => c.Id == id);

    public Task AddAsync(Customer customer)
    {
        db.Customers.Add(customer);
        return Task.CompletedTask;
    }

    public Task<int> SaveAsync() => db.SaveChangesAsync();
}