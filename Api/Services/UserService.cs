using AMS.Api.Repositories;
using AMS.Data.Entities;

namespace AMS.Api.Services;

public interface IUserService
{
    Task<List<UserItem>> ListAsync();
    Task<UserItem> CreateAsync(NewUserRequest req);
    Task<UserItem> UpdateAsync(int id, UpdateUserRequest req);
}

/** User management (Admin only) — plan §2/§6. */
public sealed class UserService(IUserRepository users) : IUserService
{
    public async Task<List<UserItem>> ListAsync()
    {
        var list = await users.ListAsync();
        return list.Select(ToItem).ToList();
    }

    public async Task<UserItem> CreateAsync(NewUserRequest req)
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
        return ToItem(user);
    }

    public async Task<UserItem> UpdateAsync(int id, UpdateUserRequest req)
    {
        var user = await users.FindByIdAsync(id)
            ?? throw ApiException.NotFound("User not found");

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
        return ToItem(user);
    }

    private static UserItem ToItem(User u) => new(u.Id, u.Name, u.Email, u.Role, u.IsActive);
}