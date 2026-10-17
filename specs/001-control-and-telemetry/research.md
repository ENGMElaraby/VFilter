# Research: Control and Telemetry (VFilter 001)

## R1. Hosting and timers
`Microsoft.Extensions.Hosting` worker with `BackgroundService`s: ConfigPoller (2 s), CommandProcessor (1 s),
Heartbeat (5 s), SessionMirror (event driven, 2 s flush), LogWriter (1 s / 500 events). They use `PeriodicTimer`
(no drift accumulation) and run on the thread pool, never on socket loops (FR-017). `UseWindowsService()` on
Windows, console otherwise.

## R2. Database access
`Microsoft.Data.SqlClient` + Dapper, with one connection per operation (pooled). There are no ORM and no migrations.
- Heartbeat: one `MERGE filter_status` per module in one batch. `heartbeat_at = SYSUTCDATETIME()`, so the
  database clock is used and stays consistent with the website's `DatabaseClock`.
- Claim: `UPDATE filter_commands SET status='running' OUTPUT inserted.* , SYSUTCDATETIME() AS db_now
  WHERE id=@id AND status='pending'`. Zero rows means skip, because the command was cancelled or taken.
- Finish: `UPDATE … SET status=@s, result=@r, processed_at=SYSUTCDATETIME() WHERE id=@id AND status='running'`.
- Logs: `SqlBulkCopy` into `filter_logs` per batch (fast, minimal locking).
- Sessions: insert with `OUTPUT inserted.id`, update by id, delete by id. At startup, `DELETE filter_sessions`
  (the filter is the only writer).

## R3. Configuration snapshots
Each area is an immutable record (`FrozenDictionary`/`ImmutableArray`) held in a `volatile` reference and swapped
whole (`Interlocked.Exchange`). Readers never lock. A reload reads all rows of one area, parses each row
defensively (a bad row is skipped and logged), and then swaps. Versions are read with one query over
`filter_config_version`; a missing row counts as 0.

## R4. Module controller seam
`IModuleRunner` (StartAsync/StopAsync per `ModuleConfig`) is what the proxy (next feature) implements.
This feature ships `StandbyModuleRunner`, which opens no sockets and only transitions state.
`ModuleController` owns ordering (start: Download, Agents, then Gateway last; stop: reverse), state, start
time, errors and counts.

## R5. Stale commands
Claimed commands with `db_now − created_at > 10 min` are marked `failed: expired` (contract PATCH 0.3.3).

## R6. Tests
- Unit (xUnit, no DB): ConfigStore swap and parsing, CommandProcessor dispatch/expiry/ordering with in-memory
  repositories, ModuleController ordering and states, GatewayMode start rule, LogBuffer drop policy.
- Integration (`VFilter.Data.Tests`, `[Trait("Category","SqlServer")]`): runs against SQL Server when
  `VFILTER_TEST_CONNECTION` is set (local container), and is skipped otherwise. The schema comes from
  `tests/VFilter.Data.Tests/contract-schema.sql`, written from contract 0.3.2.
- An end-to-end smoke check runs the Host against the container with the website's defaults.
