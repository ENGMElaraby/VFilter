# HANDOFF: VFilter (proxy filter)

**Date:** 2026-10-17 · **Status:** Spec Kit and project docs are set up. No C# code yet. The
website goes first (see `../VFilterWebsite/HANDOFF.md`), and this repo starts once the website's
feature 002 (contract schema) exists.

## What exists
| Path | What |
| --- | --- |
| `CLAUDE.md` | Project guide for Claude Code (stack, layout, commands, conventions) |
| `.specify/memory/constitution.md` | Project rules, v1.0.0 (deny-by-default, stability, perf budget) |
| `.specify/` + `.claude/skills/speckit-*` | GitHub Spec Kit (Claude integration, git extension) |
| `docs/shared-contract.md` | VFILTER DB contract. **Must stay identical to the website's copy.** |
| `docs/maxiguard-feature-map.md` | Features to match |

## Decisions made
- C# on the latest .NET LTS, running as a Windows Service next to the SRO server files.
- Proxy design: players → Guard IP/Port → VFilter → real Gateway/Agent/Download (hidden).
  Gateway login responses are rewritten so Agent redirects point at our Agent guard.
- Config comes from the VFILTER DB and is cached in memory, reloaded on `filter_config_version`
  change. Commands come from `filter_commands`, and the heartbeat goes to `filter_status`.

## Inputs needed from you before feature 001
1. Server files version (vSRO 1.188?) and the real IP/port of the Gateway, Download and each Agent.
2. Machine layout: does the filter run on the same Windows box as the modules?
3. A packet capture or two (optional but very useful) for test fixtures. The filter can record
   these itself once 001 works.

## Step 0: one-time setup on your Mac
```bash
cd /Users/mohammedelaraby/Work/Silkroad/Filter/VFilter
dotnet --version            # install the latest LTS SDK if missing
dotnet new sln -n VFilter
for p in Host Core Security Packets Rules Data; do
  t=classlib; [ $p = Host ] && t=worker
  dotnet new $t -o src/VFilter.$p && dotnet sln add src/VFilter.$p
done
for p in Security Packets Rules Core; do
  dotnet new xunit -o tests/VFilter.$p.Tests && dotnet sln add tests/VFilter.$p.Tests
done
dotnet build && git add -A && git commit -m "Scaffold solution" && git push -u origin main
```

## Step 1: features with Spec Kit (open Claude Code in this folder)
**001-proxy-core**
```
/speckit-specify Proxy core for vSRO 1.188: listen on guard ports for Gateway, Download and 1-5 Agents from filter_modules; implement the Silkroad security handshake, Blowfish and packet framing in VFilter.Security; forward packets both ways; rewrite the Gateway login/agent-redirect response to our guard IP/port; per-session state; graceful start/stop. Success = a real client logs in and plays through the filter.
```
**002-control-and-telemetry**
```
/speckit-specify Connect the filter to the VFILTER DB per docs/shared-contract.md: config cache with per-area filter_config_version polling (2s, reload only changed areas), filter_commands processing (start/stop/restart/set_gateway_mode/reload/kick/notice/ban; claim each with UPDATE ... WHERE status='pending' and skip cancelled rows, contract 0.3.0), filter_status heartbeat every 5s, filter_sessions, and batched filter_logs. Gateway modes gm/inspection/public. Keeps running on last good config if the DB is down.
```
**003-security**
```
/speckit-specify Deny-by-default packet validation (opcode whitelist per module and direction, size checks), known vSRO exploit blocks, flood/rate limits per session and IP, blocked IPs/HWIDs, allowed GM IPs and GM login security, IP and HWID/PC limits (global, cafe, per area), with every block logged as security.
```
**004-rules-engine**
```
/speckit-specify Rules engine in VFilter.Rules for filter_area_rules, filter_cooldowns and conditionals: block skills/items/teleports/party/pets/PK/berserk/trace/stall/exchange per area and per job/FW state, with player messages from the Messages settings.
```
Later: triggers (SQL procs), the client DLL and its custom-window protocol (daily reward, lucky
spin, battle pass, macro), and the GameServer-side features.
After the client DLL: **Silk Stall** (stalls priced in silk), planned in `docs/features/silk-stall.md`
with a draft `/speckit-specify` prompt.
