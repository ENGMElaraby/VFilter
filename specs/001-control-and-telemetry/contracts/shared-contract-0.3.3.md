# Shared contract 0.3.2 → 0.3.3 (PATCH), both repos

Add to the filter_commands "Claiming and cancelling" note:
> The filter marks a command `failed` with result `expired` instead of running it when it claims it more than
> 10 minutes after `created_at` (database clock). `notice` is `failed: not supported yet` until the proxy can
> send notices. `heartbeat_at` and `processed_at` are written with the database clock (`SYSUTCDATETIME()`).

Changelog: `- 0.3.3 (2026-10-17, PATCH): command expiry, database-clock timestamps (VFilter 001).`
