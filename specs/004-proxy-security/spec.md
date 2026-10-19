# Feature Specification: Proxy Security

**Feature Branch**: `004-proxy-security`

**Created**: 2026-10-19

**Status**: Draft

**Input**: User description: "Make VFilter's proxy protect the vSRO 1.188 Gateway/Agent/Download modules the way MaxiGuard's network filter does: deny-by-default packet whitelist per module and direction with an abuse score; flood and rate limits; blocked IPs at connect; IP limits including Net Cafe IPs; GM login security; a framework of known exploit blocks with captured-packet tests; and meeting the performance budget. Out of scope: HWID, captcha, PIN, VPN detection, virtualisation blocking, area rules and cooldowns. Every block is logged with its reason."

## Context

VFilter already sits between players and the game servers (feature 002). It forwards every packet in both directions,
applies the gateway modes, and hides the real server addresses. It does not yet stop anything harmful:
- a malformed or unknown packet reaches the server
- a flood of packets or connections reaches the server
- a known exploit packet reaches the server
- an attacker can open unlimited connections from one address

MaxiGuard, which VFilter replaces, blocks all of these. This feature closes that gap. It is the core of what makes a
filter a filter.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Unknown and malformed packets never reach the server (Priority: P1)

A cheater or a bot sends a packet the game does not expect: an unknown type, the wrong direction, a wrong size, or a
broken structure. VFilter drops it before it reaches the server and logs it as a security event. Repeated offences add
up, and the player is disconnected once their abuse score passes the limit. Normal players never notice anything.

**Why this priority**: most server crashes and exploits start with a packet the server should never have received.
This is the constitution's non-negotiable rule II (deny by default).

**Independent Test**: replay a captured normal play session through VFilter: nothing is dropped. Replay sessions with
injected unknown, oversized and malformed packets: each one is dropped and logged, and the session is closed once it
crosses the abuse limit.

**Acceptance Scenarios**:

1. **Given** a normal player session, **When** the player logs in, picks a character and plays (moving, fighting,
   trading, chatting), **Then** no packet is dropped.
2. **Given** a session, **When** it sends a packet type that is not allowed for that server and direction, **Then**
   the packet is dropped, a security log entry names the packet type and the reason, and the session's abuse score
   rises.
3. **Given** a session, **When** it sends an allowed packet type with a size or structure outside the allowed shape,
   **Then** the packet is dropped and logged the same way.
4. **Given** a session whose abuse score passes the limit, **When** the next offence happens, **Then** the session is
   disconnected and the reason is logged.

---

### User Story 2 - Floods are stopped at the door (Priority: P1)

An attacker floods a server with packets from one session, or opens many connections from one address. VFilter
disconnects the flooding session, temporarily refuses that address, and keeps everyone else playing normally.

**Why this priority**: one flooder can lag or crash a whole server for every player.

**Independent Test**: a load tool sends packets above the per-session rate, and opens connections above the
per-address rate. The flooder is cut off and refused for the cool-off period, while a normal session running at the
same time keeps working with no added lag.

**Acceptance Scenarios**:

1. **Given** a session, **When** it sends more packets or bytes per second than the limit, **Then** it is disconnected
   and logged as `flood`.
2. **Given** an address, **When** it opens more connections per minute than the limit, or holds more unfinished
   handshakes than the limit, **Then** further connections from it are refused for the cool-off period, and this is
   logged once per cool-off (not once per connection).
3. **Given** an address in cool-off, **When** the cool-off ends, **Then** it can connect again.

---

### User Story 3 - Blocked addresses can't even connect (Priority: P1)

An admin adds an address or range to "Blocked IPs" on the website. Within a few seconds, any connection from it to any
server module is refused before the game handshake starts. Players already connected from it are disconnected.

**Why this priority**: it is the admin's first tool against a known attacker, and it costs the servers nothing.

**Independent Test**: block a test machine's address. Its next connection to Gateway, Download or Agent is closed at
once, and its existing session is cut.

**Acceptance Scenarios**:

1. **Given** an address in Blocked IPs (single address or range), **When** it connects to any module, **Then** the
   connection is closed before the handshake, and logged.
2. **Given** a connected player, **When** an admin blocks their address, **Then** they are disconnected within 5
   seconds.

---

### User Story 4 - Limits per address and Net Cafes (Priority: P2)

The server allows a set number of players per address (for example 3). A fourth login from the same address is refused
with a clear login error. Net Cafe addresses get their own, higher limits, as set on the website. Game Assistants and
Allowed GM addresses are exempt when "GM Exemption" is on.

**Why this priority**: limits stop multi-boxing and bot farms, which unbalance the game, but they don't threaten the
server itself.

**Independent Test**: with the limit set to 3, log in 4 accounts from one address: the 4th is refused. Add that address
as a Net Cafe with a limit of 10: 10 logins work and the 11th is refused.

**Acceptance Scenarios**:

1. **Given** the per-address limit N, **When** the (N+1)th player from one address tries to log in, **Then** the login
   is refused with the "IP limit" error, and logged as `limit`.
2. **Given** a Net Cafe address with its own login limit, **When** players log in from it, **Then** its limit applies
   instead of the global one (0 = the default cafe limit, -1 = unlimited).
3. **Given** GM Exemption on, **When** a Game Assistant account, or a player from an Allowed GM address, logs in,
   **Then** no address limit applies.
4. **Given** a limit lowered while players are online, **When** it changes, **Then** players already online stay
   online, and only new logins are checked.

---

### User Story 5 - GM accounts only from trusted addresses (Priority: P2)

With "GM Login Security" on, a GM account can only log in from an Allowed GM address. Anyone who steals a GM password
still can't use it from elsewhere.

**Why this priority**: a stolen GM account can destroy the game economy. This is an inexpensive, strong defence.

**Independent Test**: with the setting on, log in to a GM account from an allowed address (works) and from another
address (refused with a login error, and logged as `gm`).

**Acceptance Scenarios**:

1. **Given** GM Login Security on, **When** a GM account logs in from an address that is not allowed, **Then** the
   login is refused and logged with the account name and address.
2. **Given** GM Login Security off, **When** a GM account logs in from anywhere, **Then** the login proceeds as today.
3. **Given** VFilter cannot tell whether an account is a GM (its source is unavailable), **When** that account logs
   in, **Then** VFilter uses the last known GM list. It never lets a login through as "not GM" just because the source
   was down.

---

### User Story 6 - Known exploits are blocked by name (Priority: P2)

VFilter carries a list of named exploit rules, such as a known item-duplication or crash packet. Each rule blocks its
exploit, logs it as `exploit` with the rule's name, and can be switched off from the website if it ever causes trouble.

**Why this priority**: the whitelist (US1) already stops most unknown attacks. Named rules catch attacks that use
allowed packet types with harmful contents.

**Independent Test**: replay each rule's captured exploit packet: it is blocked and logged with the rule name. Replay
normal play: no rule fires.

**Acceptance Scenarios**:

1. **Given** a known exploit packet, **When** a session sends it, **Then** it is dropped, logged with the rule name,
   and the session's abuse score rises (or the session is closed, if the rule says so).
2. **Given** a rule switched off on the website, **When** its exploit packet arrives, **Then** that rule does nothing.
   The other protections still apply.

---

### User Story 7 - Protection without lag (Priority: P1)

Players can't feel the filter. With 1,000 players online and every protection on, the extra delay VFilter adds stays
within the budget.

**Why this priority**: a filter that lags the game will be switched off, which is worse than no filter. Feature 002
measured +2.4 ms p50 / +5.4 ms p99 per round trip, which is above the budget.

**Independent Test**: a load test with 1,000 simulated players and every protection on measures the added delay per
packet.

**Acceptance Scenarios**:

1. **Given** 1,000 simulated players with all protections on, **When** they play for 10 minutes, **Then** the delay
   VFilter adds per packet is under 1 ms at the 99th percentile.

---

### Edge Cases

- **A legitimate packet missing from the whitelist** (for example after a client update): it is dropped and logged.
  Admins see the packet type in the logs and can report it. A short run in observe-only mode before enforcing catches
  these (see FR-002).
- **Players behind a shared address** (a household or a carrier NAT): the address limit refuses the extra logins with a
  clear message. Admins can add the address as a Net Cafe with a higher limit.
- **The database is down**: all protections keep working with the last good config (constitution III). The block lists
  and limits stay as they were.
- **A flood of rejected connections**: refusals are logged once per address per cool-off, so the logs and the database
  are not flooded either.
- **An address both blocked and allowed as GM**: blocked wins.
- **Settings changed while players are online**: new limits apply to new logins and new packets. Nobody is disconnected
  by a limit change. A newly blocked address is disconnected (US3).
- **IPv6 and IPv4-mapped addresses**: they match the lists the same way as their IPv4 form.

## Requirements *(mandatory)*

### Functional Requirements

**Packet whitelist (US1)**
- **FR-001**: VFilter MUST forward only packet types on the allowed list for that server module (Gateway, Download,
  Agent) and direction. Every other packet is dropped.
- **FR-002**: VFilter MUST support an observe-only mode, per module, that logs what would be dropped without dropping
  it. [NEEDS CLARIFICATION: should a fresh install start in observe-only mode (safer for the first days with real
  players, but no protection) or enforcing (protected from day one, but a missing legitimate packet breaks that action
  until it's fixed)?]
- **FR-003**: For allowed packet types with a known shape, VFilter MUST check their size and structure, and drop
  packets that don't match.
- **FR-004**: Each dropped packet MUST add to the session's abuse score, weighted by severity. A session whose score
  passes the limit MUST be disconnected. The score decays over time, so occasional noise from a normal player never
  adds up to a disconnect.
- **FR-005**: Every drop and disconnect MUST be logged with: level `security`, module, address, account and character
  (when known), packet type and reason.

**Flood and connection limits (US2)**
- **FR-006**: VFilter MUST disconnect a session that exceeds the packets-per-second or bytes-per-second limit.
- **FR-007**: VFilter MUST refuse new connections from an address for a cool-off period when it exceeds the
  connections-per-minute limit or the unfinished-handshakes limit.
- **FR-008**: Refusals during a cool-off MUST be logged at most once per address per cool-off.

**Blocked addresses (US3)**
- **FR-009**: Connections from Blocked IPs (single addresses or ranges) MUST be closed before the handshake, on every
  module.
- **FR-010**: Sessions from a newly blocked address MUST be disconnected within 5 seconds of the block being saved.

**Address and cafe limits (US4)**
- **FR-011**: VFilter MUST refuse a login when the address already has the maximum number of logged-in players (global
  setting), with the game's "IP limit" login error.
- **FR-012**: Net Cafe addresses MUST use their own login limit from the website's cafe settings (0 = default cafe
  limit, -1 = unlimited). Limits for other areas (Fortress War, Job and so on) belong to the area-rules feature.
- **FR-013**: With "GM Exemption" on, Game Assistant accounts and players from Allowed GM addresses MUST be exempt from
  address limits.

**GM login security (US5)**
- **FR-014**: With "GM Login Security" on, a GM account MUST be refused at login unless it comes from an Allowed GM
  address. [NEEDS CLARIFICATION: how does VFilter know an account is a GM? (a) read the game's account database (GM
  flag on the account, read-only, refreshed every few minutes), (b) a "GM accounts" list that admins keep on the
  website, (c) both]
- **FR-015**: If the GM source is unavailable, VFilter MUST keep using the last known GM list, and MUST NOT treat
  accounts as non-GM because of the outage.

**Exploit rules (US6)**
- **FR-016**: VFilter MUST have named exploit rules. Each rule has an on/off setting, a severity (raise the score, or
  disconnect) and a test with a captured exploit packet.
- **FR-017**: The first rule set MUST cover the exploits documented in our reference research for vSRO 1.188. The plan
  lists them, each with its source.

**Performance and stability (US7, constitution III and V)**
- **FR-018**: With all protections on and 1,000 players, VFilter MUST add under 1 ms per packet at p99.
- **FR-019**: No protection may query the database per packet or per login. Everything is decided from the cached
  config and in-memory state.
- **FR-020**: A failure inside one protection MUST only affect that session (it is closed and logged), never other
  players or the server.

**Settings**
- **FR-021**: Every limit and switch MUST be a setting in the settings catalog, with a safe default:
  - **Settings**: observe-only per module, abuse limit and decay, packets and bytes per second, connections per minute,
    unfinished handshakes, cool-off minutes, IP limit, default cafe limit, GM Exemption, GM Login Security, and each
    exploit rule's switch.
  - **Pickup**: changes take effect within 2 seconds (the config poll), without a restart.

### Key Entities

- **Packet rule**: a module, a direction and a packet type, plus the allowed size range and structure check if any.
  Shipped with VFilter and versioned. It is not edited on the website.
- **Abuse score**: per session, a number that rises with offences and decays over time. When it passes the limit, the
  session is disconnected.
- **Address cool-off**: an address and the time its cool-off ends, with the reason. Kept in memory.
- **Exploit rule**: a name, a description, a severity, an on/off switch, and a captured test packet.
- **Security event**: a `filter_logs` row with level `security` (or category `flood`/`limit`/`gm`/`exploit`). It holds
  the reason, the packet type and the session identity.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A recorded normal play session (login, character select, moving, fighting, trading, chatting, stalls,
  pets) replays through VFilter with 0 packets dropped.
- **SC-002**: 100% of the injected bad packets in the test set (unknown type, wrong direction, wrong size, bad
  structure, each known exploit) are dropped and logged with the right reason.
- **SC-003**: A single flooding client is cut off within 2 seconds, and a normal player running at the same time sees
  no added delay beyond the budget.
- **SC-004**: A newly blocked address is disconnected within 5 seconds, and its new connections are refused before the
  handshake.
- **SC-005**: With 1,000 simulated players and all protections on, the added delay is under 1 ms per packet at p99.
- **SC-006**: Over a 24-hour test with normal players, no normal player is disconnected by the abuse score or the
  flood limits.
- **SC-007**: Every block shows up on the website's Logs page within 1 minute, with a reason an admin can understand.

## Assumptions

- The protocol knowledge comes from our references: AegisCore-SRO (opcode registry, packet layouts, captures) and
  silkroad-docs. GPL sources are read, never copied.
- The allowed packet list ships inside VFilter. It is not edited on the website. A missing packet is fixed in a VFilter
  release.
- Login error codes (IP limit, blocked, GM refusal) use the vSRO 1.188 login failure codes already used by feature 002.
  The exact message each shows in the client is checked during the real-client test.
- Defaults (adjustable on the website):
  - abuse limit: disconnect after about 5 severe offences
  - 300 packets/s and 64 KiB/s per session
  - 30 connections/min per address
  - 5 unfinished handshakes per address
  - 10-minute cool-off
  - 3 players per address
  - default cafe limit 10
- Game Assistant accounts and Allowed GM addresses are the website lists from feature 005.
- HWID/PC-based limits, captcha, PIN, VPN/proxy detection and virtualisation blocking are out of scope. HWID needs the
  later client-side component.
- Area rules, cooldowns and region PC/IP limits are the next feature (rules engine).
