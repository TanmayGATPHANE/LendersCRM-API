using AMS.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AMS.Data;

/// The database model for AMS (notes/requirements-plan.md §5).
public class AmsDbContext : DbContext
{
    public AmsDbContext(DbContextOptions<AmsDbContext> options) : base(options) { }

    /// Every insert keeps the stored year glued to its date — the business
    /// reports/filter on EntryYear and it must never drift from EntryDate.
    public override int SaveChanges()
    {
        StampYears();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        StampYears();
        return base.SaveChangesAsync(ct);
    }

    private void StampYears()
    {
        foreach (var e in ChangeTracker.Entries<Data.Entities.Entry>()
                     .Where(x => x.State == EntityState.Added || x.State == EntityState.Modified))
            e.Entity.EntryYear = e.Entity.EntryDate.Year;
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Entry> Entries => Set<Entry>();
    public DbSet<PartialReturn> PartialReturns => Set<PartialReturn>();
    public DbSet<CashLedger> CashLedger => Set<CashLedger>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<User>(e =>
        {
            e.Property(u => u.Name).HasMaxLength(100);
            e.Property(u => u.Email).HasMaxLength(150);
            e.HasIndex(u => u.Email).IsUnique();
            e.Property(u => u.PasswordHash).HasMaxLength(500);
            e.Property(u => u.Role).HasMaxLength(20);

            // Seed the first admin so the very first login works (plan §9).
            // The hash is computed at migration-design time and embedded.
            e.HasData(new User
            {
                Id = 1,
                Name = "Admin",
                Email = AdminSeed.Email,
                PasswordHash = AdminSeed.PasswordHash,
                Role = "Admin",
                IsActive = true,
                CreatedAt = new DateTime(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc),
            });
        });

        mb.Entity<Customer>(e =>
        {
            e.Property(c => c.Name).HasMaxLength(150);
            e.Property(c => c.Phone).HasMaxLength(20);
            e.Property(c => c.Address).HasMaxLength(400);
            e.Property(c => c.Notes).HasMaxLength(500);
            e.HasIndex(c => c.Name);
        });

        mb.Entity<Entry>(e =>
        {
            e.Property(x => x.ItemDescription).HasMaxLength(500);
            e.Property(x => x.AmountLend).HasPrecision(12, 2);
            e.Property(x => x.InterestRate).HasPrecision(5, 2);
            e.Property(x => x.AmountReturned).HasPrecision(12, 2);
            e.Property(x => x.InterestCollected).HasPrecision(12, 2);
            e.Property(x => x.TotalCollected).HasPrecision(12, 2);
            e.Property(x => x.Status).HasMaxLength(10);
            e.Property(x => x.Notes).HasMaxLength(500);
            e.HasIndex(x => x.GroupId);
            e.HasIndex(x => x.CustomerId);
            e.HasIndex(x => x.EntryYear); // the stored lending year (reports/filters)
            e.HasIndex(x => new { x.Status, x.EntryDate });
        });

        mb.Entity<PartialReturn>(e =>
        {
            e.Property(p => p.Amount).HasPrecision(12, 2);
            e.Property(p => p.Notes).HasMaxLength(500);
            e.HasIndex(p => p.GroupId);
        });

        mb.Entity<CashLedger>(e =>
        {
            // Signed for Manual rows (+ in / − out); lending rows are +
            e.Property(x => x.Amount).HasPrecision(12, 2);
            e.Property(x => x.Source).HasMaxLength(30);
            e.Property(x => x.Notes).HasMaxLength(500);
            e.HasIndex(x => x.LedgerDate);
            e.HasIndex(x => x.Source);
            e.HasIndex(x => x.ReferenceId);
        });
    }
}