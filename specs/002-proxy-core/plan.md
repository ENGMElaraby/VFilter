# Implementation Plan: Proxy Core

**Branch**: `002-proxy-core` | **Date**: 2026-10-17 | **Spec**: [spec.md](spec.md)

## Summary
- Port the owner's proven Silkroad security layer into `VFilter.Security`.
- Add `ProxyModuleRunner : IModuleRunner` in `VFilter.Core/Proxy`. It manages a TCP listener per guard endpoint and one
  `ProxySession` per client: two security contexts, ordered forwarding, inspectors, bounded output and timeouts.
- Inspectors:
  - Gateway login: mode refusals, plus the username and token for later Agent linking.
  - Login answer redirect rewrite to the Agent guard address.
  - Agent auth and character select identity.
- Sessions go through `SessionRegistry` (001), which also handles kick and counts.

## Technical Context
**Language**: C# / .NET 10 · **Dependencies**: BouncyCastle.Cryptography (MIT, Blowfish) · **Testing**: xUnit with loopback sockets
**Performance**: < 1 ms p99 added latency at 200 sessions (test) · **Constraints**: no DB on the traffic path, per-session isolation

## Constitution Check
| Principle | Status | Notes |
| --- | --- | --- |
| I. Contract | Pass | Reads modules, lists and settings from snapshots. New setting keys `gateway.*_refusal_code` are documented for the 006 catalog. |
| II. Deny by default | Partial | Bad security bytes, decrypt failures and oversize frames close the session. Opcode whitelists are 003 (planned). |
| III. Stability | Pass | Failures close only one session. A listener error affects only its module. |
| IV. Test-first | Pass | Ported security tests plus proxy loopback tests (fake modules speaking the protocol). |
| V. Performance | Measured | The framer allocates per frame (see Complexity Tracking). |
| VI. Observable | Pass | Security, connection and login events go to the log buffer. |
| VII. Simplicity | Pass | Reuses proven code and adds one project. |

## Project Structure
```text
src/VFilter.Security/   Packet, PacketReader/Writer, PacketFramer, FramedPacket, SilkroadBlowfish, Security/*
src/VFilter.Core/Proxy/ ProxyModuleRunner, GuardListener, ProxySession, IPacketInspector, GatewayInspector,
                        AgentInspector, Opcodes, ProxyOptions
tests/VFilter.Security.Tests/   ported tests
tests/VFilter.Core.Tests/Proxy/ loopback tests with FakeModule (server-role security) + FakeClient (client-role)
```

## Complexity Tracking
| Item | Why | Simpler alternative rejected because |
| --- | --- | --- |
| Per-frame allocations in the framer | Reuses code proven against live servers | A pooled rewrite now would risk protocol bugs before the proxy has real traffic. Optimise with measurements in 003. |
