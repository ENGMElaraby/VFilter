using VFilter.Core.Modules;
using VFilter.Security;

namespace VFilter.Core.Proxy;

/// <summary>Who is on an Agent session (spec 002 US3): username from 0x6103, character from 0x7001.</summary>
public sealed class AgentInspector : IPacketInspector
{
    public bool AppliesTo(ModuleType type) => type == ModuleType.Agent;

    public Inspection Inspect(ProxySession session, PacketDirection direction, Packet packet)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(packet);

        if (direction != PacketDirection.ClientToModule)
        {
            return Inspection.Pass(packet);
        }

        try
        {
            switch (packet.Opcode)
            {
                case Opcodes.AgentAuthRequest:
                    var auth = packet.OpenReader();
                    auth.ReadUInt32();
                    session.Registry.Identify(session.Live, auth.ReadAscii(), charname: null, hwid: null);
                    break;
                case Opcodes.LobbySelectCharacter:
                    session.Registry.Identify(session.Live, username: null, charname: packet.OpenReader().ReadAscii(), hwid: null);
                    break;
                default:
                    break;
            }
        }
        catch (EndOfPacketException)
        {
            return Inspection.Close("security: malformed identity packet");
        }

        return Inspection.Pass(packet);
    }
}
