using AMS.Api.Repositories;
using AMS.Data.Entities;

namespace AMS.Api.Services;

public interface IPaymentService
{
    Task<List<PaymentItem>> ListAsync();
    Task<PaymentItem> AddToGroupAsync(int groupId, NewPaymentRequest req, int? userId);
    Task<PaymentItem> UpdateAsync(int id, UpdatePaymentRequest req);
}

/** Partial returns (plan §4.6): list all, record one against a collateral
    group, correct one. Amounts validate against the group's outstanding —
    oldest lend reduced first. */
public sealed class PaymentService(
    IPaymentRepository payments, IEntryRepository entries, ICashLedgerRepository cash) : IPaymentService
{
    public async Task<List<PaymentItem>> ListAsync()
    {
        var list = await payments.ListAsync();
        return list.Select(PaymentMapper.ToItem).ToList();
    }

    public async Task<PaymentItem> AddToGroupAsync(int groupId, NewPaymentRequest req, int? userId)
    {
        var group = await entries.ListForGroupAsync(groupId);
        if (group.Count == 0) throw ApiException.NotFound("Collateral group not found");
        if (group.All(e => e.Status != "Active"))
            throw ApiException.Conflict("This collateral is already released");

        if (req.Amount <= 0) throw ApiException.BadRequest("Enter the amount received");
        var activePrincipal = group.Where(e => e.Status == "Active").Sum(e => e.AmountLend);
        var paid = (await payments.ListForGroupAsync(groupId)).Sum(p => p.Amount);
        var remaining = activePrincipal - paid;
        if (req.Amount > remaining)
            throw ApiException.BadRequest(
                $"Partial return cannot exceed the outstanding ₹{remaining.ToString("N2")}");

        var record = new PartialReturn
        {
            GroupId = groupId,
            PaymentDate = req.Date,
            Amount = req.Amount,
            Notes = req.Notes,
            RecordedByUserId = userId,
        };
        await payments.AddAsync(record);
        await payments.SaveAsync(); // the save assigns record.Id — the ledger needs it
        // The cash ledger (§3.5) books this money coming back, one row.
        await cash.AddAsync(new CashLedger
        {
            LedgerDate = req.Date,
            Amount = req.Amount,
            Source = "PartialReturn",
            GroupId = groupId,
            ReferenceId = record.Id,
            Notes = record.Notes,
        });
        await cash.SaveAsync();
        return PaymentMapper.ToItem(record);
    }

    public async Task<PaymentItem> UpdateAsync(int id, UpdatePaymentRequest req)
    {
        var record = await payments.FindByIdAsync(id, tracked: true)
            ?? throw ApiException.NotFound("Payment not found");

        // The amount must keep the group fully covered: validate against
        // the outstanding with this payment's current value excluded.
        var newAmount = req.Amount ?? record.Amount;
        if (newAmount <= 0) throw ApiException.BadRequest("Amount must be greater than 0");
        var activePrincipal = (await entries.ListForGroupAsync(record.GroupId))
            .Where(e => e.Status == "Active").Sum(e => e.AmountLend);
        var others = (await payments.ListForGroupAsync(record.GroupId))
            .Where(p => p.Id != id).Sum(p => p.Amount);
        if (newAmount > activePrincipal - others)
            throw ApiException.BadRequest(
                $"Partial return cannot exceed the outstanding ₹{(activePrincipal - others).ToString("N2")}");

        if (req.Date is DateOnly d) record.PaymentDate = d;
        record.Amount = newAmount;
        if (req.Notes is not null) record.Notes = req.Notes;
        await payments.SaveAsync();
        // Keep the cash ledger row (§3.5) in step with the corrected payment.
        var ledgerRow = await cash.FindForSourceAsync("PartialReturn", id, tracked: true);
        if (ledgerRow is not null)
        {
            ledgerRow.LedgerDate = record.PaymentDate;
            ledgerRow.Amount = newAmount;
            ledgerRow.Notes = record.Notes;
            await cash.SaveAsync();
        }
        return PaymentMapper.ToItem(record);
    }
}