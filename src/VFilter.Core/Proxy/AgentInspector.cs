using VFilter.Core.Modules;
using VFilter.Security;

namespace VFilter.Core.Proxy;

/// <summary>
/// Who is on an Agent session (spec 002 US3). The client's requests (0x6103 username, 0x7001 character) are only
/// remembered; the registry is updated when the Agent's answer (0xA103, 0xB001) reports success, so a client cannot
/// label its session with someone else's name.
/// </summary>
public sealed class AgentInspector : IPacketInspector
{
    public bool AppliesTo(ModuleType type) => type == ModuleType.Agent;

    public Inspection Inspect(ProxySession session, PacketDirection direction, Packet packet)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(packet);

        try
        {
            switch (direction, packet.Opcode)
            {
                case (PacketDirection.ClientToModule, Opcodes.AgentAuthRequest):
                    var auth = packet.OpenReader();
                    auth.ReadUInt32();
                    session.PendingUsername = auth.ReadAscii();
                    break;
                case (PacketDirection.ClientToModule, Opcodes.LobbySelectCharacter):
                    session.PendingCharname = packet.OpenReader().ReadAscii();
                    break;
                case (PacketDirection.ModuleToClient, Opcodes.AgentAuthResponse) when Succeeded(packet):
                    session.Registry.Identify(session.Live, session.PendingUsername, charname: null, hwid: null);
                    break;
                case (PacketDirection.ModuleToClient, Opcodes.LobbySelectCharacterResponse) when Succeeded(packet):
                    session.Registry.Identify(session.Live, username: null, charname: session.PendingCharname, hwid: null);
                    break;
                default:
                    break;
            }
        }
        catch (EndOfPacketException)
        {
            return direction == PacketDirection.ClientToModule
                ? Inspection.Close("security: malformed identity packet")
                : Inspection.Pass(packet);
        }

        return Inspection.Pass(packet);
    }

    private static bool Succeeded(Packet packet) => packet.Payload.Length > 0 && packet.Payload[0] == Opcodes.LoginSucceeded;
}
