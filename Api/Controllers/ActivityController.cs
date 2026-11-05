using AMS.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMS.Api.Controllers;

/// The audit trail (§10) — Admin only. The Activity tab reads the whole
/// log (optionally one user's trail); a groupId filter powers the
/// per-collateral history panel inside the expanded table rows.
[ApiController]
[Route("api/activity")]
[Authorize(Roles = "Admin")]
public class ActivityController : ControllerBase
{
    private readonly IActivityService _activity;

    public ActivityController(IActivityService activity) => _activity = activity;

    /// GET /api/activity?userId=&groupId=&from=&to= — newest first.
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int? userId, [FromQuery] int? groupId) =>
        Ok(await _activity.ListAsync(userId, groupId));
}