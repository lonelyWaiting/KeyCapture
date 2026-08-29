using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using KeyCapture.Data;

namespace KeyCapture.Services;

internal sealed class DataRetentionService : IDisposable
{
    private static readonly TimeSpan RetentionPeriod = TimeSpan.FromDays(30);
    private static readonly TimeSpan PurgeInterval = TimeSpan.FromHours(12);

    private readonly CancellationTokenSource _shutdown = new();
    private Task? _loopTask;

    /// <summary>
    /// Purges once at startup and then keeps purging while the app runs — the tray app is
    /// typically left running for days, so a startup-only purge let the database grow forever.
    /// </summary>
    public void Start()
    {
        _loopTask ??= Task.Run(async () =>
        {
            try
            {
                using var timer = new PeriodicTimer(PurgeInterval);
                do
                {
                    await PurgeOldDataAsync().ConfigureAwait(false);
                }
                while (await timer.WaitForNextTickAsync(_shutdown.Token).ConfigureAwait(false));
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
            {
                // Expected on shutdown.
            }
        });
    }

    /// <summary>
    /// Deletes records older than the retention period from both tables.
    /// </summary>
    public async Task PurgeOldDataAsync()
    {
        try
        {
            using var db = new AnalyticsDbContext();
            var cutoff = DateTime.UtcNow - RetentionPeriod;

            // ExecuteDelete issues a single DELETE statement instead of loading every
            // expired row into the change tracker first.
            int keyEvents = await db.KeyEvents.Where(e => e.Timestamp < cutoff).ExecuteDeleteAsync().ConfigureAwait(false);
            int sessions = await db.AppSessions.Where(s => s.LastActiveTime < cutoff).ExecuteDeleteAsync().ConfigureAwait(false);

            Debug.WriteLine($"[DataRetention] Purge completed: {keyEvents} key events, {sessions} sessions.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DataRetention] Purge failed: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        _shutdown.Dispose();
    }
}
