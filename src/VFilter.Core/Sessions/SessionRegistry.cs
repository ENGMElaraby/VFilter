using System.Collections.Concurrent;
using System.Threading.Channels;

namespace VFilter.Core.Sessions;

/// <summary>
/// Live sessions, fed by the proxy. Changes are queued for <see cref="SessionMirror"/> and never touch
/// the database on the traffic path (spec 001, FR-013/FR-017).
/// </summary>
public sealed class SessionRegistry(TimeProvider time)
{
    public static readonly TimeSpan TouchInterval = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<long, LiveSession> _sessions = new();
    private readonly Channel<SessionChange> _changes = Channel.CreateUnbounded<SessionChange>(new UnboundedChannelOptions { SingleReader = true });
    private long _nextId;

    public ChannelReader<SessionChange> Changes => _changes.Reader;

    public int Count => _sessions.Count;

    public LiveSession Open(string module, string ip, Action<string> disconnect)
    {
        var session = new LiveSession(Interlocked.Increment(ref _nextId), module, ip, disconnect) { LastTouchReported = time.GetTimestamp() };
        _sessions[session.LocalId] = session;
        _changes.Writer.TryWrite(new SessionChange(SessionChangeKind.Opened, session));

        return session;
    }

    public void Identify(LiveSession session, string? username, string? charname, string? hwid)
    {
        session.Username = username ?? session.Username;
        session.Charname = charname ?? session.Charname;
        session.Hwid = hwid ?? session.Hwid;
        _changes.Writer.TryWrite(new SessionChange(SessionChangeKind.Identified, session));
    }

    /// <summary>Called on traffic; reports activity at most every 30 s per session.</summary>
    public void Touch(LiveSession session)
    {
        var now = time.GetTimestamp();

        if (time.GetElapsedTime(session.LastTouchReported, now) < TouchInterval)
        {
            return;
        }

        session.LastTouchReported = now;
        _changes.Writer.TryWrite(new SessionChange(SessionChangeKind.Touched, session));
    }

    public void Close(LiveSession session)
    {
        if (_sessions.TryRemove(session.LocalId, out _))
        {
            _changes.Writer.TryWrite(new SessionChange(SessionChangeKind.Closed, session));
        }
    }

    /// <summary>Disconnects every session matching the character, username or IP; returns how many.</summary>
    public int Kick(string target, string reason)
    {
        var matches = _sessions.Values.Where(s => s.Matches(target)).ToList();

        foreach (var session in matches)
        {
            session.Disconnect(reason);
        }

        return matches.Count;
    }
}
