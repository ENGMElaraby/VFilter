# HANDOFF: VFilter (proxy filter)

**Date:** 2026-10-17 · **Status:** Feature **001-control-and-telemetry is built** (branch `001-control-and-telemetry`).
The filter is now linked to the website through the VFILTER database. Next: `002-proxy-core`.

## What exists
| Path | What |
| --- | --- |
| `VFilter.sln`, `Directory.Build.props` | .NET 10, nullable, warnings as errors, recommended analyzers |
| `src/VFilter.Host/` | Worker / Windows Service entry point (`dotnet run --project src/VFilter.Host`) |
| `src/VFilter.Core/` | Config cache, module controller, gateway mode, command processor, heartbeat, sessions, log buffer |
| `src/VFilter.Data/` | SQL Server repositories (Dapper, SqlBulkCopy) |
| `tests/VFilter.Core.Tests/` | Unit tests with in-memory fakes |
| `tests/VFilter.Data.Tests/` | SQL Server integration tests (`contract-schema.sql`), skipped without `VFILTER_TEST_CONNECTION` |
| `docs/shared-contract.md` | VFILTER DB contract 0.3.3. **Must stay identical to the website's copy.** |
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

Until 002, modules run on `StandbyModuleRunner`: they show Running, but **no ports are opened and no traffic flows yet**.

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

## Inputs needed before 002-proxy-core
1. Server files version (vSRO 1.188?) and the real IP/port of the Gateway, Download and each Agent.
2. Machine layout: does the filter run on the same Windows box as the modules?
3. A packet capture or two (optional but very useful) for test fixtures.

## Next features
**002-proxy-core**
```
/speckit-specify Proxy core for vSRO 1.188 implementing IModuleRunner: listen on guard IP/port per enabled module; Silkroad security handshake, Blowfish and packet framing in VFilter.Security; forward packets both ways; rewrite the Gateway login/agent-redirect response to our guard IP/port; register sessions in SessionRegistry (username/charname on login) and honour kick; per-module connection/online counts; gateway modes gm/inspection/public at login. Success = a real client logs in and plays through the filter.
```
**003-security** and **004-rules-engine** follow, as before: deny-by-default packet validation, lists/limits, and area rules. They use the config snapshots already loaded by 001.
