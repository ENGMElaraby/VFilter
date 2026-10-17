# MaxiGuard Feature Map

Captured 2026-10-17 from the MaxiGuard panel (maxiguard.org/panel), all 50 pages. This is the
reference for what VFilter + VFilterWebsite must match. Live doc:
https://claude.ai/code/artifact/946e992b-c6f0-40bb-8044-be16db5497e3

## Overview
MaxiGuard is a hosted vSRO proxy filter plus a client DLL and a GameServer DLL, configured from one
web panel (50 pages). Players connect to the Guard IP/Port, and the real Gateway/Agent/Download
modules stay hidden. Settings live in a `MaxiGuard_User` DB next to the Account/Shard/Log DBs. Many
features call SQL trigger procedures (`_EDIT` procs). An area is a Region ID, a World ID or a ContinentName.

| Area | Pages | Covers |
| --- | --- | --- |
| Connection | 3 | Dashboard, SQL connection, module IP/ports |
| Security | 4 | GM/allowed IPs, Game Assistants, blocked IPs, blocked HWIDs |
| In-game settings | 37 | restrictions, events, colors, UI windows, triggers, Other Settings (~300 toggles, 38 tabs) |
| Server tools | 3 | Notifications, Server Observation, Server Management |
| Account | 3 | License, downloads, help |

## Core and connection
**Dashboard (Filter Control)**
- Start / Restart / Stop for Gateway, Agent, or All, with live filter status.
- Gateway modes: **GM** (only allowed IPs log in; others see "Check"), **Inspection** (others get an
  inspection error), **Public**. Starts in GM by default, or in Inspection if that's configured.
- Server status: gateway state, online player count.

**SQL Settings**: server IP/port, user, password, Account/Shard/Log DB names. All three connections
are validated before saving. Needs TCP/IP and a fixed port. Has a "Refresh cache" button.

**Filter IP/Port (modules)**: add Gateway / Agent / Download with the real IP and port. Exactly
1 Gateway, 1 Download, and up to 5 Agents (Agent1… in order). Each module gets a public Guard
IP/DNS and port, and only the Gateway Guard IP/Port goes in the client. Also: custom domain, and a
list of firewall IPs to allow.

## Security
| Page | What it does |
| --- | --- |
| Allowed IPs (GM) | IPs allowed in GM mode. With GM Exemption they skip IP/PC limits and in-game restrictions. With GM Login Security, only these IPs can log into GM accounts. `//` comments are allowed. |
| Proxy bypass | IPs and usernames exempt from VPN/proxy protection |
| Game Assistants | Usernames exempt from IP/HWID limits, with a per-account grid of GM commands: MakeItem, Zoe, LoadMonster, Mobkill 0/1, Notice, filter cmd, Invisible, Invincible, Ban, RecallUser, RecallGuild, MoveToUser, MoveToNpc, ToTown, GoTown, ResetQ, Wp, GMSkill, WorldStatus. Live. |
| Blocked IPs | Refused at login. Live. |
| Blocked HWIDs | Manual HWID bans, refused at login. Live. |
| Net Cafe IPs | Cafe IP limit, plus per-IP special limits for Login/FW/Job/FGW (IP and PC each) and per-region limits. 0 = default, -1 = unlimited. |
| Region PC/IP Limits | PC and IP caps per area. Checked before teleport/reverse; the player is disconnected if entry can't be prevented. |

Login options: GM login security, auto image captcha (+ charset), enforced PIN, proxy protection
(None/Simple/Medium/Extreme/SQL log), block CPU virtualisation, global IP / HWID-PC / cafe limits.

## In-game restrictions
| Page | Settings |
| --- | --- |
| Cooldowns (s, 0–32000) | Reverse, Reverse(Job), Global, Exit/Restart, Exchange, Stall, Ride pet, Grab pet, All chat, All chat (safe), Teleport, Teleport(Job), Berserk(Job), Berserk(FW), Res scroll, Res scroll(Job), Friend request |
| Messages | Every filter message editable; reset to EN/TR |
| Abuse filter | Blocked words in All chat, Global, Party Matching |
| Teleports | Blocked target teleport IDs (_RefTeleport) per group: Chinese, European, Trader, Thief, Non-Job, Special/Event, Premium |
| Trace | Trace-blocked areas |
| Party (GS restart) | Blocked, no party mobs, no ban, no leave, no invite, Party Matching blocked, auto-leave, guild/union/team-only invite areas |
| Pets (GS restart) | All pets or by type (grab/attack/trans/ride) off per area; auto-unsummon |
| Skills | Block per area (1 = auto attack, -1 = everywhere); buff-only event regions (cmd 59) + extra buff list; cooldown-check exempt regions; blocked in Job / in FW; only-in regions; regions allowing only certain skills; Trader/Hunter-only, Thief-only; auto-attack always-allowed areas |
| Stall | Stall region whitelist |
| Alchemy (GS restart) | Instant-alchemy regions |
| Berserk | Off; off in Job |
| PK (GS restart) | PK disabled areas (-1 = all) |
| Items (GS restart) | Block per area; blocked in Job; safe-zone-only; blocked in safe zones; only-in regions; regions allowing only certain items |
| Resurrection | Revive in town (PvP); get up in place; revive cooldown per area |
| Reverse | Reverse-blocked areas; extra worlds for move-to-party / save location |
| PVP-Mode | Auto yellow PvP; hidden name/guild/title; auto event suits; PvP cape blocked; All chat blocked; gear swap blocked; Exit blocked; Exchange blocked; minimap hidden; Return-to-FW blocked; hidden visual-effect skills; GM name reveal |
| Town on death | Auto return to town |
| Ignore defense | Uniques immune to ignore-defense avatars |
| Premium | Enable, premium skill IDs, block FGW/Arena/CTF/Magic POP/trade goods without premium, preferred access + threshold |
| Monsters | Fortress-style HP bar mobs; 1-damage-only mobs |
| Conditionals | IF level/race/gender/STR/INT/region/world (op: > >= == < <= != true false) THEN block skill / mastery up / item (ID, codename, type) / teleport / berserk / trace / movement / stall |

## Visuals, events, UI, triggers
| Page | Settings |
| --- | --- |
| Title colors | Color per Hwan level; forbidden custom-title words |
| Chat colors | Notice, GM all chat, PM, guild, union, academy, plus notice, ignore notify, mini event schedule, orange notice bg/text |
| Global colors | Color per global item ID; cooldown-exempt global items |
| Unique notices | Spawn/kill message + color per unique (`{KillerName}`) |
| Unique points | Points per unique (ranking window) |
| DPS meter | DPS uniques; excluded from history; live DPS uniques + per-unique speed (`MOB_X=300`) |
| NPC settings | Add repair / avatar blue / storage / guild / trader / hunter / thief to NPCs |
| Character icons | 16×16 .ddj icons from Media.pk2, assigned by ID |
| Event schedule | Visual in-game schedule, or `_RefEventSchedule` |
| Event zones | World ID as Team (2–4, `_CurrentEventTeamList`) / Party / Guild / Union event: suits, hidden names, name colors, no friendly damage |
| Changelog | In-game changelog text (`$$$` green, `---` orange, `>>>` pink) |
| Discord | Rich Presence: client ID, image keys/texts, show char/guild/race |

**Triggers** (each toggleable, calls a SQL proc): Arena reward/register, CTF/register, unique
kill/spawn (Shard+GS), job EXP, job suit equip/remove, job join/leave, teleport, spawn complete,
chat, custom action, trade goods buy/sell, loading screen, reverse return, get-up, quest receive,
DPS meter, kill log, party matching/joined/left/formed, custom title, dungeon stone, alchemy
plus/fail, global chat, item mall (+detailed), GS/Shard startup, FW register, extra icon, skill
scroll, job package, user login, silk/hour, stall open/modify/close, NPC buy/sell request/complete,
body state, skill up/down; planned Bridge/GS commands.

## Other Settings (38 tabs)
| Tab | Settings |
| --- | --- |
| Login | Server name, fake %, max capacity, GM login security, captcha + charset, show online/capacity, enforce PIN, beta random ID/PW, proxy protection, block virtualisation |
| Server General | Level cap, mastery cap, time zone, CH only, no PK drop, max chars/account, Snow Shield INT limit, disable item mall, NPC return-to-death max lv, start in inspection, disable firewall block, macro, equalized, item lock mail, advanced macro, hash system, GoldenExtraData |
| Limits | IP, HWID/PC, cafe IP, cafe PC exemption |
| GM | Auto invisible, no +8 limit in GM console, exemption for GMs/IPs |
| CTF | Min lv, IP/HWID limit, kill/win reward codename + amount, min participants, no trophy, period |
| Battle Arena | Min lv, IP/HWID limit, reward codename, guild/job + random/party win/lose amounts, AFK threshold, min participants |
| Magic POP | No random plus/blues/stats, ease |
| Fortress War | No res/trace/berserk/potion/pets, min/max tax, IP/HWID limit, Constantinople pulleys, kill counter, chat restriction off, min guild members, union changes, hide char/flag names |
| Guild | Max players per level 1–5, max union guilds, block GP donation, union chat limit, no penalty, grant-name min lv, guild in job, no GP from mobs, more storage, recall outside town |
| Job | Block phBot auto trade, trade captcha (charset, distortion, early), IP/HWID limit, disable trace/exchange/reverse/res/berserk/potion, job↔non-job trace + PK prevention, max job lv, no suit in auto-PvP, disable trade goods, no NPC thieves, no penalty, thieves can't pick in town, cave AP min lv, destroy pet on quit, titles in job, no asterisk, block pet termination in town, no job gold/EXP, no ground click with pet, fixed thief count, alias price multiplier |
| Dynamic Rank | V2, window, tabs Unique/Job/Honor/Custom1–5 (+titles), auto-fill job rank, hide disabled |
| Global Chat | Disable, min lv, colors, URLs, item linking + item IDs |
| Alchemy | Max plus, max devil plus, no adv. elixirs, astral fallback, plus notice (lv, links, degree, SOX, adv, devil), immortal after +5 SOX, permanent materials, no elixir/stone fusing, no elements on dismantle, 0% assimilation, socket min degree, instant fuse |
| Oldschool | Old EXP bar, login, alchemy, tooltip, sidebar, cape; no new PvP effects |
| Academy | Disable, allow/auto establishment, graduation lv, title length |
| Bugfixes | No thunder noise, skill cooldown check, purification pill cooldown, exchange quick-attack fix, TimedJob seconds |
| Uniques | Notice on GM kill; STR/INT, EU/CH, job, level-diff protection; no repair-invalid drops |
| Client | Zoom hack, DLL English, title, chat restriction off, tooltip width, nude patch, avatar/item blue limit off |
| Server Files | Silence GS errors/dumps, bypass NPC plus limit, new GS command system |
| Features | Mob HP %, cooldown ping, auto sort (inv/storage/pet), auto stat, item compare, unique target, AFK bubble + timer, buff bar space, ignore-damage notifier, no durability, no pet starvation, 2nd quick bar, auto mastery/skill, improved auto potion, improved pet filter + max degree |
| Event | Auto-PvP hide names/titles/guilds, suits, no pets, no all chat; kill counter zones; hidden names per event type; equalize looks/pluses/character |
| Stall / Exchange | Gold limits, stall in job, stall network, none in battlefield, block exchange if target in combat |
| Reverse | Min lv, no reverse to non-res areas, special reverse + item IDs |
| DPS Meter | Meter, party, extended, proximity, FW heart (per char), extended history, live speed |
| Party / Pets / FGW | Matching count, party-mob min members + chance, keep party when full, see members, matching visuals; fellow pet fixes, spawn in combat; daily FGW IP/PC, common limit |
| In-game UI | WebView2, icon manager, title manager + color picker (silk price/type), grant name, unique history, event schedule, changelog (on login), account lock, chest, achievements, event register, main menu icon, block tag remove, mini schedule |
| URL | Discord, Facebook, website |
| Silk/Hour | Enable, amount, PC/IP limit, interval, min lv, silk type |
| Title/Tag | Custom titles (Hwan ID, scroll codename), custom tags |
| Blocks | No drop in town, keep transferred buffs, no skill after emote, no exit in battlefield, no skill up in job, no exchange/exit/stall/teleport in combat, auto-attack max lv, socket abuse + period, skill up/down max lv, avatar/devil switch in town only |
| Weather | Rain, snow, intensity |
| Version | Client DLL / GS DLL version |
| Target Support | Cooldown, max consecutive; block if not same party/guild/union, not master, player/monster selected; excluded worlds |
| Other | Recall in non-res areas, default icons, PM sender name, SHA256 file hashes, settings V2, auto potion, smuggler caravan, **Battle Pass**, special offers, season rank, custom item data, macro (title, icon, disabled worlds, auto alchemy + skill), no auto party invite in town, equalized mastery count, CH single weapon, SMTP |

## Server tools and account
- Server Observation, Server Management (Manage Game): live lookup and game management. Fields load
  by script and weren't captured; capture them in a later spec.
- Downloads: client/GS DLLs (mandatory, optional, old).
- Account: license info, password, hosting location, static IP protection for the panel, top-up,
  purchase. **Not needed for us** (self-hosted, no license).
- Help/Contact: docs, FAQ.

## Our server specifics
Cap 100, Chinese race only (EU characters converted to CH), so EU-only options can default off or be
hidden. Planned custom windows: daily reward, lucky spin, battle pass, macro. Plus a website item mall.
