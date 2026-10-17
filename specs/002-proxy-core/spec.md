# Feature Specification: Proxy Core

**Feature Branch**: `002-proxy-core`

**Created**: 2026-10-17

**Status**: Draft

**Input**: User description: "Proxy core for vSRO 1.188 implementing IModuleRunner: listen on guard IP/port per enabled module; Silkroad security handshake, Blowfish and packet framing in VFilter.Security; forward packets both ways; rewrite the Gateway login/agent-redirect response to our guard IP/port; register sessions in SessionRegistry (username/charname on login) and honour kick; per-module connection/online counts; gateway modes gm/inspection/public at login. Success = a real client logs in and plays through the filter."

## Overview

After feature 001 the filter is controlled by the website, but it carries no traffic. This feature makes
it a real proxy. Players connect to the public guard address of each module. The filter completes the
Silkroad security handshake with the player and, separately, with the real module. It then forwards
every packet in both directions, decrypting and re-encrypting as each side needs. Players never learn the
real module addresses: when the Gateway tells a client which Agent to connect to, the filter replaces
that address with the Agent's guard address.

The gateway modes from the website now take effect at login:
- **GM**: only allowed IPs may log in.
- **Inspection**: everyone gets the inspection message.
- **Public**: everyone may log in.

The real module addresses come from the website's Modules page (`filter_modules`). Nothing is
hard-coded.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A player logs in and plays through the filter (Priority: P1)

A player runs the game client pointed at the Gateway guard address. They see the server list, log in,
and are handed to an Agent. That Agent is the filter's Agent guard address, not the real one. They then
pick a character and play normally. Everything they do reaches the real servers, and everything the
servers send reaches them.

**Why this priority**: This is what a proxy filter is. Every later security and rule feature inspects
the traffic this feature carries.

**Independent Test**: Run in-process fake Gateway and Agent modules that speak the Silkroad security
protocol. Point a test client at the guard ports. The client completes both handshakes and logs in.
It receives a redirect containing the Agent guard address (never the real one), connects there, and
exchanges encrypted and plain packets both ways, byte-for-byte equal after decoding.

**Acceptance Scenarios**:

1. **Given** the Gateway, Download and Agent modules are enabled with real and guard addresses, **When** the filter starts them, **Then** it listens on each guard address and port, and the dashboard shows them running.
2. **Given** a client connects to a guard port, **When** the connection opens, **Then** the filter connects to the real module, completes the security handshake with both sides, and forwards packets both ways.
3. **Given** the Gateway answers a successful login with the real Agent's address, **When** the answer passes through the filter, **Then** the client receives the matching Agent's guard address and port instead.
4. **Given** the login answer names an address that is not a configured Agent, **When** it passes through, **Then** the filter blocks it, closes the session, and logs a `security` event (real addresses never leak).
5. **Given** a packet from the client has a wrong security count or checksum, or cannot be decrypted, **When** it arrives, **Then** the session is closed and a `security` event is logged. Nothing is forwarded.
6. **Given** the real module is down, **When** a client connects, **Then** the client is disconnected promptly and an `error` event is logged. The module keeps listening.

---

### User Story 2 - Gateway modes decide who may log in (Priority: P1)

The admin switches the gateway mode in the website.
- **GM**: only clients whose IP is in Allowed GM IPs (single addresses or ranges) can log in. Everyone else gets a refusal.
- **Inspection**: everyone except allowed GM IPs gets the inspection refusal.
- **Public**: everyone may log in.

**Why this priority**: Gateway modes are the first website control that changes what players see, and
they are needed for every maintenance window.

**Independent Test**: In each mode, send login requests from an allowed IP and from another IP. Check
which ones reach the Gateway and which get the refusal response straight from the filter.

**Acceptance Scenarios**:

1. **Given** GM mode, **When** a login comes from an IP inside Allowed GM IPs, **Then** it is forwarded.
2. **Given** GM mode, **When** a login comes from another IP, **Then** the filter answers it with a login refusal (configurable code, default "server unavailable") and does not forward it.
3. **Given** Inspection mode, **When** a login comes from an IP outside Allowed GM IPs, **Then** it gets the inspection refusal.
4. **Given** Public mode, **When** any login comes in, **Then** it is forwarded.
5. **Given** the admin switches the mode, **When** the next login arrives, **Then** the new mode applies. Players already logged in are not affected.

---

### User Story 3 - Sessions, counts and kick (Priority: P1)

Every connection appears in the website's Sessions page with its module and IP. Once known, the username
(from the Gateway login) and the character name (from character selection on the Agent) are added. The
dashboard shows each module's connection count and the number of players in game. An admin's Kick
disconnects the player at once.

**Why this priority**: These are the website's Sessions page and dashboard counts. Kick must actually
work now that real players can connect.

**Independent Test**: Connect two test clients and log one in through the Gateway and Agent. Check the
session registry entries with username, the module counts, and that a kick command closes the right
connection.

**Acceptance Scenarios**:

1. **Given** a client connects, **When** the connection opens, **Then** a session with module and IP is registered and shows in the Sessions page within 2 seconds.
2. **Given** the client logs in on the Gateway, **When** the login request passes, **Then** the session gets the username. The Agent session for the same player is linked to that username through the login token in the Agent's authentication request.
3. **Given** the player selects a character, **When** the selection passes, **Then** the session gets the character name.
4. **Given** clients are connected, **When** the heartbeat runs, **Then** each module reports its open connections. Agents report players in game (sessions with a character).
5. **Given** an admin kicks a player by character, username or IP, **When** the filter processes it, **Then** both sides of the matching connection close, and the command reports how many sessions were closed.
6. **Given** a module is stopped or restarted from the website, **When** the command runs, **Then** its listener closes and its sessions are disconnected; on start it listens again.

---

### Edge Cases

- **Large server answers** are split into multi-part messages (0x600D). They are forwarded as they are, in order.
- **Slow clients**: the outbound queue to a client is bounded at 4 MB. A client that cannot keep up is disconnected instead of growing memory without limit.
- **Half-open connections**: if either side closes, the other side is closed too. If no data arrives from a client for 5 minutes, the session is closed.
- **Handshake timeout**: a client that does not finish the handshake within 10 seconds is disconnected.
- **Many connections from one IP**: per-IP and per-PC limits belong to 003-security. Here every connection is accepted and counted.
- **A guard port already in use**: that module goes to the error state with the reason. The other modules keep working.
- **Module addresses changed in the website**: the new addresses apply when the module is restarted (as in 001).

## Requirements *(mandatory)*

### Functional Requirements

**Security protocol (both roles)**

- **FR-001**: The filter MUST implement the Silkroad packet security as server toward clients and as client toward modules:
  - the 0x5000 handshake (initial Blowfish key, count/CRC seeds, Diffie-Hellman key exchange, signature check, final key)
  - Blowfish in Silkroad byte order
  - the per-packet count byte and checksum byte on client-to-server packets
  - the encrypted-flag framing (6-byte header, 15-bit size)
- **FR-002**: The filter MUST generate its own handshake values for each client from a cryptographically secure random source. It never reuses the module's values.
- **FR-003**: Handshake packets (0x5000, 0x9000) MUST be consumed on each side and never forwarded. All other packets are forwarded in order, keeping each packet's encrypted flag.
- **FR-004**: A client packet with a bad count, bad checksum, bad length or failed decryption MUST close the session and be logged as `security`. A packet over the frame limit MUST close the session.

**Proxying**

- **FR-005**: For each enabled module the filter MUST listen on its guard address and port when started, stop listening when stopped, and open one connection to the real module per client.
- **FR-006**: The Gateway's successful login answer MUST be rewritten so its Agent address and port become that Agent's guard address and port. An answer naming an unknown address MUST be blocked and the session closed.
- **FR-007**: The filter MUST NOT add per-packet database work. Sessions, counts and events go through the in-memory registry and log buffer from 001.

**Gateway modes**

- **FR-008**: For each Gateway login request the filter MUST apply the current gateway mode:
  - GM: forward only from Allowed GM IPs (address or range); refuse the rest.
  - Inspection: forward only from Allowed GM IPs; refuse the rest with the inspection code.
  - Public: forward everything.

  Refused logins are answered by the filter with a login failure and logged (`login` category). The refusal codes are filter settings (`gateway.gm_refusal_code`, `gateway.inspection_refusal_code`), defaulting to the "server unavailable" code until checked against the real client.

**Sessions**

- **FR-009**: Every connection MUST be registered in the session registry. The username is added from the Gateway login request, and the Agent session is linked by its login token. The character name is added from character selection.
- **FR-010**: Kick MUST close both sides of the matching connections.
- **FR-011**: Each module MUST report its open connection count. Agents MUST report the number of sessions with a character as players online.

**Robustness**

- **FR-012**: A failure in one session (bad packet, exception, module disconnect) MUST close only that session.
- **FR-013**: The filter MUST apply these limits: handshake timeout 10 s, client idle timeout 5 minutes, outbound buffer limit 4 MB per side, frame payload ≤ 32,767 bytes.

### Key Entities

- **Guard listener**: the public endpoint of one module.
- **Proxy session**: a client connection plus its module connection, with two security contexts, the session registry entry and its counters.
- **Security context**: the handshake state, ciphers, count and checksum state for one side of one connection.
- **Login token**: the number the Gateway gives the client for the Agent. The filter uses it to link the Agent session to the username.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A real vSRO 1.188 client logs in through the filter, reaches the character screen, and plays. To be confirmed on the real server (the test plan's manual step).
- **SC-002**: In automated tests, 100% of forwarded packets arrive unchanged in content and order in both directions, including encrypted packets and multi-part answers.
- **SC-003**: A client never receives a real module address, verified for every login answer in tests.
- **SC-004**: Added latency per packet is under 1 ms at the 99th percentile with 200 concurrent test sessions on a developer machine.
- **SC-005**: A malformed or forged client packet closes only its own session, 100% of the time in tests.
- **SC-006**: Kick closes the player's connection within 2 seconds of the website queuing it.

## Assumptions

- The protocol follows vSRO 1.188 as implemented in AegisCore-SRO (`framework/security`, `framework/network`, `gateway-server`). The Silkroad security algorithm is ported to C#, not copied from GPL sources.
- Real module and guard addresses are entered by the operator on the website's Modules page. The owner will confirm real IPs and ports there for the manual test.
- The login failure codes for the GM/Inspection refusals are configurable because their exact client messages must be checked with the real client. Default code 6 is "server unavailable/full".
- Per-IP/HWID limits, opcode whitelists and exploit blocks are 003-security. In-game rules are 004.
- The Agent authentication request (0x6103) carries the login token from the Gateway answer. The character selection request carries the character name.
