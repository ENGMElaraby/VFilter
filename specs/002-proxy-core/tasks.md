---
description: "Tasks for VFilter 002 Proxy Core"
---
# Tasks: Proxy Core

- [ ] T001 Create src/VFilter.Security + tests/VFilter.Security.Tests; port MElarabyBot security files (namespace VFilter.Security, provenance header) and their tests; BouncyCastle.Cryptography
- [ ] T002 [US1] Proxy: IPacketInspector, ProxySession (two securities, pumps, ordered flush, 4 MB bound, handshake/idle timeouts, close both sides), GuardListener, ProxyModuleRunner : IModuleRunner (start/stop listeners, counts, close sessions on stop)
- [ ] T003 [US1] Tests: loopback FakeModule (server-role) + FakeClient (client-role) — handshake both sides, encrypted + plain + massive packets forwarded unchanged both ways; bad count/CRC closes only that session; module down closes client; listener port in use → module error
- [ ] T004 [US1] GatewayInspector: rewrite 0xA102 success agent ip/port → guard of matching agent module; unknown address → block + close + security log; tests
- [ ] T005 [US2] Gateway modes on 0x6102: GM / inspection refusal from settings codes; allowed IPs incl. CIDR via IpRange; refusal answered by filter; login log; tests per mode
- [ ] T006 [US3] Session identity: register on accept, username on 0x6102 / 0x6103, charname on 0x7001; kick closes both sides; counts (connections, agents online = with charname); tests
- [ ] T007 Wire ProxyModuleRunner in Host instead of StandbyModuleRunner; latency test (200 sessions, p99 < 1 ms)
- [ ] T008 Docs: HANDOFF, CLAUDE.md, settings keys note; full build/test; commit, PR
