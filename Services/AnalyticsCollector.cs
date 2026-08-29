using System.Diagnostics;
using System.Threading.Channels;
using KeyCapture.Data;
using KeyCapture.Models;

namespace KeyCapture.Services;

/// <summary>
/// Buffers captured key events and writes them to SQLite in batches on a single
/// background writer.
///
/// Writing straight from the hook callback meant one thread-pool work item, one
/// DbContext and one SQLite transaction per keystroke, and concurrent writers could
/// race each other into duplicate session rows. Everything now funnels through a
/// bounded queue that is drained a couple of times per second.
/// </summary>
internal sealed class AnalyticsCollector : IDisposable
{
    private const int QueueCapacity = 8192;
    private const int MaxBatchSize = 512;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(2);

    private readonly Channel<KeyEventRecord> _queue = Channel.CreateBounded<KeyEventRecord>(
        new BoundedChannelOptions(QueueCapacity)
        {
            // Never block or grow without bound because of a slow disk: the newest events
            // are dropped instead, which is acceptable for usage statistics.
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true
        });

    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _writerTask;
    private int _droppedEvents;
    private bool _disposed;

    public AnalyticsCollector()
    {
        _writerTask = Task.Run(RunWriterAsync);
    }

    /// <summary>Number of events dropped because the queue was saturated.</summary>
    public int DroppedEvents => Volatile.Read(ref _droppedEvents);

    /// <summary>
    /// Queues a key event. Safe to call from the keyboard hook: it only allocates the
    /// record and performs a lock-free enqueue.
    /// </summary>
    public void RecordKeyEvent(int virtualKeyCode, string keyDisplayText, string modifiers, string appName, bool isCombo)
    {
        var record = new KeyEventRecord
        {
            Timestamp = DateTime.UtcNow,
            VirtualKeyCode = virtualKeyCode,
            KeyDisplayText = keyDisplayText,
            Modifiers = modifiers,
            ApplicationName = appName,
            IsCombo = isCombo
        };

        if (!_queue.Writer.TryWrite(record))
            Interlocked.Increment(ref _droppedEvents);
    }

    private async Task RunWriterAsync()
    {
        var reader = _queue.Reader;
        var batch = new List<KeyEventRecord>(MaxBatchSize);

        try
        {
            while (await reader.WaitToReadAsync(_shutdown.Token).ConfigureAwait(false))
            {
                // Coalesce whatever arrives during the interval into a single transaction.
                try
                {
                    await Task.Delay(FlushInterval, _shutdown.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Shutting down — fall through and persist what is already queued.
                }

                DrainInto(batch);
                Flush(batch);
                batch.Clear();

                if (_shutdown.IsCancellationRequested)
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AnalyticsCollector] Writer stopped: {ex}");
        }

        DrainInto(batch);
        Flush(batch);
    }

    private void DrainInto(List<KeyEventRecord> batch)
    {
        while (batch.Count < MaxBatchSize && _queue.Reader.TryRead(out var record))
            batch.Add(record);
    }

    private static void Flush(List<KeyEventRecord> batch)
    {
        if (batch.Count == 0)
            return;

        try
        {
            using var db = new AnalyticsDbContext();
            db.KeyEvents.AddRange(batch);
            UpdateSessions(db, batch);
            db.SaveChanges();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AnalyticsCollector] Flush of {batch.Count} events failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Rolls the batch up into one session row per application per day. Doing this from the
    /// single writer removes the lost-update race the previous per-keystroke upsert had.
    /// </summary>
    private static void UpdateSessions(AnalyticsDbContext db, List<KeyEventRecord> batch)
    {
        var countsByApp = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var record in batch)
            countsByApp[record.ApplicationName] = countsByApp.GetValueOrDefault(record.ApplicationName) + 1;

        var today = DateTime.UtcNow.Date;
        var appNames = countsByApp.Keys.ToArray();
        var existing = new Dictionary<string, AppSessionRecord>(StringComparer.Ordinal);
        foreach (var session in db.AppSessions.Where(s => s.StartTime >= today && appNames.Contains(s.ApplicationName)))
        {
            // Databases written by older builds can hold more than one row per app per day.
            existing.TryAdd(session.ApplicationName, session);
        }

        var now = DateTime.UtcNow;
        foreach (var (appName, count) in countsByApp)
        {
            if (existing.TryGetValue(appName, out var existingSession))
            {
                existingSession.LastActiveTime = now;
                existingSession.KeyCount += count;
            }
            else
            {
                db.AppSessions.Add(new AppSessionRecord
                {
                    ApplicationName = appName,
                    StartTime = now,
                    LastActiveTime = now,
                    KeyCount = count
                });
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _queue.Writer.TryComplete();
        _shutdown.Cancel();

        try
        {
            _writerTask.Wait(TimeSpan.FromSeconds(3));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AnalyticsCollector] Shutdown flush failed: {ex.Message}");
        }

        _shutdown.Dispose();
    }
}
