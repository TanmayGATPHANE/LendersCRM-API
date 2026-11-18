using AMS.Api.Repositories;
using AMS.Data.Entities;

namespace AMS.Api.Services;

public interface IOutsourceService
{
    Task<List<OutsourcedRow>> ListAsync();
    Task OutsourceAsync(int groupId, NewOutsourceRequest req, Actor actor);
    Task ReturnAsync(int groupId, OutsourceReturnRequest req, Actor actor);
}

/** Outsourcing (§4.8): one collateral handed to an outside party. Going
    out stamps IsOutsourced on EVERY lend of the group and hides it from
    the dashboard / reports / customers views (the list/search/stats
    queries all filter the flag); the Outsourced page is where it shows.
    Marking it RETURNED clears the flag the same moment the OutsourcedItems
    row is stamped — never a half-state between the two tables. */
public sealed class OutsourceService(
    IOutsourceRepository outsourced, IEntryRepository entries,
    IActivityRepository activity) : IOutsourceService
{
    public async Task<List<OutsourcedRow>> ListAsync()
    {
        var rows = await outsourced.ListOpenAsync();
        if (rows.Count == 0) return [];
        var groupIds = rows.Select(x => x.GroupId).Distinct().ToList();

        // The group's items/customers/amounts — straight from the lends.
        var lends = await entries.ListForGroupsAsync(groupIds);
        var primaryByGroup = lends
            .GroupBy(e => e.GroupId)
            .ToDictionary(g => g.Key, g => g.OrderBy(e => (e.EntryDate, e.Id)).First());

        return rows.Select(r =>
        {
            var primary = primaryByGroup[r.GroupId];
            return new OutsourcedRow(
                GroupId: r.GroupId,
                Customer: primary.Customer?.Name ?? $"customer #{primary.CustomerId}",
                Item: primary.ItemDescription,
                AmountLent: lends.Where(e => e.GroupId == r.GroupId).Sum(e => e.AmountLend),
                OutsourcedTo: r.OutsourcedTo,
                Date: r.OutsourceDate,
                Notes: r.Notes);
        }).ToList();
    }

    public async Task OutsourceAsync(int groupId, NewOutsourceRequest req, Actor actor)
    {
        var to = (req.To ?? "").Trim();
        if (to.Length == 0) throw ApiException.BadRequest("Outsourced to — name the person/shop");
        var groupEntries = await entries.ListForGroupAsync(groupId, tracked: true);
        if (groupEntries.Count == 0) throw ApiException.NotFound("Collateral group not found");
        if (groupEntries.Any(e => e.IsOutsourced))
            throw ApiException.Conflict("This collateral is already outsourced");

        var primary = groupEntries.OrderBy(e => (e.EntryDate, e.Id)).First();
        var stamp = DateTime.UtcNow;
        groupEntries.ForEach(e => e.IsOutsourced = true);
        groupEntries.ForEach(e => e.ModifiedAt = stamp);
        await entries.SaveAsync();

        var open = new OutsourcedItem
        {
            GroupId = groupId,
            OutsourcedTo = to,
            OutsourceDate = req.Date,
            Notes = req.Notes,
            CreatedByUserId = actor.Id,
        };
        await outsourced.AddAsync(open);
        await outsourced.SaveAsync();

        // The audit row: which collateral went to whom.
        await activity.AddAsync(actor, "Updated", "Group", groupId, groupId,
            $"Outsourced collateral — {primary.ItemDescription} " +
            $"({(primary.Customer?.Name ?? $"customer #{primary.CustomerId}")}) to {to}");
        await activity.SaveAsync();
    }

    public async Task ReturnAsync(int groupId, OutsourceReturnRequest req, Actor actor)
    {
        var groupEntries = await entries.ListForGroupAsync(groupId, tracked: true);
        if (groupEntries.Count == 0) throw ApiException.NotFound("Collateral group not found");
        if (groupEntries.All(e => !e.IsOutsourced))
            throw ApiException.Conflict("This collateral is not outsourced");
        var open = await outsourced.FindOpenForGroupAsync(groupId, tracked: true)
            ?? throw ApiException.Conflict("This collateral is not outsourced");

        var primary = groupEntries.OrderBy(e => (e.EntryDate, e.Id)).First();
        groupEntries.ForEach(e => e.IsOutsourced = false);
        groupEntries.ForEach(e => e.ModifiedAt = DateTime.UtcNow);
        await entries.SaveAsync();

        open.ReturnedDate = req.DateReturned;
        await outsourced.SaveAsync();

        // The audit row: the collateral is home again.
        await activity.AddAsync(actor, "Updated", "Group", groupId, groupId,
            $"Outsourced item returned — {primary.ItemDescription} " +
            $"({(primary.Customer?.Name ?? $"customer #{primary.CustomerId}")}) from {open.OutsourcedTo}");
        await activity.SaveAsync();
    }
}
