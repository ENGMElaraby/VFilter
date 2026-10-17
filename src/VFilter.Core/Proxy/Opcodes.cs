namespace VFilter.Core.Proxy;

/// <summary>vSRO 1.188 opcodes the proxy core reads (AegisCore-SRO framework/network/opcode).</summary>
public static class Opcodes
{
    public const ushort GatewayPatchResponse = 0xA100;
    public const ushort GatewayLoginRequest = 0x6102;
    public const ushort GatewayLoginResponse = 0xA102;
    public const ushort AgentAuthRequest = 0x6103;
    public const ushort AgentAuthResponse = 0xA103;
    public const ushort LobbySelectCharacter = 0x7001;
    public const ushort LobbySelectCharacterResponse = 0xB001;

    public const byte LoginSucceeded = 1;
    public const byte LoginFailed = 2;

    /// <summary>0xA100 failure code meaning "patch available": followed by the Download server's address.</summary>
    public const byte PatchAvailable = 2;
}
