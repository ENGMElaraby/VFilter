# Implementation Plan: Control and Telemetry

**Branch**: `001-control-and-telemetry` | **Date**: 2026-10-17 | **Spec**: [spec.md](spec.md)

## Summary
A .NET 10 worker connects to VFILTER and runs these services:
- a config cache with 2 s per-area reloads into immutable snapshots
- a command processor with atomic claim, expiry, ordered dispatch and outcomes
- a 5 s heartbeat that uses the DB clock
- a session mirror
- a batched log writer with a bounded buffer

Module start/stop goes through `IModuleRunner`, which the proxy implements next. For now a standby
runner is used.

## Technical Context
**Language/Version**: C# 14 / .NET 10 LTS · **Dependencies**: Microsoft.Extensions.Hosting(+WindowsServices), Microsoft.Data.SqlClient, Dapper
**Storage**: SQL Server VFILTER (schema owned by the website) · **Testing**: xUnit; SQL Server integration via env-gated tests
**Target**: Windows Service (prod), console on macOS (dev) · **Project Type**: background service
**Performance Goals**: no DB call on the traffic path; reload 10k-entry list < 1 s · **Constraints**: survive DB outages
**Scale/Scope**: 7 modules, ≤ 1,000 sessions, log bursts of 10k events

## Constitution Check
| Principle | Status | Notes |
| --- | --- | --- |
| I. Contract is law | Pass | Writes only status/logs/sessions/command status. 0.3.3 PATCH in both repos. |
| II. Deny by default | N/A | No packets in this feature. |
| III. Stability | Pass | DB outages tolerated, last good config kept, failures isolated per command/row. |
| IV. Test-first | Pass | Unit tests with fakes plus SQL Server integration tests. |
| V. Performance | Pass | All DB work is on background services; snapshots are lock-free. |
| VI. Observable | Pass | Heartbeat every 5 s; command and config events are logged. |
| VII. Simplicity | Pass | Only the projects needed now: Host, Core, Data (Security/Packets/Rules arrive with the proxy). |

## Project Structure
```text
VFilter.sln
src/VFilter.Host/      Program.cs, appsettings.json, Worker registration (Windows Service)
src/VFilter.Core/      Config/, Modules/, Commands/, Sessions/, Logging/, Status/  (+ interfaces for repositories)
src/VFilter.Data/      SqlConnectionFactory, ConfigRepository, CommandRepository, StatusRepository,
                       SessionRepository, LogRepository (Dapper / SqlBulkCopy)
tests/VFilter.Core.Tests/   unit tests (fakes)
tests/VFilter.Data.Tests/   SQL Server integration tests + contract-schema.sql
Directory.Build.props       net10.0, nullable, warnings as errors, analyzers
```

## Complexity Tracking
None.
