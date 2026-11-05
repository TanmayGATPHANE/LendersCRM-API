using AMS.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMS.Api.Controllers;

/// User management — Admin role required on every action (plan §2/§6).
[ApiController]
[Authorize(Roles = "Admin")]
public class UsersController : ControllerBase
{
    private readonly IUserService _users;

    public UsersController(IUserService users) => _users = users;

    [HttpGet("/api/users")]
    public async Task<IActionResult> List() => Ok(await _users.ListAsync());

    [HttpPost("/api/users")]
    public async Task<IActionResult> Create(NewUserRequest req) =>
        Ok(await _users.CreateAsync(req, CurrentUser.Actor(User)));

    [HttpPut("/api/users/{id}")]
    public async Task<IActionResult> Update(int id, UpdateUserRequest req) =>
        Ok(await _users.UpdateAsync(id, req, CurrentUser.Actor(User)));
}