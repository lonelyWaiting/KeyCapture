using System.Collections.Concurrent;
using System.IO;
using Microsoft.EntityFrameworkCore;
using KeyCapture.Models;

namespace KeyCapture.Data;

public class AnalyticsDbContext : DbContext
{
    public DbSet<KeyEventRecord> KeyEvents => Set<KeyEventRecord>();
    public DbSet<AppSessionRecord> AppSessions => Set<AppSessionRecord>();

    // Building the options (and the provider's internal service provider lookup) is far more
    // expensive than the context itself, so they are built once per database path.
    private static readonly ConcurrentDictionary<string, DbContextOptions<AnalyticsDbContext>> OptionsCache = new();

    public AnalyticsDbContext() : base(GetOptions(ResolveDbPath()))
    {
    }

    private static string ResolveDbPath()
    {
        var folder = Environment.GetEnvironmentVariable("KEYCAPTURE_TEST_DB_PATH")
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "KeyCapture");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, "analytics.db");
    }

    private static DbContextOptions<AnalyticsDbContext> GetOptions(string dbPath) =>
        OptionsCache.GetOrAdd(dbPath, static path =>
            new DbContextOptionsBuilder<AnalyticsDbContext>()
                .UseSqlite($"Data Source={path}")
                .Options);

    /// <summary>
    /// Switches the database to write-ahead logging, which removes most of the fsync cost of
    /// the periodic analytics flushes and lets the dashboard read while a flush is in flight.
    /// The setting is stored in the database file, so this only needs to run at startup.
    /// </summary>
    public void EnableWriteAheadLogging()
        => Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<KeyEventRecord>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Timestamp);
            e.HasIndex(x => x.ApplicationName);
        });

        modelBuilder.Entity<AppSessionRecord>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ApplicationName);
            e.HasIndex(x => x.StartTime);
        });
    }
}
