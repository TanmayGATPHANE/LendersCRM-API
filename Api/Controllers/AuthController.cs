using AMS.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMS.Api.Controllers;

/// Login — the only unauthenticated endpoint (plan §3.4/§6).
[ApiController]
public class AuthController(IAuthService auth) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("/api/auth/login")]
    public async Task<IActionResult> Login(LoginRequest req)
    {
        var (user, token) = await auth.LoginAsync(req.Email, req.Password);
        return Ok(new
        {
            token,
            user = new { id = user.Id, name = user.Name, email = user.Email, role = user.Role },
        });
    }
}