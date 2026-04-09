using Microsoft.EntityFrameworkCore;
using DarahOcr.Models;

namespace DarahOcr.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<OcrResult> OcrResults => Set<OcrResult>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // User
        modelBuilder.Entity<User>(e =>
        {
            e.HasIndex(u => u.Username).IsUnique();
            e.Property(u => u.Permissions)
             .HasColumnType("text[]")
             .HasDefaultValueSql("ARRAY[]::text[]");
        });

        // Job
        modelBuilder.Entity<Job>(e =>
        {
            e.HasOne(j => j.User)
             .WithMany(u => u.Jobs)
             .HasForeignKey(j => j.UserId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(j => j.OcrResult)
             .WithOne(r => r.Job)
             .HasForeignKey<OcrResult>(r => r.JobId)
             .OnDelete(DeleteBehavior.Cascade);

            e.Property(j => j.UpdatedAt)
             .HasDefaultValueSql("NOW()");
        });

        // AuditLog
        modelBuilder.Entity<AuditLog>(e =>
        {
            e.HasOne(a => a.User)
             .WithMany(u => u.AuditLogs)
             .HasForeignKey(a => a.UserId)
             .OnDelete(DeleteBehavior.SetNull)
             .IsRequired(false);
        });

        // ApiKey
        modelBuilder.Entity<ApiKey>(e =>
        {
            e.HasOne(k => k.User)
             .WithMany(u => u.ApiKeys)
             .HasForeignKey(k => k.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
