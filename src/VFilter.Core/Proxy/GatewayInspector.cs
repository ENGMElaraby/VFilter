using VFilter.Core.Config;
using VFilter.Core.Logging;
using VFilter.Core.Modules;
using VFilter.Security;

namespace VFilter.Core.Proxy;

/// <summary>
/// Gateway logins (spec 002 US1/US2): applies the gateway mode to 0x6102, takes the username, and rewrites the
/// 0xA102 success answer so the client is sent to the Agent's guard address, never the real one.
/// </summary>
public sealed class GatewayInspector(ConfigStore config, GatewayModeState gatewayMode) : IPacketInspector
{
    public const string AllowedIpsList = "allowed_ips";
    public const string GmRefusalCodeKey = "gateway.gm_refusal_code";
    public const string InspectionRefusalCodeKey = "gateway.inspection_refusal_code";

    /// <summary>"Server unavailable/full" until the client's messages for GM/inspection are confirmed.</summary>
    public const int DefaultRefusalCode = 6;

    public bool AppliesTo(ModuleType type) => type == ModuleType.Gateway;

    public Inspection Inspect(ProxySession session, PacketDirection direction, Packet packet)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(packet);

        return (direction, packet.Opcode) switch
        {
            (PacketDirection.ClientToModule, Opcodes.GatewayLoginRequest) => OnLoginRequest(session, packet),
            (PacketDirection.ModuleToClient, Opcodes.GatewayLoginResponse) => OnLoginResponse(session, packet),
            _ => Inspection.Pass(packet),
        };
    }

    private Inspection OnLoginRequest(ProxySession session, Packet packet)
    {
        string username;

        try
        {
            var reader = packet.OpenReader();
            reader.ReadByte();
            username = reader.ReadAscii();
        }
        catch (EndOfPacketException)
        {
            return Inspection.Close("security: malformed login request");
        }

        session.Registry.Identify(session.Live, username, charname: null, hwid: null);
        var mode = gatewayMode.Mode;

        if (mode == GatewayMode.Public || IpMatcher.AnyContains(config.Lists.Get(AllowedIpsList).Select(e => e.Value), session.ClientAddress))
        {
            return Inspection.Pass(packet);
        }

        var key = mode == GatewayMode.Gm ? GmRefusalCodeKey : InspectionRefusalCodeKey;
        var code = (byte)Math.Clamp(config.Settings.GetInt(key, DefaultRefusalCode), 1, 255);
        session.Log(FilterLogLevel.Info, "login", $"login refused: gateway in {GatewayModeState.Key(mode)} mode");

        var reply = new PacketWriter();
        reply.WriteByte(Opcodes.LoginFailed);
        reply.WriteByte(code);

        return Inspection.Answer(reply.ToPacket(Opcodes.GatewayLoginResponse, packet.Encrypted));
    }

    private Inspection OnLoginResponse(ProxySession session, Packet packet)
    {
        try
        {
            var reader = packet.OpenReader();

            if (reader.ReadByte() != Opcodes.LoginSucceeded)
            {
                return Inspection.Pass(packet);
            }

            var token = reader.ReadUInt32();
            var ip = reader.ReadAscii();
            var port = reader.ReadUInt16();
            var rest = reader.ReadBytes(reader.Remaining);

            var agent = config.Modules.Modules.FirstOrDefault(m =>
                m.Type == ModuleType.Agent
                && string.Equals(m.ModuleIp, ip, StringComparison.OrdinalIgnoreCase)
                && m.ModulePort == port);

            if (agent is null)
            {
                // Never forward an address we cannot hide: it would reveal a real server.
                return Inspection.Close("security: login answer names an unknown agent address");
            }

            var rewritten = new PacketWriter();
            rewritten.WriteByte(Opcodes.LoginSucceeded);
            rewritten.WriteUInt32(token);
            rewritten.WriteAscii(agent.GuardIp);
            rewritten.WriteUInt16((ushort)agent.GuardPort);
            rewritten.WriteBytes(rest);

            return Inspection.Replace(rewritten.ToPacket(packet.Opcode, packet.Encrypted, packet.Massive));
        }
        catch (EndOfPacketException)
        {
            return Inspection.Close("module protocol error: malformed login answer");
        }
    }
}
