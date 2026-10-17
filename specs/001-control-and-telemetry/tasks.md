---
description: "Tasks for VFilter 001 Control and Telemetry"
---

# Tasks: Control and Telemetry

**Input**: `specs/001-control-and-telemetry/` · **Tests**: required (constitution IV).

## Phase 1: Setup
- [ ] T001 Scaffold VFilter.sln: src/VFilter.Host (worker), src/VFilter.Core, src/VFilter.Data, tests/VFilter.Core.Tests, tests/VFilter.Data.Tests (xUnit); Directory.Build.props (net10.0, nullable, TreatWarningsAsErrors, latest analyzers); packages: Microsoft.Extensions.Hosting.WindowsServices, Microsoft.Data.SqlClient, Dapper
- [ ] T002 Apply contracts/shared-contract-0.3.3.md to docs/shared-contract.md in both repos
- [ ] T003 [P] tests/VFilter.Data.Tests/contract-schema.sql (all 11 contract tables, exact types) + `SqlServerFixture` (creates a fresh database per test class from the schema, skipped without `VFILTER_TEST_CONNECTION`)

## Phase 2: Foundational
- [ ] T004 Core types and repository interfaces per data-model.md (ConfigArea, snapshots, ModuleConfig, ModuleState, GatewayMode, PendingCommand, CommandOutcome, LiveSession, FilterLogEvent; IConfigRepository, ICommandRepository, IStatusRepository, ISessionRepository, ILogRepository, IDbClock-free design: DB writes use SYSUTCDATETIME)
- [ ] T005 [P] VFilter.Data: SqlConnectionFactory (options `VFilter:ConnectionString`, never logged)

## Phase 3: US3 Config cache (P1)
- [ ] T006 [P] Core tests: ConfigStore loads all areas, swaps atomically, reloads only changed versions, keeps last good on failure, skips bad rows (invalid JSON params, unknown setting type)
- [ ] T007 Core: ConfigStore + ConfigPoller (BackgroundService, 2 s, one warning per outage)
- [ ] T008 [P] Data tests + ConfigRepository (versions, 6 loaders)

## Phase 4: US1 Heartbeat and modules (P1)
- [ ] T009 [P] Core tests: ModuleController order (start Download→Agents→Gateway, stop reverse), disabled modules stay stopped, start errors → Error + message, already-running/stopped results, NeedsRestart when addresses change; GatewayModeState initial gm / inspection from setting
- [ ] T010 Core: IModuleRunner, StandbyModuleRunner, ModuleController, GatewayModeState, HeartbeatService (5 s, final stopped on shutdown)
- [ ] T011 [P] Data tests + StatusRepository (MERGE per module, DB clock heartbeat)

## Phase 5: US2 Commands (P1)
- [ ] T012 [P] Core tests: CommandProcessor oldest-first, claim-before-run, skip when claim fails, expiry > 10 min, start/stop/restart for module/all, set_gateway_mode payload validation, reload, kick/ban via ISessionRegistry, notice unsupported, bad payload/unknown target failed, exception isolated, result ≤ 500 chars, command log event
- [ ] T013 Core: CommandProcessor (1 s) + handlers
- [ ] T014 [P] Data tests + CommandRepository (pending query, atomic claim with db_now, finish; claim vs cancel race leaves exactly one winner)

## Phase 6: US4 Sessions and logs (P2)
- [ ] T015 [P] Core tests: SessionRegistry events, mirror throttles last-activity (30 s), LogBuffer batching (500 / 1 s), bound 50,000 with low-level-first drop + dropped count, field truncation
- [ ] T016 Core: SessionRegistry, SessionMirror, FilterLog (IFilterLog), LogWriter
- [ ] T017 [P] Data tests + SessionRepository (clear at start, insert/update/delete) + LogRepository (SqlBulkCopy)

## Phase 7: Host + polish
- [ ] T018 Host: Program.cs wiring, appsettings.json (no secrets), env override `VFILTER__ConnectionString`, Windows Service support, version from assembly
- [ ] T019 End-to-end smoke: website migrations on the container DB, run Host, observe status rows, queue a restart, see done
- [ ] T020 Update HANDOFF.md, CLAUDE.md, README of commands; mark tasks done
