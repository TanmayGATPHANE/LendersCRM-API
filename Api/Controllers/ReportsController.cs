using AMS.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMS.Api.Controllers;

/// The Reports page's read-only endpoints (plan §3.6) — every row is
/// backend-computed here (§6 rule: calculations live with the API).
[ApiController]
[Route("api/reports")]
[Authorize] // read-only — ViewOnly accounts browse reports too
public class ReportsController : ControllerBase
{
    private readonly IReportsService _reports;

    public ReportsController(IReportsService reports) => _reports = reports;

    /// ?from=2026-01-01&to=2026-06-30 (inclusive; default all time) —
    /// one money row per ledger month inside the window.
    [HttpGet("summary")]
    public async Task<IActionResult> Summary([FromQuery] DateOnly? from, [FromQuery] DateOnly? to) =>
        Ok(await _reports.SummaryAsync(from, to));

    /// Every customer's track record across the lends made in the window.
    [HttpGet("customers")]
    public async Task<IActionResult> Customers([FromQuery] DateOnly? from, [FromQuery] DateOnly? to) =>
        Ok(await _reports.CustomersAsync(from, to));

    /// Every released collateral's outcome (releases dated in the window).
    [HttpGet("releases")]
    public async Task<IActionResult> Releases([FromQuery] DateOnly? from, [FromQuery] DateOnly? to) =>
        Ok(await _reports.ReleasesAsync(from, to));
}