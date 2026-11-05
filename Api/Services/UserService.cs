using AMS.Api.Repositories;
using AMS.Data.Entities;

namespace AMS.Api.Services;

public interface IUserService
{
    Task<List<UserItem>> ListAsync();
    Task<UserItem> CreateAsync(NewUserRequest req, Actor actor);
    Task<UserItem> UpdateAsync(int id, UpdateUserRequest req, Actor actor);
}

/** User management (Admin only) — plan §2/§6. */
public sealed class UserService(IUserRepository users, IActivityRepository activity) : IUserService
{
    public async Task<List<UserItem>> ListAsync()
    {
        var list = await users.ListAsync();
        return list.Select(ToItem).ToList();
    }

    public async Task<UserItem> CreateAsync(NewUserRequest req, Actor actor)
    {
        var email = (req.Email ?? "").Trim().ToLower();
        if (string.IsNullOrWhiteSpace(req.Name)) throw ApiException.BadRequest("Name is required");
        if (string.IsNullOrWhiteSpace(email)) throw ApiException.BadRequest("Email is required");
        if (string.IsNullOrWhiteSpace(req.Password)) throw ApiException.BadRequest("Password is required");
        if (req.Role is not ("Admin" or "Lender" or "ViewOnly"))
            throw ApiException.BadRequest("Role must be Admin, Lender or ViewOnly");
        if (await users.EmailExistsAsync(email))
            throw ApiException.Conflict("A user with this email already exists");

        var user = new User
        {
            Name = req.Name.Trim(),
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
            Role = req.Role,
            IsActive = true,
        };
        await users.AddAsync(user);
        await users.SaveAsync();

        // The audit row: which admin created which account.
        await activity.AddAsync(actor, "Created", "User", user.Id, groupId: null,
            $"Created user {user.Name} ({user.Email}) as {user.Role}");
        await activity.SaveAsync();
        return ToItem(user);
    }

    public async Task<UserItem> UpdateAsync(int id, UpdateUserRequest req, Actor actor)
    {
        var user = await users.FindByIdAsync(id)
            ?? throw ApiException.NotFound("User not found");

        // The audit diff — old values before anything mutates.
        var oldRole = user.Role;
        var oldActive = user.IsActive;
        var oldName = user.Name;

        if (req.Name is not null) user.Name = req.Name.Trim();
        if (req.Role is not null)
        {
            if (req.Role is not ("Admin" or "Lender" or "ViewOnly"))
                throw ApiException.BadRequest("Role must be Admin, Lender or ViewOnly");
            user.Role = req.Role;
        }
        if (req.IsActive.HasValue) user.IsActive = req.IsActive.Value;
        if (!string.IsNullOrWhiteSpace(req.Password))
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password);
        await users.SaveAsync();

        var changes = new List<string>();
        if (user.Role != oldRole) changes.Add($"role {oldRole} → {user.Role}");
        if (user.IsActive != oldActive)
            changes.Add(user.IsActive ? "re-activated" : "deactivated");
        if (user.Name != oldName) changes.Add($"name {oldName} → {user.Name}");
        if (!string.IsNullOrWhiteSpace(req.Password)) changes.Add("password reset");

        // The audit row: which admin changed which account (never the
        // password itself, obviously).
        await activity.AddAsync(actor, "Updated", "User", id, groupId: null,
            $"Updated user {user.Name} — {string.Join("; ", changes)}");
        await activity.SaveAsync();
        return ToItem(user);
    }

    private static UserItem ToItem(User u) => new(u.Id, u.Name, u.Email, u.Role, u.IsActive);
}