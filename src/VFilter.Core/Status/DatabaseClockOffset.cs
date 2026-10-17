namespace VFilter.Core.Status;

/// <summary>
/// Database clock minus local clock, refreshed by every heartbeat, so log timestamps line up with the
/// website's database-clock view even when the machines' clocks drift.
/// </summary>
public sealed class DatabaseClockOffset(TimeProvider time)
{
    private long _offsetTicks;

    public TimeSpan Offset => TimeSpan.FromTicks(Interlocked.Read(ref _offsetTicks));

    public void Observe(DateTime databaseUtcNow) =>
        Interlocked.Exchange(ref _offsetTicks, (DateTime.SpecifyKind(databaseUtcNow, DateTimeKind.Utc) - time.GetUtcNow().UtcDateTime).Ticks);

    public DateTime UtcNow() => time.GetUtcNow().UtcDateTime + Offset;
}
