using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace AMS.Api;

/** Reads the signed-in user's id from the JWT claims. */
public static class CurrentUser
{
    public static int? TryId(ClaimsPrincipal user)
    {
        var sub = user.FindFirstValue(JwtRegisteredClaimNames.Sub)
                  ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(sub, out var id) ? id : null;
    }
}