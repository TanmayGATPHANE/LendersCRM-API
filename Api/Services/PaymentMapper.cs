using AMS.Data.Entities;

namespace AMS.Api.Services;

/** PartialReturn entity → PaymentItem response (Contracts). */
public static class PaymentMapper
{
    public static PaymentItem ToItem(PartialReturn p) => new(p.Id, p.GroupId, p.PaymentDate, p.Amount, p.Notes);
}