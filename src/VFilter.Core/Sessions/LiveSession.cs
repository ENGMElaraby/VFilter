namespace VFilter.Core.Sessions;

/// <summary>One live client connection as the filter knows it so far.</summary>
public sealed class LiveSession(long localId, string module, string ip, Action<string> disconnect)
{
    public long LocalId { get; } = localId;

    public string Module { get; } = module;

    public string Ip { get; } = ip;

    public string? Hwid { get; internal set; }

    public string? Username { get; internal set; }

    public string? Charname { get; internal set; }

    /// <summary>Row id in filter_sessions, once inserted.</summary>
    public long? DatabaseId { get; internal set; }

    internal long LastTouchReported { get; set; }

    internal Action<string> Disconnect { get; } = disconnect;

    /// <summary>Whether a kick/ban target (character, username or IP) names this session.</summary>
    public bool Matches(string target) =>
        string.Equals(Charname, target, StringComparison.OrdinalIgnoreCase)
        || string.Equals(Username, target, StringComparison.OrdinalIgnoreCase)
        || string.Equals(Ip, target, StringComparison.OrdinalIgnoreCase);
}

public enum SessionChangeKind
{
    Opened,
    Identified,
    Touched,
    Closed,
}

public sealed record SessionChange(SessionChangeKind Kind, LiveSession Session);
