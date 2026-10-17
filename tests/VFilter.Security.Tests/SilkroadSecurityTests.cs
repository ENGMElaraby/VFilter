
namespace VFilter.Security.Tests;

public sealed class SilkroadSecurityTests
{
    // Stand-in for the real polynomial constant: any 1024 bytes exercise the same code paths.
    private static readonly SecurityChecksum TestChecksum = SecurityChecksum.FromPolynomials(
        Enumerable.Range(0, SecurityPolynomials.RequiredLength).Select(i => (byte)((i * 73) ^ (i >> 3) ^ 0x5A)).ToArray());

    [Fact]
    public void The_opening_handshake_has_the_vsro_layout()
    {
        var server = SilkroadSecurity.CreateServer(SecurityFlags.Standard, TestChecksum);
        var client = SilkroadSecurity.CreateClient(TestChecksum);

        var opening = server.TakeOutgoing();

        // flags + blowfish key + count/crc seeds + handshake key, g, p, A = 37 bytes, as the Abyss server sends.
        Assert.Equal(37, opening[0] | (opening[1] << 8));
        Assert.Equal(0x5000, opening[2] | (opening[3] << 8));
        Assert.Equal((byte)SecurityFlags.Standard, opening[6]);

        Assert.Empty(client.Receive(opening));
        var response = client.TakeOutgoing();
        Assert.Equal(12, response[0] | (response[1] << 8)); // B + challenge, as the real client sends
    }

    [Fact]
    public void Client_and_server_complete_the_handshake()
    {
        var (client, server) = Connect();

        Assert.True(client.IsHandshakeComplete);
        Assert.True(server.IsHandshakeComplete);
        Assert.Equal(SecurityFlags.Standard, client.Flags);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Packets_cross_in_both_directions(bool encrypted)
    {
        var (client, server) = Connect();

        client.Send(new Packet(0x7021, [1, 2, 3, 4, 5], encrypted));
        client.Send(new Packet(0x2002, [], encrypted));
        var atServer = server.Receive(client.TakeOutgoing());

        server.Send(new Packet(0xB021, [9, 8, 7], encrypted));
        var atClient = client.Receive(server.TakeOutgoing());

        Assert.Equal(2, atServer.Count);
        Assert.Equal(0x7021, atServer[0].Opcode);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, atServer[0].Payload);
        Assert.Equal(encrypted, atServer[0].Encrypted);
        Assert.Equal(0x2002, atServer[1].Opcode);
        Assert.Equal(new byte[] { 9, 8, 7 }, Assert.Single(atClient).Payload);
    }

    [Fact]
    public void Encrypted_packets_are_not_readable_on_the_wire()
    {
        var (client, server) = Connect();
        var secret = "HELLO-SECRET"u8.ToArray();

        client.Send(new Packet(0x7001, secret, Encrypted: true));
        var wire = client.TakeOutgoing();

        Assert.True((wire[1] & 0x80) != 0);
        Assert.DoesNotContain("HELLO", System.Text.Encoding.ASCII.GetString(wire));
        Assert.Equal(secret, Assert.Single(server.Receive(wire)).Payload);
    }

    [Fact]
    public void Many_packets_keep_the_count_bytes_in_step()
    {
        var (client, server) = Connect();

        for (var i = 0; i < 300; i++)
        {
            client.Send(new Packet((ushort)(0x7000 + i), [(byte)i], Encrypted: i % 2 == 0));
        }

        var received = server.Receive(client.TakeOutgoing());

        Assert.Equal(300, received.Count);
    }

    [Fact]
    public void Bytes_arriving_one_at_a_time_still_decode()
    {
        var (client, server) = Connect();
        client.Send(new Packet(0x7074, [1, 2, 3, 4, 5, 6, 7, 8, 9], Encrypted: true));
        var wire = client.TakeOutgoing();

        var packets = new List<Packet>();
        foreach (var b in wire)
        {
            packets.AddRange(server.Receive([b]));
        }

        Assert.Equal(0x7074, Assert.Single(packets).Opcode);
    }

    [Fact]
    public void A_tampered_packet_is_rejected()
    {
        var (client, server) = Connect();
        client.Send(new Packet(0x7021, [1, 2, 3, 4], Encrypted: false));
        var wire = client.TakeOutgoing();

        // The checksum is one byte, so about 1 flip in 256 keeps it (the test was flaky): flip in a way it sees.
        var flip = Enumerable.Range(1, 255).Select(b => (byte)b).First(b => ChecksumWithLastByteFlipped(wire, server, b) != wire[5]);
        wire[^1] ^= flip;

        Assert.Throws<PacketSecurityException>(() => server.Receive(wire));
    }

    private static byte ChecksumWithLastByteFlipped(byte[] wire, SilkroadSecurity server, byte flip)
    {
        var copy = (byte[])wire.Clone();
        copy[^1] ^= flip;
        copy[5] = 0;
        return TestChecksum.Compute(CrcSeedOf(server), copy, 0, copy.Length);
    }

    [Fact]
    public void A_wrong_count_byte_is_rejected_even_with_a_valid_checksum()
    {
        var (client, server) = Connect();
        client.Send(new Packet(0x7021, [1], Encrypted: false));
        var wire = client.TakeOutgoing();
        wire[4] ^= 0xFF;
        wire[5] = 0;
        wire[5] = TestChecksum.Compute(CrcSeedOf(server), wire, 0, wire.Length);

        Assert.Throws<PacketSecurityException>(() => server.Receive(wire));
    }

    [Fact]
    public void Application_packets_wait_for_the_handshake()
    {
        var server = SilkroadSecurity.CreateServer(SecurityFlags.Standard, TestChecksum);
        var client = SilkroadSecurity.CreateClient(TestChecksum);

        client.Send(new Packet(0x6100, [1]));
        Assert.Empty(client.TakeOutgoing());

        var received = new List<Packet>();
        for (var round = 0; round < 4; round++)
        {
            client.Receive(server.TakeOutgoing());
            received.AddRange(server.Receive(client.TakeOutgoing()));
        }

        Assert.True(server.IsHandshakeComplete);
        Assert.Equal(0x6100, Assert.Single(received).Opcode);
    }

    [Fact]
    public void A_server_rejects_application_packets_before_the_handshake()
    {
        var server = SilkroadSecurity.CreateServer(SecurityFlags.Blowfish | SecurityFlags.Handshake);
        byte[] early = [0x00, 0x00, 0x00, 0x61, 0x00, 0x00];

        Assert.Throws<PacketSecurityException>(() => server.Receive(early));
    }

    [Fact]
    public void Massive_packets_are_split_and_reassembled()
    {
        var (client, server) = Connect();
        var big = Enumerable.Range(0, 10_000).Select(i => (byte)i).ToArray();

        server.Send(new Packet(0x3013, big, Massive: true));
        var wire = server.TakeOutgoing();
        var packets = client.Receive(wire);

        var packet = Assert.Single(packets);
        Assert.Equal(0x3013, packet.Opcode);
        Assert.True(packet.Massive);
        Assert.Equal(big, packet.Payload);
    }

    [Fact]
    public void A_client_without_the_checksum_table_refuses_a_server_that_needs_it()
    {
        var server = SilkroadSecurity.CreateServer(SecurityFlags.Standard, TestChecksum);
        var client = SilkroadSecurity.CreateClient(checksum: null);

        Assert.Throws<PacketSecurityException>(() => client.Receive(server.TakeOutgoing()));
    }

    [Fact]
    public void Without_security_bytes_no_checksum_table_is_needed()
    {
        const SecurityFlags flags = SecurityFlags.Blowfish | SecurityFlags.Handshake;
        var server = SilkroadSecurity.CreateServer(flags);
        var client = SilkroadSecurity.CreateClient(checksum: null);

        Pump(client, server);
        client.Send(new Packet(0x7001, [42], Encrypted: true));

        Assert.True(server.IsHandshakeComplete);
        Assert.Equal(new byte[] { 42 }, Assert.Single(server.Receive(client.TakeOutgoing())).Payload);
    }

    [Fact]
    public void A_wrong_challenge_fails_the_handshake()
    {
        var server = SilkroadSecurity.CreateServer(SecurityFlags.Standard, TestChecksum);
        var client = SilkroadSecurity.CreateClient(TestChecksum);
        client.Receive(server.TakeOutgoing());

        var response = client.TakeOutgoing();
        response[10] ^= 0x01; // inside the encrypted challenge; the checksum is recomputed below
        response[5] = 0;
        response[5] = TestChecksum.Compute(CrcSeedOf(server), response, 0, response.Length);

        Assert.Throws<PacketSecurityException>(() => server.Receive(response));
    }

    [Fact]
    public void The_handshake_accept_is_its_own_write_ahead_of_held_packets()
    {
        // 7Kingdom's gateway drops a packet that shares a TCP write with the 0x9000 accept.
        var server = SilkroadSecurity.CreateServer(SecurityFlags.Standard, TestChecksum);
        var client = SilkroadSecurity.CreateClient(TestChecksum);
        client.Send(new Packet(0x2001, [1, 2, 3], Encrypted: true)); // the game client's hello, queued early
        client.Receive(server.TakeOutgoing());
        server.Receive(client.TakeOutgoing());
        client.Receive(server.TakeOutgoing());

        var writes = client.TakeOutgoingWrites();

        Assert.Equal(2, writes.Count);
        Assert.Equal(6, writes[0].Length);
        Assert.Equal(0x9000, writes[0][2] | (writes[0][3] << 8));
        Assert.Equal(0x2001, Assert.Single(server.Receive([.. writes[0], .. writes[1]])).Opcode);
    }

    [Fact]
    public void A_handshake_packet_cannot_be_queued_by_hand()
    {
        var (client, _) = Connect();

        Assert.Throws<ArgumentException>(() => client.Send(Packet.Empty(0x5000)));
    }

    // Reads the server's CRC seed from its private state, only to forge a valid checksum in one test.
    private static uint CrcSeedOf(SilkroadSecurity server) =>
        (uint)typeof(SilkroadSecurity)
            .GetField("_crcSeed", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(server)!;

    private static (SilkroadSecurity Client, SilkroadSecurity Server) Connect()
    {
        var server = SilkroadSecurity.CreateServer(SecurityFlags.Standard, TestChecksum);
        var client = SilkroadSecurity.CreateClient(TestChecksum);
        Pump(client, server);
        return (client, server);
    }

    private static void Pump(SilkroadSecurity client, SilkroadSecurity server)
    {
        for (var round = 0; round < 4; round++)
        {
            Assert.Empty(client.Receive(server.TakeOutgoing()));
            Assert.Empty(server.Receive(client.TakeOutgoing()));
        }
    }
}

public sealed class MassiveLimitTests
{
    private static readonly SecurityChecksum Checksum = SecurityChecksum.Build();

    private static (SilkroadSecurity Server, SilkroadSecurity Client) Connected()
    {
        var server = SilkroadSecurity.CreateServer(SecurityFlags.Standard, Checksum);
        var client = SilkroadSecurity.CreateClient(Checksum);

        for (var i = 0; i < 4 && !(server.IsHandshakeComplete && client.IsHandshakeComplete); i++)
        {
            client.Receive(server.TakeOutgoing());
            server.Receive(client.TakeOutgoing());
        }

        Assert.True(server.IsHandshakeComplete && client.IsHandshakeComplete);

        return (server, client);
    }

    [Fact]
    public void A_server_refuses_a_massive_packet_before_the_handshake()
    {
        var server = SilkroadSecurity.CreateServer(SecurityFlags.Blowfish | SecurityFlags.Handshake);
        // [size=5][0x600D][count][crc] [header=1][parts=65535][opcode=0x3013]
        byte[] frame = [5, 0, 0x0D, 0x60, 0, 0, 1, 0xFF, 0xFF, 0x13, 0x30];

        Assert.Throws<PacketSecurityException>(() => server.Receive(frame));
    }

    [Fact]
    public void A_server_refuses_client_massive_packets_over_the_limit()
    {
        var (server, client) = Connected();
        client.Send(new Packet(0x7001, new byte[SilkroadSecurity.MaxClientMassiveBytes + 1], Massive: true));

        Assert.Throws<PacketSecurityException>(() => server.Receive(client.TakeOutgoing()));
    }

    [Fact]
    public void A_massive_packet_may_not_carry_a_handshake_opcode()
    {
        var (server, client) = Connected();
        client.Send(new Packet(0x600D, [1, 2, 3], Massive: true));

        Assert.Throws<PacketSecurityException>(() => server.Receive(client.TakeOutgoing()));
    }

    [Fact]
    public void Counts_bytes_held_until_the_handshake()
    {
        var client = SilkroadSecurity.CreateClient(Checksum);
        client.Send(new Packet(0x7001, new byte[100]));
        client.Send(new Packet(0x7002, new byte[50]));

        Assert.Equal(150, client.PendingBytes);
    }
}
