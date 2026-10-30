using AMS.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMS.Api.Controllers;

/// The header's global search (plan §3.1): one endpoint fanning out across
/// customers, users and transactions. Users surface only for Admin callers —
/// Lender and ViewOnly never see accounts.
[ApiController]
[Authorize]
public class SearchController : ControllerBase
{
    private readonly ISearchService _search;

    public SearchController(ISearchService search) => _search = search;

    [HttpGet("/api/search")]
    public async Task<IActionResult> Get([FromQuery] string? q) =>
        Ok(await _search.SearchAsync(q ?? "", User.IsInRole("Admin")));
}