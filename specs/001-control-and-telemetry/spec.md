# Feature Specification: Control and Telemetry (filter ⇄ VFILTER database)

**Feature Branch**: `001-control-and-telemetry`

**Created**: 2026-10-17

**Status**: Draft

**Input**: User description: "Start on the filter itself: apply what we did in the website so that what the website controls actually reaches the filter. Connect the filter to the VFILTER DB per docs/shared-contract.md: config cache with per-area filter_config_version polling (2s, reload only changed areas), filter_commands processing (start/stop/restart/set_gateway_mode/reload/kick/notice/ban), filter_status heartbeat every 5s, filter_sessions, and batched filter_logs. Gateway modes gm/inspection/public. Keeps running on last good config if the DB is down."

## Overview

The website (VFilterWebsite features 001–005) already writes configuration, queues commands, and
shows status, logs and sessions. All of it goes through the shared `VFILTER` database
(`docs/shared-contract.md` 0.3.2). This feature makes the filter the other half of that contract:
- it reads and caches the configuration
- it executes queued commands and reports their outcome
- it reports each module's status every few seconds
- it mirrors its live sessions
- it writes its logs

The packet proxy itself (handshake, forwarding, Agent redirect) is the next feature. Here, modules
are started and stopped through a module controller that the proxy plugs into later. Until then,
starting a module means "the filter reports it running and is ready to accept the proxy".

This order was chosen on purpose, as the owner asked: the website's controls become real
end-to-end first.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - The website sees the filter alive (Priority: P1)

The operator starts the filter service. Within 5 seconds the website dashboard shows every enabled
module as Online, with its state, start time and filter version. The gateway card shows the gateway
mode. When the filter stops (cleanly or by crash), the dashboard shows the modules Offline within
15 seconds.

**Why this priority**: Without a heartbeat the dashboard is blind. This is the first proof that the
two halves are connected.

**Independent Test**: Run the filter against a VFILTER database with the website's default modules.
Check that `filter_status` gets one row per configured module, with `heartbeat_at` refreshed about
every 5 s by the database clock, the right state and version, and `gateway_mode` on the gateway row.
Then stop the filter and check that the rows stop refreshing.

**Acceptance Scenarios**:

1. **Given** the default modules (Gateway, Download, Agent1) are enabled, **When** the filter starts, **Then** within 5 seconds each has a status row with state `running`, a start time, the filter version and a fresh heartbeat.
2. **Given** a module is disabled in the website, **When** the filter runs, **Then** its status row shows state `stopped` and the module is not started.
3. **Given** the filter is running, **When** 5 seconds pass, **Then** every status row's heartbeat moves forward (measured by the database clock).
4. **Given** the filter stops cleanly, **When** shutdown completes, **Then** every status row shows `stopped` with its last heartbeat. The website shows them offline 15 s later.
5. **Given** a module fails to start, **When** the next heartbeat is written, **Then** its row shows state `error` with the error text.

---

### User Story 2 - Website commands are carried out (Priority: P1)

An admin clicks Start, Stop or Restart, or switches the gateway mode, in the website. Within a few
seconds the filter picks the command up, marks it running, carries it out, and marks it done (or
failed, with the reason). The website shows each step live. A command the admin cancelled while it
was pending is never run.

**Why this priority**: This is the core of the phase-1 definition of done: "click Restart Agent and
see the command move from pending to done".

**Independent Test**: Insert pending commands the way the website does: `restart agent1`,
`set_gateway_mode {"mode":"public"}`, a cancelled command, a command for an unknown target, and
`kick`. Check each moves to the expected final status and result, in order. Check the cancelled one
is untouched. Check the gateway mode shows in the status row.

**Acceptance Scenarios**:

1. **Given** a pending `restart` for `agent1`, **When** the filter polls, **Then** it claims it (pending → running) only if it is still pending, restarts Agent1, and sets `done` with a short result and the processing time, all within 3 seconds of the command being queued.
2. **Given** a command the website cancelled, **When** the filter polls, **Then** it never claims or runs it.
3. **Given** two filter polls race, or a cancel races with a claim, **When** both happen, **Then** the command is either run once or cancelled, never both and never twice.
4. **Given** a pending `set_gateway_mode` with `{"mode":"public"}`, **When** carried out, **Then** the gateway mode becomes Public immediately for new logins, the gateway status row shows `public` on the next heartbeat, and the command is `done`.
5. **Given** `start`/`stop`/`restart` for `all`, **When** carried out, **Then** every enabled module is acted on, Gateway last on start and first on stop, and the result lists each module's outcome.
6. **Given** a command for an unknown or disabled target, a bad payload, or an unsupported command, **When** processed, **Then** it is marked `failed` with a clear reason, and the filter keeps going.
7. **Given** a pending command older than 10 minutes when the filter first sees it (for example queued while the filter was down), **When** processed, **Then** it is marked `failed` with "expired", not run. Stale stops and restarts must not hit players by surprise.
8. **Given** `kick` or `ban` for a player, **When** processed, **Then** the filter acts on matching live sessions (in this feature there are none until the proxy exists) and reports `done` with how many sessions were affected, or `failed` with "player not online".
9. **Given** `reload`, **When** processed, **Then** every configuration area is reloaded from the database at once and the result says which areas changed.

---

### User Story 3 - Configuration changes take effect without a restart (Priority: P1)

An admin changes something in the website: a module address, a security list, a Game Assistant, a
setting, an area rule or a cooldown. Within 2 seconds the filter has the new values in memory, and
only the changed area was reloaded. If the database is unreachable, the filter keeps working with
the last configuration it loaded.

**Why this priority**: Every website page is only useful if the filter applies it live.

**Independent Test**: Start the filter, change a list entry and bump the `lists` counter the way the
website does. The filter's in-memory lists change within 2 s, and the other areas are not reloaded.
Stop the database. The filter keeps running on the old configuration, logs the problem, and recovers
when the database returns.

**Acceptance Scenarios**:

1. **Given** the filter starts, **When** it connects, **Then** it loads all six areas (settings, lists, area rules, cooldowns, modules, Game Assistant permissions) before starting modules.
2. **Given** the website bumps one area's counter, **When** the next poll runs (every 2 s), **Then** only that area is reloaded and swapped in at once, so readers never see a half-loaded area.
3. **Given** the database is unreachable at startup, **When** the filter starts, **Then** it retries with backoff and keeps the modules stopped until the first configuration loads. It never starts with no configuration.
4. **Given** the database becomes unreachable while running, **When** polls fail, **Then** the filter keeps the last good configuration, keeps modules running, logs a warning once (not every 2 s), and resumes normally when the database is back.
5. **Given** a configuration value that cannot be read (for example invalid JSON in an area rule), **When** the area loads, **Then** that one row is skipped and logged, and the rest of the area still loads.
6. **Given** a module's addresses changed, **When** the modules area reloads, **Then** running modules keep their current listeners until restarted, and the status shows the module needs a restart. The website already tells admins to restart.

---

### User Story 4 - Sessions and logs reach the website (Priority: P2)

Every connection the filter holds is mirrored in the session table, so the website's Sessions page
shows it. Every event the filter logs (connections, blocks, commands, errors) is written to the log
table in batches, so the website's Logs page shows it. Neither ever slows the game traffic down.

**Why this priority**: Operators need logs and sessions, but they only fill up once the proxy carries
traffic (next feature). The plumbing must exist and be tested now.

**Independent Test**: Register and remove fake sessions through the session registry, and log events
through the filter's logger. Check that the tables match within 2 s, that a burst of 10,000 events is
written in batches, and that with the database down events are buffered (bounded) and written when
it returns.

**Acceptance Scenarios**:

1. **Given** the filter starts, **When** it connects, **Then** it removes session rows left by a previous run (the filter is the only writer).
2. **Given** a session connects, logs in, or disconnects, **When** the change happens, **Then** the session row is inserted, updated (username, character) or deleted within 2 seconds. Last-activity time is updated at most every 30 seconds per session.
3. **Given** events are logged, **When** 1 second passes or 500 events are waiting, **Then** they are written in one batch.
4. **Given** the database is down, **When** events keep coming, **Then** up to 50,000 are buffered. Beyond that, debug and info events are dropped first and a count of dropped events is logged once the database returns.
5. **Given** commands are carried out, **When** they finish, **Then** each produces a `command`-category log event naming the command, target and outcome.

---

### Edge Cases

- The website and the filter run on different machines with clock skew: heartbeat and processing times are written with the database clock, never the filter machine's clock.
- Two filter instances are started by mistake against one database. Each claims commands atomically, so none run twice. The second instance logs a clear "another filter is already heartbeating these modules" warning at startup, based on status rows refreshed by a different process id in the last 15 s.
- A command whose payload JSON is malformed is marked `failed: bad payload`.
- Stop or restart of a module that is already stopped: `done` with "already stopped".
- Start of an already running module: `done` with "already running".
- Shutdown while a command is running: it finishes (with a short time limit) before the final heartbeat.
- A config area counter row is missing: it is treated as version 0 and loaded once.
- Very large lists (10,000 entries) load in under a second and are swapped in without blocking readers.

## Requirements *(mandatory)*

### Functional Requirements

**Configuration**

- **FR-001**: The filter MUST load all six configuration areas at startup and keep them in memory as read-only snapshots, swapped whole on reload.
- **FR-002**: The filter MUST poll every area's change counter every 2 seconds and reload only areas whose counter changed.
- **FR-003**: The filter MUST keep the last good configuration when the database is unreachable or an area fails to load, and MUST NOT start modules before a first configuration has loaded.
- **FR-004**: Unreadable rows MUST be skipped and logged individually, without failing the area.

**Commands**

- **FR-005**: The filter MUST poll for pending commands every second, oldest first, and claim each with a conditional "still pending" update. It runs a command only if its own claim succeeded.
- **FR-006**: The filter MUST carry out `start`, `stop`, `restart` (targets `all`, `gateway`, `download`, `agent1`…`agent5`), `set_gateway_mode` (`gm`, `inspection`, `public`), `reload`, `kick` and `ban` (payloads per contract 0.3.1). `notice` MUST be marked `failed: not supported yet` until the proxy can send in-game notices.
- **FR-007**: Every claimed command MUST end as `done` or `failed`, with a short human-readable result (≤ 500 characters) and the processing time from the database clock.
- **FR-008**: Commands pending for more than 10 minutes when claimed MUST be marked `failed: expired` without running.
- **FR-009**: Commands MUST run one at a time, in queue order. A failing command MUST NOT stop the processor.

**Status**

- **FR-010**: Every 5 seconds the filter MUST write one status row per configured module with state, connection count, online player count, gateway mode (gateway only), filter version, start time, last error and a heartbeat taken from the database clock.
- **FR-011**: The gateway MUST start in GM mode, or in Inspection mode when the setting `server.start_in_inspection` is true. The mode is held in memory, changed by commands, and reported in the status row.
- **FR-012**: On clean shutdown the filter MUST stop the modules in order and write a final status (`stopped`) for each.

**Sessions and logs**

- **FR-013**: The filter MUST mirror live sessions into the session table (insert, update names, delete), clear leftover rows at startup, and throttle last-activity updates to once per 30 seconds per session.
- **FR-014**: The filter MUST write log events in batches (every 1 s or 500 events), with a bounded buffer of 50,000. Under pressure it drops lower levels first and reports the dropped count.
- **FR-015**: Log rows MUST carry level, module, category, IP, HWID, username, character, opcode, message and optional detail, within the contract's column limits (longer values are cut).

**Operation**

- **FR-016**: The database connection MUST come from configuration (file plus environment variable override) and never be logged. The filter runs as a Windows Service on Windows and as a console app on macOS/Linux for development.
- **FR-017**: All database work MUST run off the traffic path. A slow or failed database call MUST NOT block packet handling.

### Key Entities

- **Configuration snapshot**: the in-memory, read-only copy of one area (settings, lists, area rules, cooldowns, modules, Game Assistant permissions) plus its version.
- **Module**: Gateway, Download or Agent1–5, with lifecycle state (stopped, starting, running, error), start time, last error and connection counts.
- **Gateway mode**: gm, inspection or public; held in memory.
- **Command**: a website instruction with its claim and outcome.
- **Session**: one live client connection with IP, HWID, username and character as known so far.
- **Log event**: one structured event queued for the log table.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: After the filter starts, the website shows every enabled module Online within 5 seconds.
- **SC-002**: A Restart queued in the website shows Done within 3 seconds, 100% of the time while the database is reachable.
- **SC-003**: A configuration change saved in the website is in the filter's memory within 2 seconds, with only the changed area reloaded.
- **SC-004**: With the database unreachable for 5 minutes, the filter keeps running on its last configuration, loses no warning- or higher-level log events (buffer ≤ 50,000), and recovers within 5 seconds of the database returning.
- **SC-005**: No command is ever run twice or run after being cancelled, verified under concurrent claim/cancel tests.
- **SC-006**: Database work adds no measurable latency to the traffic path, because no database call is made from it.

## Assumptions

- The VFILTER schema already exists, created by the website's migrations (shared contract 0.3.2). The filter never changes it.
- Until the proxy feature lands, "starting" a module does not open a listener: the module controller reports state only. The proxy will implement the same controller interface.
- `kick`/`ban` act on live sessions from the session registry. The game-account part of a ban needs a later feature, so here ban disconnects the sessions and reports `failed: account ban not supported yet; disconnected N session(s)`. The website must never show a ban as done when the account was not banned.
- Command expiry (10 minutes) is filter behaviour. It is added to the shared contract as a PATCH note in both repos.
- The filter version is the assembly's informational version.
- Development and tests run on macOS against a local SQL Server container. Production is Windows Server.
