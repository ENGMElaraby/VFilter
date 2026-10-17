# VFilter — CLAUDE.md

Our own **vSRO (Silkroad Online) proxy filter**, replacing MaxiGuard. It sits between players and
the GatewayServer / AgentServer / DownloadServer, decrypts and inspects every packet, blocks
exploits, and enforces limits and in-game rules. It's configured from **VFilterWebsite** through
the shared `VFILTER` SQL Server database.

## Read first
- `HANDOFF.md`: current state, decisions, and what to do next.
- `docs/shared-contract.md`: the database contract with the website. **Never change it in only one repo.**
- `docs/maxiguard-feature-map.md`: every MaxiGuard feature we're matching.
- `docs/references.md`: outside repos (protocol docs, servers, clients, PK2 tools), ranked, with licenses.
- `.specify/memory/constitution.md`: project rules. They override everything else here.

## References (full ranked list with licenses: `docs/references.md`)
https://github.com/ENGMElaraby/AegisCore-SRO is our open-source Go server for vSRO 1.188 (Gateway,
Agent, Download). Check it first for any protocol question: opcodes, packet layouts, the security
handshake, and the Gateway → Agent login handoff. It's Go, so port what we need to C#; no dependency
on it.
- `protocol/vsro188/`, `protocol/registry/`, `protocol/captures/`: opcodes, packet definitions, sanitized captures.
- `framework/network/`, `framework/security/`: framing, Blowfish/handshake, packet I/O.
- `gateway-server/`, `agent-server/`, `download-server/`: the server side of each flow we proxy.
- `_docs/` (e.g. `vsro188-opcode-online-research.md`, `vsro188-client-decoder-layouts.md`,
  `client-packet-tracing.md`, `sr-gameserver-*.md`): research notes on game systems.
- `REFERENCES.md`: its own list of upstream sources.

Secondary: https://github.com/ENGMElaraby/go-sro, our fork of `ferdoran/go-sro`, the older Go server
AegisCore grew from (last updated 2024-10). Use it when AegisCore lacks something or to compare an
older take: `framework/network/`, `framework/security/` (handshake), `framework/pk2/`,
`gateway-server/`, `agent-server/`. If the two disagree, AegisCore wins.

For per-opcode field layouts, also check https://github.com/ferdoran/silkroad-docs (`docs/messages/`,
`docs/network_protocol.md`, `docs/anticheat.md`). For the client side of the wire, read
https://github.com/ferdoran/openroad (`packets/`), but it is GPL-3.0: never copy its code.

## Workflow: Spec-Driven Development (GitHub Spec Kit)
Every feature goes through the spec-kit skills in `.claude/skills/`:
`/speckit-specify` → `/speckit-clarify` → `/speckit-plan` → `/speckit-tasks` → `/speckit-analyze` →
`/speckit-implement`. Don't write feature code without `specs/NNN-feature/` (spec + plan + tasks).

## Stack
- **C# on .NET** (latest LTS; .NET 10 at time of writing, check with `dotnet --version`).
- Windows Server is the production target (same machine or LAN as the SRO server files).
  It must also build and run its tests on macOS for development.
- Packet security (handshake, Blowfish, CRC/count bytes) lives in `VFilter.Security`, based on
  the public SilkroadSecurityApi design, rewritten for modern .NET (Span<byte>, no unsafe globals).
- Networking: `System.Net.Sockets` with async I/O (`SocketAsyncEventArgs` or Pipelines). One
  async loop per connection pair (client ⇄ module).
- Database: `Microsoft.Data.SqlClient` + Dapper. No EF migrations here; the website owns the schema.
- Logging: `Microsoft.Extensions.Logging`, written to console/file and to the `filter_logs` table
  (batched).
- Hosting: `Microsoft.Extensions.Hosting` worker, installable as a Windows Service.
- Tests: xUnit. Packet handlers are tested with captured packet fixtures (`tests/fixtures/*.bin`).

## Solution layout (target)
```
src/VFilter.Host/         entry point, DI, Windows Service, command-queue poller, heartbeat
src/VFilter.Core/         proxy engine: listeners, sessions, routing, Agent redirect
src/VFilter.Security/     SRO handshake + Blowfish + packet framing
src/VFilter.Packets/      opcode constants + packet readers/writers
src/VFilter.Rules/        limits, area rules, cooldowns, conditionals (pure logic, no I/O)
src/VFilter.Data/         VFILTER DB access: config cache, logs, status, commands
tests/VFilter.*.Tests/
```

## Commands
```bash
export DOTNET_ROOT=/opt/homebrew/opt/dotnet/libexec PATH=/opt/homebrew/opt/dotnet/bin:$PATH   # macOS (Homebrew .NET 10)
dotnet build
dotnet test                                    # SQL Server tests run only with VFILTER_TEST_CONNECTION set
dotnet run --project src/VFilter.Host          # needs VFilter__ConnectionString
```

## Conventions
- The filter **never** alters the DB schema. It reads config tables and writes only
  `filter_status`, `filter_logs`, `filter_sessions`, and `filter_commands.status/result`.
- Config is cached in memory. Poll `filter_config_version` every 2 s and reload on change.
  No per-packet DB queries, ever.
- Unknown or malformed packets are dropped and logged as `security`, never forwarded "just in case".
- Every rule is pure, testable logic in `VFilter.Rules`, with input = session state + packet,
  output = allow / block (+ message).
- Real module IP/ports are never sent to clients. Agent redirect packets are rewritten to Guard IP/Port.
- Heartbeat every 5 s into `filter_status`. Every DB timestamp uses the database clock (`SYSUTCDATETIME()`).
- Read config only from `ConfigStore` snapshots; never query config tables elsewhere.
- Commands run only after `ICommandRepository.TryClaimAsync` succeeded (pending → running), so a cancel and a claim never both win.
- Traffic code lives in `VFilter.Core/Proxy`: new checks are `IPacketInspector`s (pass / replace / answer / close), never DB calls on the traffic path. Sessions go through `SessionRegistry`.
- `VFilter.Security` is ported from the owner's MElarabyBot (written from jMerlin's public protocol notes). Never copy RSBot (AGPL) or openroad (GPL).
- Hot paths don't allocate per packet (use pooled buffers).

## Git
- Remote: `https://github.com/ENGMElaraby/VFilter`. Use spec-kit feature branches (`001-...`)
  and merge to `main` via PR.
