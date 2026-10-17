namespace VFilter.Core.Proxy;

/// <summary>Proxy limits (spec 002, FR-013). Configurable under "VFilter:Proxy" for tests and tuning.</summary>
public sealed class ProxyOptions
{
    public const string Section = "VFilter:Proxy";

    public TimeSpan HandshakeTimeout { get; set; } = TimeSpan.FromSeconds(10);

    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(5);

    public TimeSpan ModuleConnectTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan WriteTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public int MaxPendingBytes { get; set; } = 4 * 1024 * 1024;
}
