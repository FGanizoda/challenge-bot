using ChallengeBot.Logic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ChallengeBot.Data;

/// <summary>База данных челленджа (SQLite, один файл challenge.db).</summary>
public class ChallengeDbContext : DbContext
{
    public ChallengeDbContext(DbContextOptions<ChallengeDbContext> options) : base(options) { }

    public DbSet<Participant> Participants => Set<Participant>();
    public DbSet<DayRecord> Days => Set<DayRecord>();
    public DbSet<Setting> Settings => Set<Setting>();
    public DbSet<EventLog> Events => Set<EventLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Participant>(e =>
        {
            e.ToTable("participants");
            e.HasKey(p => p.UserId);
            // Id пользователя Telegram задаётся нами, а не базой.
            e.Property(p => p.UserId).ValueGeneratedNever();
        });

        // В базе статус хранится словом: done, gentle, sick, miss.
        var statusConverter = new ValueConverter<DayStatus, string>(
            status => status.ToString().ToLowerInvariant(),
            text => Enum.Parse<DayStatus>(text, true));

        modelBuilder.Entity<DayRecord>(e =>
        {
            e.ToTable("days");
            e.HasKey(d => new { d.UserId, d.Date });
            e.Property(d => d.Status).HasConversion(statusConverter);
        });

        modelBuilder.Entity<Setting>(e =>
        {
            e.ToTable("settings");
            e.HasKey(s => s.Key);
        });

        modelBuilder.Entity<EventLog>(e =>
        {
            e.ToTable("events");
            e.HasKey(ev => ev.Id);
        });
    }
}
