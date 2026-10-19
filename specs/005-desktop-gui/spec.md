# Feature Specification: Desktop GUI

**Feature Branch**: `005-desktop-gui`

**Created**: 2026-10-19

**Status**: Draft

**Input**: User description: "Windows desktop GUI ("VFilter Desktop") that runs on the server machine next to the VFilter Windows service, as a second control surface beside the VFilterWebsite panel. Talks to the filter only through the shared VFILTER database contract (filter_status, filter_commands, filter_logs, filter_sessions); never bypasses it. Scope: module status (same data as filter_status, incl. GameServer row from 003), start/stop/restart and gateway mode via filter_commands, live log tail, online sessions with kick, tray icon with notifications, DB connection health. Settings editing stays in the website."

## Overview

MaxiGuard, which VFilter replaces, had a desktop window on the server. The owner wants VFilter to have one too,
next to the website panel. **VFilter Desktop** is a Windows desktop app that runs on the server machine beside the
VFilter Windows service. It is a second admin client of the same `VFILTER` database, not part of the filter:

- It **reads** what the filter publishes: module status (`filter_status`), online sessions (`filter_sessions`) and
  logs (`filter_logs`).
- It **acts** only by queueing rows in `filter_commands`, exactly as the website does (start, stop, restart,
  gateway mode, kick), and shows each command's result.
- It never talks to the filter process directly, never touches the game databases, and never edits configuration.
  Settings, lists, rules, modules and admin users stay in the website.

The filter cannot tell whether a command came from the website or the desktop app, and does not need to.

## Clarifications

### Session 2026-10-19

- Q: Which UI framework? → A: Avalonia. It builds, runs and gets headless UI tests on macOS (Constitution: builds and tests on macOS) and has a Windows tray icon. (Plan-level choice, recorded here because the owner decided it.)
- Q: Where may the app run? → A: On the server **and** on other PCs that can reach the `VFILTER` SQL Server (LAN or remote). Connections must be encrypted and use a least-privilege database login.
- Q: How does the desktop user sign in, and who is the author of commands? → A: With their **website panel account** (same email and password, same roles owner/admin/viewer). `created_by` is the panel user id, so histories match. This needs a contract change: the desktop app may read the website's `users` table (and record its actions where the website records them).
- Q: Which languages? → A: **English only** for this feature. Arabic/RTL is a later feature; text is kept separate from the screens so it can be added without rework.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See the filter's state at a glance (Priority: P1)

The owner is logged in to the server machine (for example over RDP). They open VFilter Desktop and see one row per
module: Gateway, Download, Agent1…5 and, when VFilter runs it, the GameServer. Each row shows state (stopped /
starting / running / error / offline), online players, connections, gateway mode (Gateway row), uptime, version,
last heartbeat age and last error. The GameServer row also shows its hook status. The view refreshes by itself.

**Why this priority**: Knowing whether the filter is up is the first thing an admin needs, and every other story
builds on this screen. It is also read-only, so it is safe to ship first.

**Independent Test**: Fill `filter_status` with rows in different states (one with a heartbeat older than 15 s,
one `error` with a message, a GameServer row with `details`). Open the app and confirm each row shows the right
state, counts and age, that the stale row shows **offline**, and that a change in the table appears within the
refresh interval without any click.

**Acceptance Scenarios**:

1. **Given** the filter heartbeats every 5 s, **When** the app is open, **Then** each module row updates within 5 s of a change in `filter_status`.
2. **Given** a module's `heartbeat_at` is more than 15 s old (by the database clock), **When** the row is shown, **Then** it reads **offline**, whatever its stored `state`.
3. **Given** a module is in `error`, **When** the row is shown, **Then** the `last_error` text is visible (in full on demand).
4. **Given** the GameServer row exists, **When** it is shown, **Then** its hook status and restart-needed items from `details` are visible, and unknown `details` fields are ignored.
5. **Given** no module has a fresh heartbeat, **When** the app opens, **Then** it says plainly that the filter service is not running.

---

### User Story 2 - Start, stop, restart and change gateway mode (Priority: P1)

From the module view the admin starts, stops or restarts one module, all agents, all modules, or the GameServer, and
switches the gateway between GM, Inspection and Public. Each action is queued as a command, confirmed first, and its
progress (pending → running → done / failed / cancelled) and result text are shown. A pending command can be
cancelled.

**Why this priority**: Controlling the filter from the server itself, without opening a browser, is the main reason
the owner asked for a desktop app.

**Independent Test**: Against a test database with no filter running, press Restart on Agent1 and confirm a
`restart`/`agent1` row is queued, shown as pending, and can be cancelled. Then run the filter, queue Stop Gateway,
and confirm the row goes to done and the Gateway row shows stopped.

**Acceptance Scenarios**:

1. **Given** the admin presses Stop on the Gateway and confirms, **When** the command is queued, **Then** a `filter_commands` row `stop`/`gateway` is written with the same shape the website writes, and the app shows it as pending.
2. **Given** a start/stop/restart is already pending or running for a target (from the app or the website), **When** the admin asks for another one on that target, **Then** the app refuses and names the command in progress.
3. **Given** the admin switches the gateway to Public, **When** they confirm, **Then** the app warns that everyone can log in, then queues `set_gateway_mode` with `{"mode":"public"}`.
4. **Given** "Restart all agents", **When** confirmed, **Then** one command per enabled agent is queued, skipping and naming agents that already have a command in progress.
5. **Given** a command is still pending, **When** the admin cancels it, **Then** it becomes `cancelled` only if it was still pending at that moment; otherwise the app shows that it already started.
6. **Given** the filter finishes a command, **When** its status changes, **Then** the app shows done or failed with the filter's result text within 5 s.

---

### User Story 3 - Live log tail (Priority: P2)

The admin opens the Logs view and sees new filter log entries appear as they are written, newest at the bottom (or
top), with level, module, category, IP, user, character, opcode and message. They can pause the tail, filter by
level, module, category or free text, and copy an entry.

**Why this priority**: Logs are how an admin sees blocks, errors and attacks as they happen. The website has a log
page, but a live tail on the server is faster during an incident.

**Independent Test**: Insert log rows at a steady rate. Confirm new rows appear without a click, filters hide
non-matching rows, pause stops new rows from scrolling in (and resume catches up), and memory stays flat over an
hour.

**Acceptance Scenarios**:

1. **Given** the tail is running, **When** the filter writes new log rows, **Then** they appear within 2 s, in order, without duplicates or gaps.
2. **Given** the admin filters to category `security`, **When** new rows arrive, **Then** only matching rows are shown.
3. **Given** the tail is paused, **When** rows arrive, **Then** the view does not move, and resuming shows the rows written while paused.
4. **Given** the filter writes thousands of rows per minute, **When** the tail runs for hours, **Then** the app keeps only the most recent rows in view (a fixed cap) and stays responsive.

---

### User Story 4 - Online sessions and kick (Priority: P2)

The admin opens the Sessions view and sees every connected session: module, IP, username, character, connected
since and last packet. They can search and sort, and kick a session. A kick is queued as a `kick` command with the
same target and payload the website uses, and its result is shown.

**Why this priority**: Kicking a troublemaker quickly is a frequent admin task, and MaxiGuard's window offered it.

**Independent Test**: Fill `filter_sessions`, search for a character, press Kick, confirm a `kick` row is queued
with target = charname and payload `{"session_id":…,"ip":…,"reason":…}`, and that the filter's result is shown.

**Acceptance Scenarios**:

1. **Given** sessions exist, **When** the Sessions view is open, **Then** the list refreshes by itself and matches `filter_sessions` within 5 s.
2. **Given** the admin kicks a session and confirms, **When** the command is queued, **Then** target is the character name, else the username, else the IP, as in the contract.
3. **Given** a kick for that target is already pending or running, **When** the admin kicks again, **Then** the app refuses and says a kick is already in progress.
4. **Given** the filter answers `failed: player not online`, **When** the result arrives, **Then** the app shows it.

---

### User Story 5 - Tray icon and alerts (Priority: P3)

The app keeps running in the Windows notification area when its window is closed. The tray icon shows overall
health (all running / some problem / filter offline / database unreachable). The admin gets a notification when a
module goes to error or offline, comes back, or when a command they queued fails. The tray menu opens the window and
offers the gateway mode switch and Start/Stop all.

**Why this priority**: Useful for an always-open server desktop, but the app is already valuable without it.

**Independent Test**: Close the window and confirm the app stays in the tray. Set a module's heartbeat stale and
confirm the icon changes and one notification appears (not one per refresh). Bring it back and confirm a recovery
notification.

**Acceptance Scenarios**:

1. **Given** the window is closed, **When** the admin clicks the close button, **Then** the app stays in the tray; Exit in the tray menu quits it.
2. **Given** a module goes from running to offline, **When** the change is seen, **Then** exactly one notification is shown for that change.
3. **Given** the admin turns notifications off in the app, **When** states change, **Then** no notifications appear but the icon still changes.

---

### User Story 6 - Database connection health (Priority: P2)

The app always shows whether it can reach the `VFILTER` database, how long the last refresh took, and when data was
last refreshed. If the database is unreachable, the app says so, keeps showing the last data it had (marked as
stale), disables actions that need the database, and reconnects by itself.

**Why this priority**: The app only works through the database, so a database problem must never look like a
filter problem, and a stale screen must never look current.

**Independent Test**: Stop SQL Server while the app runs. Confirm a clear "database unreachable" state within one
refresh, stale data marked, action buttons disabled, and recovery without restarting the app once SQL Server is back.

**Acceptance Scenarios**:

1. **Given** the database becomes unreachable, **When** the next refresh fails, **Then** the app shows "database unreachable" with the error and the time of the last good refresh.
2. **Given** the database is unreachable, **When** the admin tries an action, **Then** it is disabled, and nothing is queued later by surprise.
3. **Given** the database comes back, **When** the next retry succeeds, **Then** the app returns to normal without a restart.
4. **Given** the app starts with no or a wrong connection setting, **When** it opens, **Then** it shows a setup screen to enter and test the connection instead of crashing.

---

### Edge Cases

- The filter service is stopped: commands stay pending and expire after 10 minutes (`failed: expired`). The app must say the filter is offline so the admin knows a queued command will not run.
- The website and the app act on the same target at once: the in-progress check is made at the moment of queueing, so only one start/stop/restart (and one kick) per target can be pending or running.
- The app's clock differs from the database clock: every age (heartbeat, uptime, "x s ago") is computed from database time, never the local clock.
- An Agent is disabled in the website while the app is open: its row disappears or shows disabled on the next refresh, and its buttons are removed.
- A log row has a very long message or `data`: the list shows a shortened line; the full text is visible on demand.
- Two copies of the app are opened on the same machine: the second one brings the first to the front instead of running twice.
- The user has no rights to act (see FR-020): action controls are hidden and any attempt is refused.
- Arabic and Chinese usernames and character names display correctly (Unicode).
- A panel user is deleted or made viewer while signed in to the app: actions stop within a minute.
- The database is reached over a slow link: refreshes never overlap, and a slow refresh is shown, not stacked.

## Requirements *(mandatory)*

### Functional Requirements

**Contract and safety**

- **FR-001**: The app MUST read and write the `VFILTER` database only as `docs/shared-contract.md` allows an admin client. It MUST NOT connect to the filter process, the game databases or the game server files, and MUST NOT change the schema or any configuration table.
- **FR-002**: The app MUST write only new `filter_commands` rows, and change a command only from `pending` to `cancelled` with the same conditional rule the website uses.
- **FR-003**: Every command the app queues MUST have the same command names, targets and payload shapes as the website's (contract 0.4.0 on the 003 branch, including target `gameserver`).
- **FR-004**: Before queueing start/stop/restart or kick, the app MUST refuse when a command of that kind is already pending or running for the same target, whoever queued it.
- **FR-005**: Each command MUST record which admin queued it from the desktop app, so the website's command history and the app's show the same author: `created_by` is the signed-in panel user's id.
- **FR-005a**: Users MUST sign in with their website panel account (email and password, checked against the website's stored password hash). Wrong passwords MUST be throttled (at most 5 tries per minute per account and per machine), and the app MUST never store the password.
- **FR-005b**: A signed-in desktop session MUST end after 30 minutes without use (re-sign-in needed to act; the tray keeps showing status read-only) and MUST stop as soon as the user's account is removed or demoted to viewer (checked at least every minute).

**Status (US1)**

- **FR-006**: The app MUST show one row per module in `filter_status`, with state, online count, connections, gateway mode, version, uptime, heartbeat age and last error, refreshed at least every 5 s.
- **FR-007**: A module whose heartbeat is older than 15 s (database clock) MUST be shown as offline.
- **FR-008**: The GameServer row MUST show hook status, active switches, hook errors and restart-needed items from `details`, ignoring fields it does not know.

**Commands (US2)**

- **FR-009**: Admins MUST be able to queue start, stop and restart for Gateway, Download, each Agent, all agents (one command per enabled agent), all modules, and the GameServer; and `set_gateway_mode` to GM, Inspection or Public.
- **FR-010**: Stop, Restart, Stop All and switching to Public MUST ask for confirmation; Public MUST warn that everyone can log in.
- **FR-011**: The app MUST show recent commands (from any source) with status, author, times and result, and let admins cancel pending ones.

**Logs (US3)**

- **FR-012**: The app MUST tail `filter_logs`, showing new rows within 2 s, with filters for level, module, category and text, pause/resume, and copy.
- **FR-013**: The tail MUST keep at most a fixed number of rows in memory (default 5,000) and MUST NOT re-read the whole table on each refresh.

**Sessions (US4)**

- **FR-014**: The app MUST list `filter_sessions` with search and sort, refreshed at least every 5 s.
- **FR-015**: Admins MUST be able to kick a session (with an optional reason), queued as `kick` with the contract's target and payload.

**Tray (US5)**

- **FR-016**: The app MUST live in the notification area, with an icon that reflects overall health and a menu to open the window, switch gateway mode, Start/Stop all, and exit.
- **FR-017**: The app MUST notify once per state change (module error/offline/recovered, database lost/back, own command failed), and notifications MUST be switchable off.

**Database health (US6)**

- **FR-018**: The app MUST show database reachability, last refresh time and duration, mark data stale when refreshes fail, disable actions while the database is unreachable, and reconnect by itself.
- **FR-019**: The connection setting MUST be enterable and testable in the app, stored only for the local machine, and never shown in plain text after it is saved.

**Access**

- **FR-020**: Only panel users with role admin or owner MUST see or use action controls; viewers get read-only views, with real module addresses never shown (the app reads none).
- **FR-021**: Every desktop action (queue, cancel, sign-in, failed sign-in) MUST be traceable afterwards (who, what, when, from which machine, result) where the website records its own actions, so one history covers both.

**General**

- **FR-022**: The app MUST run on the server machine and on any Windows PC that can reach the `VFILTER` SQL Server. Connections MUST be encrypted, and the app MUST warn (and require an explicit opt-in per profile) when the server certificate is not trusted.
- **FR-022a**: The app MUST work with a least-privilege database login that can only read `filter_status`, `filter_sessions`, `filter_logs`, `filter_commands`, `filter_modules` (names and enabled only) and the website's users, and write what FR-002 and FR-021 allow. The required grants are documented with the feature.
- **FR-023**: All text MUST be in English and kept apart from the screens, so Arabic/RTL can be added later without changing them.
- **FR-024**: Only one copy of the app MUST run per Windows user session.
- **FR-025**: The app MUST never crash on bad or missing data (unknown states, null fields, malformed `details`); it shows what it can and logs the problem locally.

### Out of Scope

- Editing settings, lists, area rules, cooldowns, GA permissions, modules or admin users (website only).
- `ban`, `notice` and `reload` commands (website only for now; can be added later with the same rules).
- Historical log search and reports (website).
- Installing, starting or stopping the VFilter Windows service itself (Windows tools / installer).

### Key Entities

- **Module status**: one `filter_status` row per module (and the GameServer), as published by the filter.
- **Command**: one `filter_commands` row, queued by the website or the app, run by the filter.
- **Session**: one `filter_sessions` row per connected client.
- **Log entry**: one `filter_logs` row.
- **Desktop user**: a website panel user (owner / admin / viewer) signed in to the app.
- **Connection profile**: the stored database connection the app uses, local to the machine.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On the real server, an admin can see every module's state and stop/start a module from the app in under 30 seconds from opening it, without a browser.
- **SC-002**: Status, sessions and command results in the app never lag the database by more than 5 s; new log rows appear within 2 s.
- **SC-003**: Every command the app queues is accepted and run by the unchanged filter, and appears in the website's command history with the right author.
- **SC-004**: The app runs for 7 days in the tray with steady memory (under 300 MB) and adds less than 1% CPU and fewer than 2 database queries per second on average with the window closed.
- **SC-005**: A database outage is shown within one refresh, and the app recovers without a restart in 100% of tests.
- **SC-006**: From a second PC on the LAN, an admin signs in with their panel account and stops/starts a module; a viewer account sees status but no actions; 5 wrong passwords in a row are throttled.

## Assumptions

- The app is an admin client, not part of the filter; Constitution I's "the filter writes only…" rule still holds for the filter, and the app follows the website's writer rules instead.
- Contract changes needed by this feature (desktop as a second command writer, reading `users`, recording desktop actions where the website records its own) are made in both repos with a MINOR version bump after 003-gameserver-hooks' 0.4.0.
- The website's password hashes are standard bcrypt, which the app can verify; the website's own login throttle is not shared, so the app keeps its own.
- Opening SQL Server to the LAN or Internet (firewall, TLS certificate, SQL login) is the server owner's setup, documented by the feature but not automated.
- The GameServer row and `gameserver` target come from 003-gameserver-hooks; until it is merged, the app simply shows no GameServer row.
- Refresh by polling (every 2–5 s) is acceptable; the database has no push channel today.
- The server runs Windows Server 2016 or later with the desktop experience; the app targets the same .NET as the filter.
- The website remains the main panel; the app covers day-to-day operations on the server.
