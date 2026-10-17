# Research: Proxy Core (VFilter 002)

## R1. Security layer: reuse MElarabyBot's, cross-checked with AegisCore-SRO
- **Decision**: port `SilkroadSecurity`, `PacketFramer`, `Packet`, `PacketReader/Writer`, `CountByteGenerator`,
  `SecurityChecksum`, `SecurityPolynomials`, `SecurityValues`, `SecurityFlags` and `SilkroadBlowfish` from the owner's
  MElarabyBot (`src/MElarabyBot.Core/Network`). That code was written from jMerlin's public protocol description,
  supports both roles (server toward the game client, client toward a real server), handles massive 0x600D groups,
  and runs against live vSRO servers in the bot's `SecureRelay`. Same owner, so there is no licence issue. Blowfish uses
  BouncyCastle (MIT) with Silkroad's little-endian halves.
- **Cross-check** with AegisCore-SRO (`framework/security`, `framework/network`):
  - same CRC polynomial table (first entry 0x968BD6B1)
  - same count generator, seed fallback 0x9ABFB3B6
  - same DH, challenge and final key transform (keyByte 3)
  - same EncryptRev byte order
  - count and CRC go only on client→server packets, with the CRC computed over the 6-byte header (CRC byte 0) plus payload, before encryption
  - the 0x8000 size flag
- **Rejected**: porting AegisCore's Go code (larger, server-framework coupled); RSBot (AGPL, never copied).

## R2. Proxy pump
- One `ProxySession` per accepted client. It connects to the real module and has two `SilkroadSecurity` instances
  (server role toward the client using `SecurityFlags.Standard` plus the checksum, and client role toward the module).
  It runs two receive loops, and each side is flushed under its own write lock, like the bot's SecureRelay.
- A packet hook (`IPacketInspector`) sees every application packet in each direction before it is forwarded. Gateway
  login handling, the redirect rewrite and session identity are inspectors. 003/004 add more.
- Bounded output: if a side's pending writes exceed 4 MB, the session closes (FR-013).
- Timeouts: the handshake must complete within 10 s, and a client with no data for 5 minutes is closed.

## R3. Gateway packets (vSRO 1.188, from AegisCore `gateway_codec.go` / `login_request_handler.go`)
- 0x6102 login request (client→gateway): `byte contentId, string username, string password, uint16 shardId`.
- 0xA102 login answer: `byte result`.
  - result 1 (success): `uint32 token, string agentIp, uint16 agentPort`.
  - result 2 (failure): `byte code`, then extra fields depending on the code (code 1 adds uint32 limit and uint32 attempts).
  - Codes seen: 1 wrong id/pw, 2 blocked, 4 already connected, 6 shard unavailable/full, 0x0B IP limit.
- 0x6103 agent auth (client→agent): `uint32 token, string username, string password, byte contentId, 6-byte MAC`.
- 0x7001 lobby character select (client→agent): `string charname`.
- Strings are uint16 length plus ASCII bytes.

## R4. Refusals for GM/Inspection modes
The filter answers 0x6102 itself with `0xA102 [2][code]` and does not forward it. Codes come from the settings
`gateway.gm_refusal_code` and `gateway.inspection_refusal_code`. Both default to 6 until checked with the real client
(the inspection message code still needs to be confirmed in-game).

## R5. Performance note
The ported framer allocates per frame. That is fine for this feature's 1 ms p99 target at 200 sessions, which is
measured in tests. Pooled buffers are left for when 003/004 profiling shows a need (constitution V allows a
measured approach). This is recorded in Complexity Tracking.
