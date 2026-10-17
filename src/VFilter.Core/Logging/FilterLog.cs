using System.Collections.Concurrent;
using VFilter.Core.Status;

namespace VFilter.Core.Logging;

/// <summary>
/// Bounded in-memory buffer for filter_logs (spec 001, FR-014). Writing never blocks; when full, debug and
/// info events are dropped before warnings and above, and the drop count is reported later.
/// </summary>
public sealed class FilterLog(DatabaseClockOffset clock)
{
    public const int Capacity = 50_000;

    private const string FilterModule = "Filter";

    private readonly ConcurrentQueue<FilterLogEvent> _normal = new();
    private readonly ConcurrentQueue<FilterLogEvent> _important = new();
    private int _count;
    private long _dropped;

    /// <summary>Raised when enough events wait for an early flush.</summary>
    public event Action? BatchReady;

    public int Count => Volatile.Read(ref _count);

    public void Write(FilterLogLevel level, string category, string message, string module = FilterModule, string? ip = null, string? username = null, string? charname = null, string? opcode = null, string? dataJson = null) =>
        Write(new FilterLogEvent(clock.UtcNow(), level, module, category, message, ip, null, username, charname, opcode, dataJson));

    public void Write(FilterLogEvent logEvent)
    {
        if (Interlocked.Increment(ref _count) > Capacity && !DropOne(logEvent))
        {
            return;
        }

        (logEvent.IsImportant ? _important : _normal).Enqueue(logEvent);

        if (Count >= LogWriter.BatchSize)
        {
            BatchReady?.Invoke();
        }
    }

    /// <summary>Takes up to <paramref name="max"/> events, oldest first.</summary>
    public List<FilterLogEvent> Take(int max)
    {
        var batch = new List<FilterLogEvent>(Math.Min(max, Count));

        while (batch.Count < max && (_important.TryDequeue(out var e) || _normal.TryDequeue(out e)))
        {
            batch.Add(e);
            Interlocked.Decrement(ref _count);
        }

        batch.Sort((a, b) => a.Ts.CompareTo(b.Ts));

        return batch;
    }

    /// <summary>Puts a batch back after a failed write (oldest are lost first if that overflows).</summary>
    public void Return(IEnumerable<FilterLogEvent> events)
    {
        foreach (var logEvent in events)
        {
            Write(logEvent);
        }
    }

    public long TakeDroppedCount() => Interlocked.Exchange(ref _dropped, 0);

    /// <summary>Makes room for <paramref name="incoming"/>; false if the incoming event is the one dropped.</summary>
    private bool DropOne(FilterLogEvent incoming)
    {
        Interlocked.Increment(ref _dropped);

        if (_normal.TryDequeue(out _))
        {
            Interlocked.Decrement(ref _count);

            return true;
        }

        if (incoming.IsImportant && _important.TryDequeue(out _))
        {
            Interlocked.Decrement(ref _count);

            return true;
        }

        Interlocked.Decrement(ref _count);

        return false;
    }
}
