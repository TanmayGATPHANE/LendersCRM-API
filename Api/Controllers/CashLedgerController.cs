using AMS.Api;
using AMS.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMS.Api.Controllers;

/// The Total Cash Available SQL table (plan §3.5). GET serves the Cash
/// page; the lending rows are posted automatically by the partial-return
/// and release endpoints; the manual ± (outside the business) rows are
/// the only ones added and deleted by hand here.
[ApiController]
[Authorize]
public class CashLedgerController : ControllerBase
{
    private readonly ICashLedgerService _cash;

    public CashLedgerController(ICashLedgerService cash) => _cash = cash;

    [HttpGet("/api/cash-ledger")]
    public async Task<IActionResult> List() => Ok(await _cash.ListAsync());

    [HttpPost("/api/cash-ledger")]
    [Authorize(Policy = "Writer")]
    public async Task<IActionResult> AddManual(NewManualCashRequest req) =>
        Ok(await _cash.AddManualAsync(req, CurrentUser.TryId(User)));

    [HttpDelete("/api/cash-ledger/{id}")]
    [Authorize(Policy = "Writer")]
    public async Task<IActionResult> Delete(int id)
    {
        await _cash.DeleteAsync(id);
        return NoContent();
    }
}