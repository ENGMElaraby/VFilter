using VFilter.Core.Config;

namespace VFilter.Core.Modules;

public enum GatewayMode
{
    Gm,
    Inspection,
    Public,
}

/// <summary>
/// The gateway mode new logins see. Starts in GM, or Inspection when server.start_in_inspection
/// is set (spec 001, FR-011). Changed only by commands; reported by the heartbeat.
/// </summary>
public sealed class GatewayModeState
{
    public const string StartInInspectionKey = "server.start_in_inspection";

    private int _mode = (int)GatewayMode.Gm;

    public GatewayMode Mode => (GatewayMode)Volatile.Read(ref _mode);

    public void Initialize(SettingsSnapshot settings) =>
        Set(settings.GetBool(StartInInspectionKey, fallback: false) ? GatewayMode.Inspection : GatewayMode.Gm);

    public void Set(GatewayMode mode) => Volatile.Write(ref _mode, (int)mode);

    public static string Key(GatewayMode mode) => mode switch
    {
        GatewayMode.Gm => "gm",
        GatewayMode.Inspection => "inspection",
        GatewayMode.Public => "public",
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    public static bool TryParse(string? value, out GatewayMode mode)
    {
        switch (value)
        {
            case "gm": mode = GatewayMode.Gm; return true;
            case "inspection": mode = GatewayMode.Inspection; return true;
            case "public": mode = GatewayMode.Public; return true;
            default: mode = default; return false;
        }
    }
}
