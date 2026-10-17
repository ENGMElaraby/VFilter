using Microsoft.Extensions.Logging.Abstractions;
using VFilter.Core.Logging;
using VFilter.Core.Sessions;
using VFilter.Core.Status;

namespace VFilter.Core.Tests;

public class SessionsAndLogsTests
{
    private sealed class RecordingSessions : ISessionRepository
    {
        public List<string> Calls { get; } = [];

        private long _id;

        public Task ClearAsync(CancellationToken cancellationToken) { Calls.Add("clear"); return Task.CompletedTask; }

        public Task<long> InsertAsync(LiveSession session, CancellationToken cancellationToken) { Calls.Add("insert " + session.Ip); return Task.FromResult(++_id); }

        public Task UpdateIdentityAsync(LiveSession session, CancellationToken cancellationToken) { Calls.Add($"identify {session.DatabaseId} {session.Username}/{session.Charname}"); return Task.CompletedTask; }

        public Task TouchAsync(long databaseId, CancellationToken cancellationToken) { Calls.Add($"touch {databaseId}"); return Task.CompletedTask; }

        public Task DeleteAsync(long databaseId, CancellationToken cancellationToken) { Calls.Add($"delete {databaseId}"); return Task.CompletedTask; }
    }

    private sealed class RecordingLogs : ILogRepository
    {
        public List<int> Batches { get; } = [];

        public bool Fail { get; set; }

        public List<FilterLogEvent> Written { get; } = [];

        public Task WriteAsync(IReadOnlyList<FilterLogEvent> events, CancellationToken cancellationToken)
        {
            if (Fail)
            {
                throw new InvalidOperationException("db down");
            }

            Batches.Add(events.Count);
            Written.AddRange(events);

            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Mirrors_session_changes_and_throttles_activity()
    {
        var h = new Harness();
        var repository = new RecordingSessions();
        var mirror = new SessionMirror(h.Sessions, repository, NullLogger<SessionMirror>.Instance);
        await mirror.ClearLeftoversAsync(CancellationToken.None);

        var session = h.Sessions.Open("Agent1", "10.0.0.5", _ => { });
        h.Sessions.Identify(session, "sara", "Knight", "HW1");
        h.Sessions.Touch(session);
        h.Time.Advance(TimeSpan.FromSeconds(31));
        h.Sessions.Touch(session);
        h.Sessions.Touch(session);
        h.Sessions.Close(session);

        while (h.Sessions.Changes.TryRead(out var change))
        {
            await mirror.ApplyAsync(change, CancellationToken.None);
        }

        Assert.Equal(["clear", "insert 10.0.0.5", "identify 1 sara/Knight", "touch 1", "delete 1"], repository.Calls);
        Assert.Equal(0, h.Sessions.Count);
    }

    [Fact]
    public async Task Writes_logs_in_batches_of_500()
    {
        var h = new Harness();
        var repository = new RecordingLogs();
        var writer = new LogWriter(h.Log, repository, NullLogger<LogWriter>.Instance);

        for (var i = 0; i < 1_200; i++)
        {
            h.Log.Write(FilterLogLevel.Info, "connection", $"event {i}");
        }

        await writer.FlushAsync(CancellationToken.None);

        Assert.Equal([500, 500, 200], repository.Batches);
        Assert.Equal("event 0", repository.Written[0].Message);
    }

    [Fact]
    public async Task Keeps_events_while_the_database_is_down_and_reports_drops()
    {
        var h = new Harness();
        var repository = new RecordingLogs { Fail = true };
        var writer = new LogWriter(h.Log, repository, NullLogger<LogWriter>.Instance);

        h.Log.Write(FilterLogLevel.Security, "exploit", "important");
        for (var i = 0; i < FilterLog.Capacity + 10; i++)
        {
            h.Log.Write(FilterLogLevel.Debug, "connection", "noise");
        }

        await writer.FlushAsync(CancellationToken.None);
        Assert.Equal(FilterLog.Capacity, h.Log.Count);

        repository.Fail = false;
        await writer.FlushAsync(CancellationToken.None);

        Assert.Contains(repository.Written, e => e.Message == "important");
        Assert.Contains(repository.Written, e => e.Message.EndsWith("log events were dropped while the database was unavailable", StringComparison.Ordinal));
    }

    [Fact]
    public void Log_times_follow_the_database_clock()
    {
        var h = new Harness();
        var clock = new DatabaseClockOffset(h.Time);
        clock.Observe(h.Time.GetUtcNow().UtcDateTime.AddSeconds(90));
        var log = new FilterLog(clock);

        log.Write(FilterLogLevel.Info, "connection", "x");

        Assert.Equal(h.Time.GetUtcNow().UtcDateTime.AddSeconds(90), log.Take(1).Single().Ts);
    }
}
