using AMS.Api;
using AMS.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMS.Api.Controllers;

/// Lend entries (plan §4.1, §4.3, §4.5, §6). Business rules live in
/// EntryService; this layer only routes, authorizes and shapes responses.
[ApiController]
[Route("api/entries")]
[Authorize]
public class EntriesController : ControllerBase
{
    private readonly IEntryService _entries;

    public EntriesController(IEntryService entries) => _entries = entries;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] string? search) =>
        Ok(await _entries.ListAsync(status, search));

    [HttpPost]
    [Authorize(Policy = "Writer")]
    public async Task<IActionResult> Add(NewEntryRequest req)
    {
        var (id, groupId) = await _entries.AddAsync(req, CurrentUser.Actor(User));
        return Created($"/api/entries/{id}", new { id, groupId });
    }

    [HttpPut("{id}")]
    [Authorize(Policy = "Writer")]
    public async Task<IActionResult> Update(int id, UpdateEntryRequest req)
    {
        var (_, groupId) = await _entries.UpdateAsync(id, req, CurrentUser.Actor(User));
        return Ok(new { id, groupId });
    }
}