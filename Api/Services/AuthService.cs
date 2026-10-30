using AMS.Api.Repositories;
using AMS.Data.Entities;

namespace AMS.Api.Services;

public interface IAuthService
{
    Task<(User User, string Token)> LoginAsync(string email, string password);
}

/** Credential check against the Users table; on success the token service
    mints the JWT the frontend stores for every later request. */
public sealed class AuthService(IUserRepository users, ITokenService tokens) : IAuthService
{
    public async Task<(User User, string Token)> LoginAsync(string email, string password)
    {
        var user = await users.FindByEmailAsync((email ?? "").Trim().ToLower(), activeOnly: true);
        if (user is null || !BCrypt.Net.BCrypt.Verify(password ?? "", user.PasswordHash))
            throw ApiException.Unauthorized("Invalid email or password");
        return (user, tokens.Create(user));
    }
}