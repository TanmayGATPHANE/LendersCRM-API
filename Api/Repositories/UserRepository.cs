using AMS.Data;
using AMS.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AMS.Api.Repositories;

public interface IUserRepository
{
    Task<List<User>> ListAsync();
    Task<User?> FindByEmailAsync(string email, bool activeOnly);
    Task<User?> FindByIdAsync(int id);
    Task<bool> EmailExistsAsync(string email);
    Task AddAsync(User user);
    Task<int> SaveAsync();
}

public sealed class UserRepository(AmsDbContext db) : IUserRepository
{
    public Task<List<User>> ListAsync() =>
        db.Users.AsNoTracking().OrderBy(u => u.Id).ToListAsync();

    public Task<User?> FindByEmailAsync(string email, bool activeOnly) =>
        activeOnly
            ? db.Users.FirstOrDefaultAsync(u => u.Email == email && u.IsActive)
            : db.Users.FirstOrDefaultAsync(u => u.Email == email);

    public Task<User?> FindByIdAsync(int id) =>
        db.Users.FirstOrDefaultAsync(u => u.Id == id);

    public Task<bool> EmailExistsAsync(string email) =>
        db.Users.AnyAsync(u => u.Email == email);

    public Task AddAsync(User user)
    {
        db.Users.Add(user);
        return Task.CompletedTask;
    }

    public Task<int> SaveAsync() => db.SaveChangesAsync();
}