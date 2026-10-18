# HANDOFF: VFilter (proxy filter)

**Date:** 2026-10-18 · **Status:** **001-control-and-telemetry** and **002-proxy-core are built** and merged to
`main`. The filter is a working Silkroad proxy, controlled by the website.

## Role change (2026-10-18)
Production runs on **AegisCore** (our Go server, `ENGMElaraby/AegisCore-SRO`). AegisCore implements the filter side
of the contract natively: login gate, gateway modes, status, sessions, kick/ban/notice, and Start/Stop through a
Docker supervisor. Design: `AegisCore-SRO/_docs/vfilter-integration.md`.

VFilter is now the **optional edge shield**: a proxy in front of AegisCore (or any vSRO server) for checks that belong
at the edge. Game rules (area rules, cooldowns, Game Assistant permissions) are built in AegisCore, not here. Two
filter-side processes must never claim the same command. If VFilter runs in front of AegisCore, give it only edge
duties (see 003 below) and leave module control, kick/ban/notice and status to AegisCore.

## What exists
| Path | What |
| --- | --- |
| `VFilter.sln`, `Directory.Build.props` | .NET 10, nullable, warnings as errors, recommended analyzers |
| `src/VFilter.Host/` | Worker / Windows Service entry point (`dotnet run --project src/VFilter.Host`) |
| `src/VFilter.Core/` | Config cache, module controller, gateway mode, command processor, heartbeat, sessions, log buffer |
| `src/VFilter.Data/` | SQL Server repositories (Dapper, SqlBulkCopy) |
| `src/VFilter.Security/` | Silkroad packet security (handshake, Blowfish, count/CRC, massive), ported from the owner's MElarabyBot |
| `src/VFilter.Core/Proxy/` | Guard listeners, ProxySession (client ⇄ module), Gateway/Agent inspectors |
| `tests/VFilter.Core.Tests/` | Unit tests with in-memory fakes |
| `tests/VFilter.Data.Tests/` | SQL Server integration tests (`contract-schema.sql`), skipped without `VFILTER_TEST_CONNECTION` |
| `docs/shared-contract.md` | VFILTER DB contract 0.3.4. **Must stay identical to the website's copy.** |
| `specs/001-control-and-telemetry/` | Spec, plan, tasks (done) |

## What the website controls now does in the filter
- **Config**: all six areas are loaded at start. A 2 s poll reloads only changed areas, and the last good config is kept if the DB is down.
- **Commands** (1 s poll, oldest first, atomic claim, so a cancelled command never runs):
  - start/stop/restart for a module or all: done. Start order is Download → Agents → Gateway; stop is the reverse.
  - set_gateway_mode: done, and the status row shows the mode.
  - reload: done, with the list of areas.
  - kick: disconnects matching sessions, or `failed: player not online`.
  - ban: disconnects, then `failed: account ban not supported yet`.
  - notice: `failed: not supported yet`.
  - Commands older than 10 minutes are `failed: expired`.
- **Heartbeat** every 5 s per configured module: state, counts, gateway mode, version, uptime. It uses the DB clock and writes a final `stopped` on shutdown.
- **Sessions** are mirrored to `filter_sessions`, and leftovers are cleared at start.
- **Logs** go to `filter_logs` in batches (1 s / 500), with a 50k buffer that drops low levels first.

## What the proxy does (002)
- **Guard listeners**: each enabled module listens on its guard port. It binds the guard IP when that IP is on this machine, otherwise all interfaces.
- **Per client**: the filter connects to the real module and terminates Silkroad security on both sides. It forwards every packet in order, keeping each packet's encrypted flag. Massive packets are reassembled and split again.
- **Gateway login (0x6102)**: the gateway mode is applied.
  - GM and Inspection: only Allowed GM IPs (single IPs or ranges) are forwarded. Everyone else gets `0xA102 [2][code]` straight from the filter.
  - The codes come from the settings `gateway.gm_refusal_code` and `gateway.inspection_refusal_code` (default 6). **Check with the real client which code shows the right message.**
- **Login answer (0xA102 success)**: the Agent IP/port is rewritten to that Agent's guard IP/port. An unknown address closes the session, so real addresses never leak.
- **Sessions**: username from 0x6102/0x6103, character from 0x7001. Counts are connections, plus players with a character on Agents. Kick closes both sides.
- **Limits**:
  - Bad count/CRC, decrypt failure or oversize frame: the session closes (security log).
  - Handshake timeout 10 s, idle timeout 5 min, write timeout 30 s, output limit 4 MB.
- **Measured latency** (200 sessions, all in one process on a Mac): about +2.4 ms p50 / +5.4 ms p99 per round trip (two proxy passes). That is **above the 1 ms per-pass target**. Profile and pool buffers in 003.

## Manual test on the real server (needed: SC-001)
1. In the website's Modules page set each module's real IP/port (where the SRO server listens) and a guard IP/port (what players use; must differ from the real port if on the same machine). Enable them.
2. Run the filter on the server machine (`VFilter.exe`, or install as a Windows Service), with `VFilter__ConnectionString` pointing at VFILTER.
3. Point a client's gateway (Media.pk2 DIVISIONINFO/GATEWAY) at the Gateway guard IP and port. Log in, pick a character, play.
4. Try GM mode from a non-allowed IP and note which refusal message the client shows. Set the two `gateway.*_refusal_code` settings if needed.

## Run it locally
```bash
export DOTNET_ROOT=/opt/homebrew/opt/dotnet/libexec PATH=/opt/homebrew/opt/dotnet/bin:$PATH
dotnet build && dotnet test
# Integration tests + local run against the SQL Server container (password in ../.local-sqlserver.env):
export VFILTER_TEST_CONNECTION="Server=127.0.0.1,14333;User Id=sa;Password=…;TrustServerCertificate=true"
VFilter__ConnectionString="Server=127.0.0.1,14333;Database=VFILTER_E2E;User Id=sa;Password=…;TrustServerCertificate=true" \
  dotnet run --project src/VFilter.Host
```
Stop it with Ctrl+C or SIGTERM to the `VFilter` process. `dotnet run` in the background does not forward signals.


## Next features
**003-edge-checks** (next, only if the edge shield is wanted): deny-by-default opcode whitelists per module and
direction, size checks, known exploit blocks, flood/rate limits, blocked IPs (CIDR) at connect. All as
`IPacketInspector`s on the 002 pipeline, using the 001 config snapshots. Include the latency work (pooled buffers),
which is above target today.
Not here any more: HWID and IP/PC/cafe limits, GM login security, area rules and cooldowns. Those go into AegisCore,
where the game state is.
