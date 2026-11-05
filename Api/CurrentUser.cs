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

    /// The actor of a write — id plus display name (the "name" claim the
    /// login token carries). The NAME is snapshotted into every ActivityLog
    /// row, so the trail keeps reading right even after a user is renamed.
    public static Actor Actor(ClaimsPrincipal user) => new(
        TryId(user),
        user.FindFirstValue("name") ?? "");

    public static string? TryName(ClaimsPrincipal user) =>
        user.FindFirstValue("name");
}

/// Who performed an action (id + name snapshot). Id may be null if a
/// request ever slips through unauthenticated.
public sealed record Actor(int? Id, string Name)
{
    public static readonly Actor Unknown = new(null, "");
}