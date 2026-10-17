<!--
Sync Impact Report
- Version: 0.0.0 → 1.0.0 (initial ratification)
- Principles added: I–VII
- Templates checked: plan-template.md (Constitution Check gate uses these principles) ✅
-->
# VFilter Constitution

## Core Principles

### I. The Shared Contract Is Law
`docs/shared-contract.md` defines how the filter and VFilterWebsite exchange config, commands,
status and logs through the `VFILTER` database. The filter MUST NOT alter the schema, and writes
only `filter_status`, `filter_logs`, `filter_sessions` and `filter_commands.status/result`. Any
contract change is made in both repos together, with a version bump.

### II. Deny by Default (NON-NEGOTIABLE)
Only packets with a known opcode, valid direction, valid size and parseable structure are
forwarded. Anything else is dropped, logged at `security` level, and counted toward that
session's abuse score. Real module IPs/ports are never exposed to clients.

### III. Stability Over Features
The filter must never crash the game servers or take down other players' sessions. A
handler exception closes only that session. The proxy keeps running if the database is
unavailable: it uses the last good config and buffers logs. It must survive restarts and
reconnects cleanly.

### IV. Test-First for Packet Logic
Every packet parser, rule and limit is pure logic in `VFilter.Rules` / `VFilter.Packets`, with
xUnit tests written before or alongside it, using captured packet fixtures. Exploit fixes ship with
a test that reproduces the exploit packet.

### V. Performance Budget
Target: 1,000 concurrent players on one instance, under 1 ms of added latency per packet at p99,
no per-packet allocations on hot paths, and no per-packet database queries (config is cached in
memory and reloaded through `filter_config_version`).

### VI. Observable
Structured logs (module, category, ip, user, char, opcode). A heartbeat to `filter_status` every
5 s. Every block decision is logged with its reason so admins can see it in the website.

### VII. Simplicity and Current Versions
Latest .NET LTS, standard Microsoft.Extensions.* hosting/DI/logging, Dapper, xUnit. No speculative
plugin systems; build features in the phase order in HANDOFF.md. Complexity must be justified in
the plan's Complexity Tracking table.

## Technical Constraints
- C# on the latest .NET LTS. Runs as a Windows Service on Windows Server, and builds and tests on macOS.
- Talks to vSRO 1.188 server modules (Gateway, Download, Agent ×1–5). Our server is cap 100,
  Chinese race only.
- The client-side DLL (custom windows, HWID) is a separate, later project and its protocol is a
  versioned contract.

## Development Workflow
- Spec Kit flow for every feature: specify → clarify → plan → tasks → analyze → implement.
- One feature per branch (`NNN-short-name`), merged by PR into `main`.
- Gates before merge: `dotnet build` with warnings as errors, `dotnet test` green, and contract
  docs in sync.

## Governance
This constitution overrides other practices. Amendments need a written rationale, a version bump
(semver), and an update to dependent templates. `CLAUDE.md` holds day-to-day guidance.

**Version**: 1.0.0 | **Ratified**: 2026-10-17 | **Last Amended**: 2026-10-17
