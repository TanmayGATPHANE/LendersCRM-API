using AMS.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMS.Api.Controllers;

/// The dashboard's three headline numbers (plan §3.1, §6).
[ApiController]
[Authorize]
public class InsightsController : ControllerBase
{
    private readonly IInsightsService _insights;

    public InsightsController(IInsightsService insights) => _insights = insights;

    /// ?tab=all (default) | active | released — the tab shapes the tiles;
    /// ?from=&to= (the dashboard's month/year window) slices every tile by
    /// the LEND date.
    [HttpGet("/api/insights")]
    public async Task<IActionResult> Get(
        [FromQuery] string? tab, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to) =>
        Ok(await _insights.GetAsync(tab, from, to));
}