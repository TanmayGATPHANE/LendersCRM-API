using AMS.Api;
using AMS.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMS.Api.Controllers;

/// Whole-collateral operations (plan §4.2, §4.3, §6): inspect a group,
/// record a partial return against it, move it to another customer,
/// release it.
[ApiController]
[Route("api/groups")]
[Authorize]
public class GroupsController : ControllerBase
{
    private readonly IGroupService _groups;
    private readonly IPaymentService _payments;

    public GroupsController(IGroupService groups, IPaymentService payments)
    {
        _groups = groups;
        _payments = payments;
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(int id) => Ok(await _groups.GetAsync(id));

    /// Partial return against a whole collateral group (§4.6).
    [HttpPost("{id}/payments")]
    [Authorize(Policy = "Writer")]
    public async Task<IActionResult> AddPayment(int id, NewPaymentRequest req) => Ok(
        await _payments.AddToGroupAsync(id, req, CurrentUser.TryId(User)));

    /// Move the whole group — lends + payments — to another customer.
    [HttpPut("{id}/customer")]
    [Authorize(Policy = "Writer")]
    public async Task<IActionResult> MoveCustomer(int id, MoveGroupRequest req)
    {
        await _groups.MoveCustomerAsync(id, req.CustomerId);
        return Ok(new { groupId = id, customerId = req.CustomerId });
    }

    /// Release the whole group (§4.2): freezes interest, stores
    /// TotalCollected. 409 if already released.
    [HttpPost("{id}/release")]
    [Authorize(Policy = "Writer")]
    public async Task<IActionResult> Release(int id, ReleaseRequest req) =>
        Ok(await _groups.ReleaseAsync(id, req));
}