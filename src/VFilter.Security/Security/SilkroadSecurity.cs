// Ported from the owner's MElarabyBot (src/MElarabyBot.Core/Network), written from jMerlin's public
// description of the Silkroad protocol. See specs/002-proxy-core/research.md R1.
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace VFilter.Security;

/// <summary>
/// One end of a Silkroad connection's packet security: the 0x5000/0x9000 handshake (a small
/// Diffie-Hellman exchange that agrees a Blowfish key), Blowfish bodies, the client's count and checksum
/// bytes, and "massive" (0x600D) packet groups. Written from jMerlin's public description of the protocol.
/// <para>
/// Feed received bytes to <see cref="Receive"/>; it handles handshake packets itself and returns the
/// application packets. Queue packets with <see cref="Send"/> and write <see cref="TakeOutgoing"/> to the
/// socket. Application packets wait in the queue until the handshake completes. Thread-safe.
/// </para>
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "MemoryStream holds no unmanaged resources.")]
public sealed class SilkroadSecurity
{
    /// <summary>The largest chunk of a massive packet's data per 0x600D part.</summary>
    public const int MassiveChunkSize = 4089;

    public const int MaxPayload = 0x7FFF;

    /// <summary>
    /// Largest reassembled massive packet accepted from a game client (server role). Clients only send small
    /// massive groups; without a cap an unauthenticated peer could grow memory without bound (VFilter review).
    /// </summary>
    public const int MaxClientMassiveBytes = 1024 * 1024;

    /// <summary>Largest reassembled massive packet accepted from a real server (client role).</summary>
    public const int MaxServerMassiveBytes = 16 * 1024 * 1024;
    private const ushort EncryptedFlag = 0x8000;

    private readonly object _gate = new();
    private readonly Func<uint> _random;
    private readonly SecurityChecksum? _checksum;
    private readonly PacketFramer _framer = new();
    private readonly Queue<Packet> _outgoing = new();
    private readonly Queue<Packet> _handshakeOutgoing = new();

    private SilkroadBlowfish? _blowfish;
    private CountByteGenerator? _count;
    private uint _crcSeed;

    // Handshake state (names follow the protocol's usual g, p, x, A, B, K).
    private ulong _handshakeKey;
    private uint _g;
    private uint _p;
    private uint _x;
    private uint _a;
    private uint _b;
    private uint _k;
    private SilkroadBlowfish? _challengeBlowfish;
    private bool _awaitingServerChallenge;
    private bool _awaitingClientResponse;
    private bool _awaitingAccept;

    private long _pendingBytes;

    // Massive (0x600D) reassembly.
    private ushort _massiveOpcode;
    private int _massiveRemaining;
    private MemoryStream? _massiveData;

    private SilkroadSecurity(SecurityRole role, SecurityChecksum? checksum, Func<uint>? random)
    {
        Role = role;
        _checksum = checksum;
        _random = random ?? RandomUInt32;
    }

    public SecurityRole Role { get; }

    /// <summary>The options the server chose, once known.</summary>
    public SecurityFlags Flags { get; private set; }

    /// <summary>True once both sides have agreed the key and application packets may flow.</summary>
    public bool IsHandshakeComplete { get; private set; }

    /// <summary>Raised (inside the lock) the moment the handshake completes.</summary>
    public event EventHandler? HandshakeCompleted;

    /// <summary>
    /// Security for our side of a connection to a real server: waits for the server's 0x5000.
    /// </summary>
    /// <param name="checksum">Needed to sign packets when the server asks for security bytes (vSRO always does).</param>
    public static SilkroadSecurity CreateClient(SecurityChecksum? checksum, Func<uint>? random = null) =>
        new(SecurityRole.Client, checksum, random);

    /// <summary>
    /// Security for our side of a connection from a real game client. Queues the opening 0x5000 at once;
    /// send it with <see cref="TakeOutgoing"/>.
    /// </summary>
    /// <param name="checksum">Needed to verify the client's checksum bytes; without it only the count byte is checked.</param>
    public static SilkroadSecurity CreateServer(
        SecurityFlags flags = SecurityFlags.Standard,
        SecurityChecksum? checksum = null,
        Func<uint>? random = null)
    {
        var security = new SilkroadSecurity(SecurityRole.Server, checksum, random);
        lock (security._gate)
        {
            security.StartServerHandshake(flags);
        }

        return security;
    }

    /// <summary>True when <see cref="TakeOutgoing"/> would return bytes.</summary>
    public bool HasOutgoing
    {
        get
        {
            lock (_gate)
            {
                return _handshakeOutgoing.Count > 0 || (IsHandshakeComplete && _outgoing.Count > 0);
            }
        }
    }

    /// <summary>Adds received bytes. Returns the application packets they complete, in order.</summary>
    /// <exception cref="PacketSecurityException">The peer broke the protocol; drop the connection.</exception>
    /// <exception cref="PacketFramingException">The stream is out of sync; drop the connection.</exception>
    public IReadOnlyList<Packet> Receive(ReadOnlySpan<byte> bytes)
    {
        lock (_gate)
        {
            var packets = new List<Packet>();
            foreach (var frame in _framer.Receive(bytes))
            {
                var packet = Decode(frame);
                Dispatch(packet, packets);
            }

            return packets;
        }
    }

    /// <summary>Queues a packet. Application packets are held until the handshake completes.</summary>
    public void Send(Packet packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        if (packet.Opcode is SecurityOpcodes.Handshake or SecurityOpcodes.HandshakeAccept)
        {
            throw new ArgumentException("Handshake packets are sent by the security layer itself.", nameof(packet));
        }

        if (!packet.Massive && packet.Payload.Length > MaxPayload)
        {
            throw new ArgumentException($"A packet payload is at most {MaxPayload} bytes; mark it massive to split it.", nameof(packet));
        }

        lock (_gate)
        {
            _outgoing.Enqueue(packet);
            _pendingBytes += packet.Payload.Length;
        }
    }

    /// <summary>Payload bytes queued but not yet taken (including packets held until the handshake completes).</summary>
    public long PendingBytes
    {
        get
        {
            lock (_gate)
            {
                return _pendingBytes;
            }
        }
    }

    /// <summary>Formats every packet that may be sent now and returns the bytes for the socket (possibly empty).</summary>
    public byte[] TakeOutgoing() => TakeOutgoingWrites().SelectMany(write => write).ToArray();

    /// <summary>
    /// Like <see cref="TakeOutgoing"/>, split into socket writes: each handshake packet alone, as the
    /// game client sends them (7Kingdom's gateway drops a packet that shares a write with the 0x9000),
    /// then the application packets together.
    /// </summary>
    public IReadOnlyList<byte[]> TakeOutgoingWrites()
    {
        lock (_gate)
        {
            var writes = new List<byte[]>();

            // Handshake packets go first: application packets queued early must not block them.
            while (_handshakeOutgoing.Count > 0)
            {
                var packet = _handshakeOutgoing.Dequeue();
                writes.Add(Format(packet.Opcode, packet.Payload, encrypted: false));
            }

            using var wire = new MemoryStream();
            while (IsHandshakeComplete && _outgoing.Count > 0)
            {
                var packet = _outgoing.Dequeue();
                _pendingBytes -= packet.Payload.Length;
                if (packet.Massive)
                {
                    WriteMassive(wire, packet);
                }
                else
                {
                    wire.Write(Format(packet.Opcode, packet.Payload, packet.Encrypted));
                }
            }

            if (wire.Length > 0)
            {
                writes.Add(wire.ToArray());
            }

            return writes;
        }
    }

    // ---- Server role ---------------------------------------------------------------------------

    private void StartServerHandshake(SecurityFlags flags)
    {
        Flags = flags & ~SecurityFlags.HandshakeResponse;
        var writer = new PacketWriter();
        writer.WriteByte((byte)Flags);

        if (Flags.HasFlag(SecurityFlags.Blowfish))
        {
            var initialKey = RandomUInt64();
            _blowfish = new SilkroadBlowfish(BitConverter.GetBytes(initialKey));
            writer.WriteUInt64(initialKey);
        }

        if (Flags.HasFlag(SecurityFlags.SecurityBytes))
        {
            var countSeed = _random() & 0xFF;
            _crcSeed = _random() & 0xFF;
            _count = new CountByteGenerator(countSeed);
            writer.WriteUInt32(countSeed);
            writer.WriteUInt32(_crcSeed);
        }

        if (Flags.HasFlag(SecurityFlags.Handshake))
        {
            _handshakeKey = RandomUInt64();
            _x = _random() & 0x7FFFFFFF;
            _g = _random() & 0x7FFFFFFF;
            do
            {
                _p = _random() & 0x7FFFFFFF;
            }
            while (_p < 2);

            _a = SecurityValues.ModPow(_g, _x, _p);
            writer.WriteUInt64(_handshakeKey);
            writer.WriteUInt32(_g);
            writer.WriteUInt32(_p);
            writer.WriteUInt32(_a);
            _awaitingClientResponse = true;
        }
        else
        {
            IsHandshakeComplete = true;
        }

        _handshakeOutgoing.Enqueue(writer.ToPacket(SecurityOpcodes.Handshake));
    }

    private void OnClientHandshakeResponse(Packet packet)
    {
        if (!_awaitingClientResponse)
        {
            throw new PacketSecurityException("The client sent an unexpected 0x5000.");
        }

        var reader = packet.OpenReader();
        _b = reader.ReadUInt32();
        var clientChallenge = reader.ReadUInt64();

        _k = SecurityValues.ModPow(_b, _x, _p);
        _challengeBlowfish = ChallengeBlowfish();

        var expected = Challenge(MakeLong(_b, _a), (byte)(_b & 0x07));
        if (expected != clientChallenge)
        {
            throw new PacketSecurityException("The client's handshake challenge is wrong.");
        }

        var writer = new PacketWriter();
        writer.WriteByte((byte)SecurityFlags.HandshakeResponse);
        writer.WriteUInt64(Challenge(MakeLong(_a, _b), (byte)(_a & 0x07)));
        _handshakeOutgoing.Enqueue(writer.ToPacket(SecurityOpcodes.Handshake));

        _blowfish = FinalBlowfish();
        _awaitingClientResponse = false;
        _awaitingAccept = true;
    }

    private void OnClientAccept()
    {
        if (!_awaitingAccept)
        {
            throw new PacketSecurityException("The client accepted a handshake that was not offered.");
        }

        _awaitingAccept = false;
        Complete();
    }

    // ---- Client role ---------------------------------------------------------------------------

    private void OnServerHandshake(Packet packet)
    {
        var reader = packet.OpenReader();
        var flags = (SecurityFlags)reader.ReadByte();

        if (flags.HasFlag(SecurityFlags.HandshakeResponse))
        {
            OnServerChallenge(reader);
            return;
        }

        if (IsHandshakeComplete || _awaitingServerChallenge)
        {
            throw new PacketSecurityException("The server restarted the handshake.");
        }

        Flags = flags;
        if (flags.HasFlag(SecurityFlags.Blowfish))
        {
            _blowfish = new SilkroadBlowfish(BitConverter.GetBytes(reader.ReadUInt64()));
        }

        if (flags.HasFlag(SecurityFlags.SecurityBytes))
        {
            if (_checksum is null)
            {
                throw new PacketSecurityException(
                    "The server requires security bytes, but the checksum table (SecurityPolynomials) is not set.");
            }

            _count = new CountByteGenerator(reader.ReadUInt32());
            _crcSeed = reader.ReadUInt32() & 0xFF;
        }

        if (!flags.HasFlag(SecurityFlags.Handshake))
        {
            Complete();
            return;
        }

        _handshakeKey = reader.ReadUInt64();
        _g = reader.ReadUInt32();
        _p = reader.ReadUInt32();
        _a = reader.ReadUInt32();
        if (_p < 2)
        {
            throw new PacketSecurityException("The server sent an invalid handshake modulus.");
        }

        _x = _random() & 0x7FFFFFFF;
        _b = SecurityValues.ModPow(_g, _x, _p);
        _k = SecurityValues.ModPow(_a, _x, _p);
        _challengeBlowfish = ChallengeBlowfish();

        var writer = new PacketWriter();
        writer.WriteUInt32(_b);
        writer.WriteUInt64(Challenge(MakeLong(_b, _a), (byte)(_b & 0x07)));
        _handshakeOutgoing.Enqueue(writer.ToPacket(SecurityOpcodes.Handshake));
        _awaitingServerChallenge = true;
    }

    private void OnServerChallenge(PacketReader reader)
    {
        if (!_awaitingServerChallenge)
        {
            throw new PacketSecurityException("The server sent a challenge before the handshake.");
        }

        var serverChallenge = reader.ReadUInt64();
        if (serverChallenge != Challenge(MakeLong(_a, _b), (byte)(_a & 0x07)))
        {
            throw new PacketSecurityException("The server's handshake challenge is wrong.");
        }

        _blowfish = FinalBlowfish();
        _awaitingServerChallenge = false;
        _handshakeOutgoing.Enqueue(Packet.Empty(SecurityOpcodes.HandshakeAccept));
        Complete();
    }

    // ---- Shared handshake math -----------------------------------------------------------------

    private void Complete()
    {
        IsHandshakeComplete = true;
        _challengeBlowfish = null;
        HandshakeCompleted?.Invoke(this, EventArgs.Empty);
    }

    // The temporary key both challenges are encrypted with.
    private SilkroadBlowfish ChallengeBlowfish()
    {
        var key = SecurityValues.KeyTransform(MakeLong(_a, _b), _k, (byte)(_k & 0x03));
        return new SilkroadBlowfish(BitConverter.GetBytes(key));
    }

    private ulong Challenge(ulong value, byte keyByte)
    {
        var transformed = SecurityValues.KeyTransform(value, _k, keyByte);
        var bytes = BitConverter.GetBytes(transformed);
        _challengeBlowfish!.EncryptInPlace(bytes);
        return BitConverter.ToUInt64(bytes, 0);
    }

    private SilkroadBlowfish FinalBlowfish() =>
        new(BitConverter.GetBytes(SecurityValues.KeyTransform(_handshakeKey, _k, 0x03)));

    private static ulong MakeLong(uint low, uint high) => ((ulong)high << 32) | low;

    // ---- Receive path --------------------------------------------------------------------------

    private Packet Decode(FramedPacket frame)
    {
        var wire = frame.Wire ?? throw new InvalidOperationException("The framer did not keep the wire bytes.");
        var sizeField = BinaryPrimitives.ReadUInt16LittleEndian(wire);
        var length = sizeField & MaxPayload;

        if (frame.EncryptedSize)
        {
            if (_blowfish is null)
            {
                throw new PacketSecurityException("Received an encrypted packet before a key was agreed.");
            }

            _blowfish.DecryptInPlace(wire.AsSpan(PacketFramer.SizeFieldLength));
        }

        var opcode = BinaryPrimitives.ReadUInt16LittleEndian(wire.AsSpan(2));
        var count = wire[4];
        var crc = wire[5];

        // Only a server checks security bytes: clients sign what they send, servers do not.
        if (Role == SecurityRole.Server && _count is not null)
        {
            var expectedCount = _count.Next();
            if (count != expectedCount)
            {
                throw new PacketSecurityException($"Packet 0x{opcode:X4} has count byte {count:X2}, expected {expectedCount:X2}.");
            }

            if (_checksum is not null)
            {
                wire[5] = 0;
                var expectedCrc = _checksum.Compute(_crcSeed, wire, 0, PacketFramer.HeaderSize + length);
                if (crc != expectedCrc)
                {
                    throw new PacketSecurityException($"Packet 0x{opcode:X4} has checksum {crc:X2}, expected {expectedCrc:X2}.");
                }
            }
        }

        var payload = wire.AsSpan(PacketFramer.HeaderSize, length).ToArray();
        return new Packet(opcode, payload, frame.EncryptedSize);
    }

    private void Dispatch(Packet packet, List<Packet> applicationPackets)
    {
        switch (packet.Opcode)
        {
            case SecurityOpcodes.Handshake when Role == SecurityRole.Client:
                OnServerHandshake(packet);
                break;
            case SecurityOpcodes.Handshake:
                OnClientHandshakeResponse(packet);
                break;
            case SecurityOpcodes.HandshakeAccept when Role == SecurityRole.Server:
                OnClientAccept();
                break;
            case SecurityOpcodes.Massive when Role == SecurityRole.Server && !IsHandshakeComplete:
                throw new PacketSecurityException("The client sent a massive packet before finishing the handshake.");
            case SecurityOpcodes.Massive:
                OnMassivePart(packet, applicationPackets);
                break;
            default:
                if (Role == SecurityRole.Server && !IsHandshakeComplete)
                {
                    throw new PacketSecurityException($"The client sent 0x{packet.Opcode:X4} before finishing the handshake.");
                }

                applicationPackets.Add(packet);
                break;
        }
    }

    private void OnMassivePart(Packet part, List<Packet> applicationPackets)
    {
        var reader = part.OpenReader();
        var isHeader = reader.ReadByte() == 1;
        if (isHeader)
        {
            _massiveRemaining = reader.ReadUInt16();
            _massiveOpcode = reader.ReadUInt16();
            if (_massiveOpcode is SecurityOpcodes.Handshake or SecurityOpcodes.HandshakeAccept or SecurityOpcodes.Massive)
            {
                throw new PacketSecurityException($"A massive packet may not carry 0x{_massiveOpcode:X4}.");
            }

            _massiveData = new MemoryStream();
            if (_massiveRemaining == 0)
            {
                throw new PacketSecurityException("A massive packet announced no parts.");
            }

            return;
        }

        if (_massiveData is null)
        {
            throw new PacketSecurityException("A massive packet part arrived without its header.");
        }

        var limit = Role == SecurityRole.Server ? MaxClientMassiveBytes : MaxServerMassiveBytes;
        if (_massiveData.Length + reader.Remaining > limit)
        {
            throw new PacketSecurityException($"A massive packet exceeds {limit} bytes.");
        }

        _massiveData.Write(reader.ReadBytes(reader.Remaining));
        if (--_massiveRemaining == 0)
        {
            applicationPackets.Add(new Packet(_massiveOpcode, _massiveData.ToArray(), part.Encrypted, Massive: true));
            _massiveData = null;
        }
    }

    // ---- Send path -----------------------------------------------------------------------------

    private void WriteMassive(MemoryStream wire, Packet packet)
    {
        var parts = (packet.Payload.Length + MassiveChunkSize - 1) / MassiveChunkSize;
        if (parts == 0 || parts > ushort.MaxValue)
        {
            throw new InvalidOperationException($"A massive packet of {packet.Payload.Length} bytes cannot be split.");
        }

        var header = new PacketWriter();
        header.WriteByte(1);
        header.WriteUInt16((ushort)parts);
        header.WriteUInt16(packet.Opcode);
        wire.Write(Format(SecurityOpcodes.Massive, header.ToArray(), encrypted: false));

        for (var offset = 0; offset < packet.Payload.Length; offset += MassiveChunkSize)
        {
            var chunk = new PacketWriter();
            chunk.WriteByte(0);
            chunk.WriteBytes(packet.Payload.AsSpan(offset, Math.Min(MassiveChunkSize, packet.Payload.Length - offset)));
            wire.Write(Format(SecurityOpcodes.Massive, chunk.ToArray(), encrypted: false));
        }
    }

    private byte[] Format(ushort opcode, byte[] payload, bool encrypted)
    {
        encrypted &= _blowfish is not null;
        var total = PacketFramer.WireLength(payload.Length, encrypted);
        var wire = new byte[total];

        var sizeField = (ushort)(payload.Length | (encrypted ? EncryptedFlag : 0));
        BinaryPrimitives.WriteUInt16LittleEndian(wire, sizeField);
        BinaryPrimitives.WriteUInt16LittleEndian(wire.AsSpan(2), opcode);
        payload.CopyTo(wire, PacketFramer.HeaderSize);

        if (Role == SecurityRole.Client && _count is not null)
        {
            wire[4] = _count.Next();
            wire[5] = _checksum!.Compute(_crcSeed, wire, 0, PacketFramer.HeaderSize + payload.Length);
        }

        if (encrypted)
        {
            _blowfish!.EncryptInPlace(wire.AsSpan(PacketFramer.SizeFieldLength));
        }

        return wire;
    }

    private static uint RandomUInt32() => BinaryPrimitives.ReadUInt32LittleEndian(RandomNumberGenerator.GetBytes(sizeof(uint)));

    private ulong RandomUInt64() => MakeLong(_random(), _random());
}
