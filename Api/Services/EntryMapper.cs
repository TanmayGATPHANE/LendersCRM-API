using AMS.Data.Entities;

namespace AMS.Api.Services;

/** Maps EF entities to the API response shapes (Contracts). One place so
    every endpoint serializes an entry the same way. */
public static class EntryMapper
{
    public static EntryItem ToItem(Entry e, decimal paid, DateOnly today) => new()
    {
        Id = e.Id,
        GroupId = e.GroupId,
        CustomerId = e.CustomerId,
        CustomerName = e.Customer?.Name ?? "?",
        CustomerAddress = e.Customer?.Address ?? "",
        Date = e.EntryDate,
        Year = e.EntryYear,   // the stored lending year (business requirement)
        Item = e.ItemDescription,
        Amount = e.AmountLend,
        Rate = e.InterestRate,
        Status = e.Status,
        // Live accrual on the outstanding principal; frozen value once released.
        Interest = e.Status == "Released"
            ? e.InterestCollected ?? 0m
            : Interest.Accrued(e.AmountLend - paid, e.InterestRate, e.EntryDate, today),
        Paid = paid,
        Notes = e.Notes,
        ReleasedDate = e.ReleasedDate,
        InterestCollected = e.InterestCollected,
        AmountReturned = e.AmountReturned,
        TotalCollected = e.TotalCollected,
    };
}