using AMS.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMS.Api.Controllers;

/// Partial returns: list all, correct one (plan §4.6). Recording one —
/// the group-scoped POST — lives in GroupsController (§6 routes).
[ApiController]
[Authorize]
public class PaymentsController : ControllerBase
{
    private readonly IPaymentService _payments;

    public PaymentsController(IPaymentService payments) => _payments = payments;

    [HttpGet("/api/payments")]
    public async Task<IActionResult> List() => Ok(await _payments.ListAsync());

    [HttpPut("/api/payments/{id}")]
    [Authorize(Policy = "Writer")]
    public async Task<IActionResult> Update(int id, UpdatePaymentRequest req) =>
        Ok(await _payments.UpdateAsync(id, req));
}