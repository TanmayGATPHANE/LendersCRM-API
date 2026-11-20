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
    public DbSet<ActivityLog> ActivityLog => Set<ActivityLog>();
    public DbSet<OutsourcedItem> OutsourcedItems => Set<OutsourcedItem>();
    public DbSet<DeletedEntry> EntriesDeleted => Set<DeletedEntry>();

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
            // §4.9 — Entries carries the deletion trigger; declaring it makes
            // EF emit OUTPUT INTO instead of bare OUTPUT (SQL Server refuses
            // bare OUTPUT on trigger-bearing tables).
            e.ToTable(tb => tb.HasTrigger("trg_Entries_MoveDeleted"));
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

        mb.Entity<ActivityLog>(e =>
        {
            e.Property(x => x.UserName).HasMaxLength(100);
            e.Property(x => x.Action).HasMaxLength(20);
            e.Property(x => x.Entity).HasMaxLength(20);
            e.Property(x => x.Details).HasMaxLength(500);
            // The Activity page reads newest-first; a group's history reads
            // by GroupId; a user's trail by UserId.
            e.HasIndex(x => x.AtUtc);
            e.HasIndex(x => x.GroupId);
            e.HasIndex(x => x.UserId);
        });

        mb.Entity<OutsourcedItem>(e =>
        {
            e.Property(x => x.OutsourcedTo).HasMaxLength(150);
            e.Property(x => x.Notes).HasMaxLength(500);
            // The Outsourced page lists the open rounds by date; a group's
            // in-and-out history reads by GroupId.
            e.HasIndex(x => x.GroupId);
            e.HasIndex(x => x.OutsourceDate);
        });

        mb.Entity<DeletedEntry>(e =>
        {
            // The archived copy of a deleted entry — same money precision
            // as Entries, the ORIGINAL entry id kept as the key, and the
            // deletion audit on top.
            e.ToTable("EntriesDeleted");
            e.Property(x => x.AmountLend).HasPrecision(12, 2);
            e.Property(x => x.InterestRate).HasPrecision(5, 2);
            e.Property(x => x.AmountReturned).HasPrecision(12, 2);
            e.Property(x => x.InterestCollected).HasPrecision(12, 2);
            e.Property(x => x.TotalCollected).HasPrecision(12, 2);
            e.Property(x => x.Status).HasMaxLength(10);
            e.Property(x => x.ItemDescription).HasMaxLength(500);
            e.Property(x => x.Notes).HasMaxLength(500);
            e.Property(x => x.DeleteReason).HasMaxLength(500);
            e.Property(x => x.Id).ValueGeneratedNever(); // THE original id, not a new one
            e.HasIndex(x => x.GroupId);
            e.HasIndex(x => x.DeletedAtUtc);
        });
    }
}