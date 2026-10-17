namespace VFilter.Core.Proxy;

/// <summary>vSRO 1.188 opcodes the proxy core reads (AegisCore-SRO framework/network/opcode).</summary>
public static class Opcodes
{
    public const ushort GatewayLoginRequest = 0x6102;
    public const ushort GatewayLoginResponse = 0xA102;
    public const ushort AgentAuthRequest = 0x6103;
    public const ushort LobbySelectCharacter = 0x7001;

    public const byte LoginSucceeded = 1;
    public const byte LoginFailed = 2;
}
