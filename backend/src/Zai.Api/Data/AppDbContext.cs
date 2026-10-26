using Microsoft.EntityFrameworkCore;
using Zai.Api.Domain;

namespace Zai.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Admin> Admins => Set<Admin>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<Series> Series => Set<Series>();
    public DbSet<Sensor> Sensors => Set<Sensor>();
    public DbSet<Measurement> Measurements => Set<Measurement>();
    public DbSet<EventLogEntry> EventLog => Set<EventLogEntry>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Admin>(e =>
        {
            e.ToTable("admins");
            e.Property(a => a.Username).HasMaxLength(50);
            e.HasIndex(a => a.Username).IsUnique();
        });

        model.Entity<Session>(e =>
        {
            e.ToTable("sessions");
            e.Property(s => s.TokenHash).HasMaxLength(64).IsFixedLength();
            e.HasIndex(s => s.TokenHash).IsUnique();
            e.HasIndex(s => s.ExpiresAt);
            e.HasOne(s => s.Admin).WithMany().HasForeignKey(s => s.AdminId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<Series>(e =>
        {
            e.ToTable("series", t =>
            {
                t.HasCheckConstraint("ck_series_range", "min_value < max_value");
                t.HasCheckConstraint("ck_series_color", "color ~ '^#[0-9A-Fa-f]{6}$'");
                t.HasCheckConstraint("ck_series_name", "char_length(btrim(name)) > 0");
            });
            e.Property(s => s.Name).HasMaxLength(100);
            e.Property(s => s.Color).HasMaxLength(7).IsFixedLength();
            e.Property(s => s.Icon).HasMaxLength(30);
            e.Property(s => s.Unit).HasMaxLength(20);
        });

        model.Entity<Sensor>(e =>
        {
            e.ToTable("sensors");
            e.Property(s => s.Name).HasMaxLength(100);
            e.Property(s => s.ApiKeyHash).HasMaxLength(64).IsFixedLength();
            e.HasIndex(s => s.ApiKeyHash).IsUnique();
            e.HasOne(s => s.Series).WithMany().HasForeignKey(s => s.SeriesId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<Measurement>(e =>
        {
            e.ToTable("measurements");
            e.HasKey(m => new { m.Id, m.Timestamp });
            e.Property(m => m.Id).UseIdentityAlwaysColumn();
            e.Property(m => m.Timestamp).HasColumnName("ts");
            e.HasIndex(m => m.Id);
            e.HasIndex(m => new { m.SeriesId, m.Timestamp });
            e.HasIndex(m => m.Timestamp);
            e.HasOne<Series>().WithMany().HasForeignKey(m => m.SeriesId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Sensor>().WithMany().HasForeignKey(m => m.SensorId).OnDelete(DeleteBehavior.SetNull);
        });

        model.Entity<EventLogEntry>(e =>
        {
            e.ToTable("event_log");
            e.Property(l => l.Level).HasMaxLength(10);
            e.Property(l => l.Kind).HasMaxLength(40);
            e.Property(l => l.Payload).HasColumnType("jsonb");
            e.HasIndex(l => l.OccurredAt).IsDescending();
        });
    }
}
