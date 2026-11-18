using AMS.Api;
using AMS.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMS.Api.Controllers;

/// Outsourcing (§4.8) — collaterals sitting with an outside party. The
/// list is visible to every signed-in role (read-only views may browse
/// it); going out / coming back are Writer actions, like every change.
[ApiController]
[Authorize]
public class OutsourceController(IOutsourceService outsourced) : ControllerBase
{
    /// The Outsourced page — one row per OPEN round (currently out).
    [HttpGet("api/outsourced")]
    public async Task<IActionResult> List() => Ok(await outsourced.ListAsync());

    [HttpPost("api/groups/{id}/outsource")]
    [Authorize(Policy = "Writer")]
    public async Task<IActionResult> Outsource(int id, NewOutsourceRequest req)
    {
        await outsourced.OutsourceAsync(id, req, CurrentUser.Actor(User));
        return Created($"/outsourced", new { groupId = id });
    }

    [HttpPost("api/groups/{id}/outsource/return")]
    [Authorize(Policy = "Writer")]
    public async Task<IActionResult> Return(int id, OutsourceReturnRequest req)
    {
        await outsourced.ReturnAsync(id, req, CurrentUser.Actor(User));
        return Ok(new { groupId = id });
    }
}
