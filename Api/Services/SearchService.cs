using AMS.Api.Repositories;
using AMS.Data.Entities;

namespace AMS.Api.Services;

public interface ISearchService
{
    Task<SearchResults> SearchAsync(string query, bool includeUsers);
}

/// The header's global search (plan §3.1): one endpoint fanning out across
/// customers, users and transactions. The server owns the 3-character gate
/// (a shorter query never reaches the data) and each section's cap.
public sealed class SearchService(
    ICustomerRepository customers,
    IUserRepository users,
    IEntryRepository entries,
    IPaymentRepository payments) : ISearchService
{
    /// Mirrors the frontend box — 3+ characters before anything fires.
    private const int MinChars = 3;

    public async Task<SearchResults> SearchAsync(string query, bool includeUsers)
    {
        var q = query.Trim();
        if (q.Length < MinChars)
            return new SearchResults([], [], []);
        var text = q.ToLower();

        // Customers: name, phone or address contain the text (repo does the
        // matching; the service caps the list).
        var custs = (await customers.ListAsync(q))
            .Take(5)
            .Select(c => new SearchHit(
                c.Id, "customer", c.Name, c.Address, null, null))
            .ToList();

        // Users: name or email. Only Admin callers even ask — Lender and
        // ViewOnly results stay empty (accounts are never visible to them).
        var usrs = !includeUsers
            ? []
            : (await users.ListAsync())
                .Where(u => u.Name.ToLower().Contains(text) || u.Email.ToLower().Contains(text))
                .Take(5)
                .Select(u => new SearchHit(
                    u.Id, "user", u.Name, $"{u.Role} · {u.Email}", null, null))
                .ToList();

        // Transactions need the group context (a lend hit is titled by the
        // group's primary item + its customer, like the dashboard shows it).
        var allEntries = await entries.ListAsync(null, null);
        var byGroup = allEntries
            .GroupBy(e => e.GroupId)
            .ToDictionary(g => g.Key, g => g.OrderBy(e => e.EntryDate).ThenBy(e => e.Id).First());

        // Lends: item, customer name or notes contain the text; a numeric
        // query also matches the amount (EntryRepository does the matching
        // and orders newest-first).
        var lends = (await entries.SearchAsync(q, take: 8))
            .Select(e =>
            {
                // The hit is titled by the GROUP's primary lend (what the
                // dashboard shows), not the matched sub-lend row.
                var primary = byGroup.GetValueOrDefault(e.GroupId);
                return new SearchHit(
                    e.Id, "lend", $"{primary?.Customer?.Name} — {primary?.ItemDescription}",
                    $"{e.EntryDate:yyyy-MM-dd} · {e.Status}",
                    primary?.AmountLend, e.GroupId);
            })
            .ToList();

        // Partial returns: the payments table is small — notes-contains or a
        // numeric amount match in memory, joined to the group's lend.
        var numeric = decimal.TryParse(q, out var val);
        var pays = (await payments.ListAsync())
            .Where(p => p.Notes != null && p.Notes.ToLower().Contains(text)
                                    || numeric && p.Amount == val)
            .OrderByDescending(p => p.PaymentDate).ThenByDescending(p => p.Id)
            .Take(8)
            .Select(p =>
            {
                var primary = byGroup.GetValueOrDefault(p.GroupId);
                return new SearchHit(
                    p.Id, "payment",
                    $"{primary?.Customer?.Name} — {primary?.ItemDescription}",
                    $"{p.PaymentDate:yyyy-MM-dd} · partial return", p.Amount, p.GroupId);
            })
            .ToList();

        return new SearchResults(custs, usrs, [.. lends, .. pays]);
    }
}