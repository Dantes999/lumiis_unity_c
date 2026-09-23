using Microsoft.EntityFrameworkCore;

namespace Lumiis.Data;

public sealed class LumiisDbContext : DbContext
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Character> Characters => Set<Character>();
    public DbSet<CharacterInventoryItem> CharacterInventoryItems => Set<CharacterInventoryItem>();

    public LumiisDbContext(DbContextOptions<LumiisDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Account>(entity =>
        {
            entity.HasIndex(a => a.Username).IsUnique();
        });

        modelBuilder.Entity<Character>(entity =>
        {
            // Case-insensitiv UND global eindeutig (nicht pro Account) - wie bisher bei der In-Memory-
            // Namensvergabe des Servers (World.cs nutzte name.ToLowerInvariant() als Dictionary-Key). Ein
            // schlichter HasIndex(c => c.Name).IsUnique() wäre case-sensitiv (Postgres-Textvergleich ist
            // standardmäßig case-sensitiv) - deshalb stattdessen ein berechneter Kleinschreibungs-
            // Schattenwert mit Unique-Index darauf.
            entity.Property<string>("NameNormalized")
                .HasComputedColumnSql("lower(\"Name\")", stored: true);
            entity.HasIndex("NameNormalized").IsUnique();

            entity.HasOne(c => c.Account)
                .WithMany(a => a.Characters)
                .HasForeignKey(c => c.AccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CharacterInventoryItem>(entity =>
        {
            entity.HasIndex(i => new { i.CharacterId, i.SlotIndex }).IsUnique();

            entity.HasOne(i => i.Character)
                .WithMany(c => c.InventoryItems)
                .HasForeignKey(i => i.CharacterId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
