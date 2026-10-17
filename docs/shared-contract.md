# VFilter Shared Contract (Website ⇄ Filter)

**Version:** 0.3.3 (draft) · **Owner:** both repos · **Last updated:** 2026-10-17

The website (VFilterWebsite, Laravel) and the filter (VFilter, C#) never talk to each other directly.
They share **one SQL Server database, `VFILTER`**, that sits next to the game databases.

```
 Players ──► VFilter (proxy) ──► GatewayServer / AgentServer / DownloadServer
                 │  ▲
     reads config│  │writes status, logs, sessions
                 ▼  │
             SQL Server: VFILTER  ◄──── VFilterWebsite (admin panel)
                 │                       reads/writes config, queues commands,
                 │                       reads logs/status
     SRO_VT_ACCOUNT / SRO_VT_SHARD / SRO_VT_LOG (read by both; written only by
     the filter's trigger procedures and explicit admin actions)
```

This file is copied into both repos. **Any change here must be made in both repos in the same
change set, with the version bumped** (MAJOR = breaking column/semantic change, MINOR = new
table/column/setting, PATCH = wording).

## Rules

1. The website owns the schema. Migrations live in VFilterWebsite (`database/migrations`, connection `filter`).
2. The filter never alters schema. It only reads config tables and writes to `filter_status`,
   `filter_logs`, `filter_sessions`, and `filter_commands.status/result/processed_at`. The website
   writes new `filter_commands` rows, and may change a command's `status` only from `pending` to
   `cancelled` (with `result` and `processed_at`).
3. All times are stored in UTC (`datetime2(3)`). Text columns are `nvarchar` (Unicode). No foreign keys.
4. Config changes are picked up via `filter_config_version`, one row per configuration area
   (`settings`, `lists`, `area_rules`, `cooldowns`, `modules`, `ga_permissions`). The website
   increments the counter of each area it changed, in the same transaction as the change. The filter
   polls all counters every 2 s and reloads only the areas whose counter changed.
5. Area values follow MaxiGuard's convention: a Region ID, a World ID, or a ContinentName.
   `-1` as a region means "everywhere".

## Tables

Exact definitions (SQL Server types). `nvarchar(max)` holds JSON where noted.

### filter_modules  (area `modules`)
| column | type | null | default | notes |
| --- | --- | --- | --- | --- |
| id | int identity PK | no | | |
| type | nvarchar(16) | no | | `gateway` \| `agent` \| `download` · index |
| name | nvarchar(32) | no | | unique · `Gateway`, `Download`, `Agent1`..`Agent5` |
| module_ip | nvarchar(64) | no | | real address, never shown to players |
| module_port | int | no | | 1–65535 |
| guard_ip | nvarchar(64) | no | | public address |
| guard_port | int | no | | 1–65535 |
| enabled | bit | no | 0 | |

Limits: exactly 1 gateway, 1 download, 1–5 agents (`Agent1`… in order, no gaps).

### filter_settings  (area `settings`)
| column | type | null | default | notes |
| --- | --- | --- | --- | --- |
| key | nvarchar(128) PK | no | | dotted, e.g. `logs.retention_days` |
| value | nvarchar(max) | no | | string form; `bool` stored `1`/`0` |
| value_type | nvarchar(16) | no | | `bool` \| `int` \| `string` \| `json` \| `color` |
| group | nvarchar(32) | no | | tab name · index |
| requires_restart | nvarchar(16) | no | `none` | `none` \| `filter` \| `gameserver` \| `client` |
| updated_at | datetime2(3) | yes | | |
| updated_by | int | yes | | panel user id (no FK) |

The canonical key list lives in `docs/settings-catalog.md` (feature 006).

### filter_lists  (area `lists`)
| column | type | null | notes |
| --- | --- | --- | --- |
| id | bigint identity PK | no | |
| list | nvarchar(32) | no | `allowed_ips`, `blocked_hwids`, … |
| value | nvarchar(256) | no | |
| comment | nvarchar(256) | yes | |
Indexes: unique `(list, value)`.

List names: `allowed_ips`, `proxy_bypass_ips`, `proxy_bypass_usernames`, `blocked_ips`, `blocked_hwids`,
`net_cafe_ips`, `blocked_words`. `allowed_ips`, `blocked_ips` and `proxy_bypass_ips` hold an IPv4/IPv6 address or a
CIDR range in canonical form (`1.2.3.0/24`, `2001:db8::/64`); the filter matches by range. `net_cafe_ips` holds
single addresses. Usernames, HWIDs and words compare case-insensitively.

### filter_area_rules  (area `area_rules`)
| column | type | null | default |
| --- | --- | --- | --- |
| id | bigint identity PK | no | |
| rule | nvarchar(48) | no | |
| area | nvarchar(64) | no | | Region/World/ContinentName/`-1` |
| target | nvarchar(128) | yes | |
| params | nvarchar(max) | yes | | JSON |
| enabled | bit | no | 1 |
Indexes: `(rule, enabled)`, `(area)`.

`cafe_limit`: area `-1`, target = a `net_cafe_ips` address, params
`{"login_ip","login_pc","fw_ip","fw_pc","job_ip","job_pc","fgw_ip","fgw_pc"}` as ints (0 = default cafe limit,
-1 = unlimited, max 1000).

### filter_cooldowns  (area `cooldowns`)
`action nvarchar(32) PK`, `seconds int not null default 0` (0–32000). Actions: `reverse`, `reverse_job`,
`global`, `exit`, `exchange`, `stall`, `ride_pet`, `grab_pet`, `all_chat`, `all_chat_safe`, `teleport`,
`teleport_job`, `berserk_job`, `berserk_fw`, `res_scroll`, `res_scroll_job`, `friend_request`.

### filter_ga_permissions  (area `ga_permissions`)
`username nvarchar(32) PK` + `bit not null default 0` for each of: `make_item`, `zoe`, `load_monster`,
`mobkill0`, `mobkill1`, `notice`, `filter_cmd`, `invisible`, `invincible`, `ban`, `recall_user`,
`recall_guild`, `move_to_user`, `move_to_npc`, `to_town`, `go_town`, `reset_q`, `wp`, `gm_skill`,
`world_status`.

### filter_commands  (website writes new rows; filter writes status/result/processed_at)
| column | type | null | default |
| --- | --- | --- | --- |
| id | bigint identity PK | no | |
| command | nvarchar(32) | no | | `start`, `stop`, `restart`, `set_gateway_mode`, `reload`, `kick`, `notice`, `ban` |
| target | nvarchar(64) | no | | `all`, `gateway`, `download`, `agent1`…`agent5` (lower-case module name), or username/charname |
| payload | nvarchar(max) | yes | | JSON |
| status | nvarchar(16) | no | `pending` | `pending` → `running` → `done` \| `failed`; or `pending` → `cancelled` (website) |
| result | nvarchar(max) | yes | |
| created_by | int | no | | panel user id |
| created_at | datetime2(3) | no | |
| processed_at | datetime2(3) | yes | |
Indexes: `(status, created_at)`. (`target` widened 32 → 64 so character names fit.)

**Claiming and cancelling.** The filter claims a command with a conditional update
(`UPDATE … SET status='running' WHERE id=@id AND status='pending'`) and runs it only if one row
changed. The website cancels with the mirror statement (`… SET status='cancelled' … AND
status='pending'`), so a command is either run or cancelled, never both. The filter ignores
`cancelled` rows. The website queues at most one pending/running `start`/`stop`/`restart` per target and
one `set_gateway_mode` at a time. `set_gateway_mode` payload: `{"mode":"gm"|"inspection"|"public"}`.

**Kick and ban payloads.** `kick`: target = charname, else username, else IP; payload
`{"session_id":123,"ip":"1.2.3.4","reason":null}`. `ban`: target = username (game account); payload
`{"session_id":123,"reason":"...","duration_minutes":1440}` (`null` = permanent). The filter bans the account
and disconnects it. The website queues at most one pending/running kick/ban per target.

**Filter behaviour.** The filter marks a command `failed` with result `expired` instead of running it when it
claims it more than 10 minutes after `created_at` (database clock). `notice` is `failed: not supported yet` until
the proxy can send notices; `ban` is `failed: account ban not supported yet` (after disconnecting the player) until
the account-ban feature exists. `heartbeat_at`, `started_at`, `processed_at` and session times are written with the
database clock (`SYSUTCDATETIME()`).

Gateway modes: `gm` (only allowed IPs; others see "Check"), `inspection` (others get inspection
error), `public`. The filter starts in `gm` unless `server.start_in_inspection` is true.

### filter_status  (filter writes)
| column | type | null | default |
| --- | --- | --- | --- |
| module | nvarchar(32) PK | no | |
| state | nvarchar(16) | no | `stopped` | `stopped`/`starting`/`running`/`error` |
| online_count | int | no | 0 |
| connections | int | no | 0 |
| gateway_mode | nvarchar(16) | yes | | `gm`/`inspection`/`public` (gateway only) |
| version | nvarchar(32) | yes | | filter build version |
| started_at | datetime2(3) | yes | |
| heartbeat_at | datetime2(3) | yes | | > 15 s old = offline |
| last_error | nvarchar(max) | yes | |

### filter_sessions  (filter writes)
| column | type | null |
| --- | --- | --- |
| id | bigint identity PK | no |
| module | nvarchar(32) | no |
| ip | nvarchar(45) | no |
| hwid | nvarchar(64) | yes |
| username | nvarchar(32) | yes |
| charname | nvarchar(64) | yes |
| connected_at | datetime2(3) | no |
| last_packet_at | datetime2(3) | yes |
Indexes: `(module)`, `(username)`, `(ip)`.

### filter_logs  (filter writes)
| column | type | null |
| --- | --- | --- |
| id | bigint identity PK | no |
| ts | datetime2(3) | no |
| level | nvarchar(8) | no |
| module | nvarchar(32) | no |
| category | nvarchar(32) | no |
| ip | nvarchar(45) | yes |
| hwid | nvarchar(64) | yes |
| username | nvarchar(32) | yes |
| charname | nvarchar(64) | yes |
| opcode | nvarchar(8) | yes |
| message | nvarchar(1024) | no |
| data | nvarchar(max) | yes |
Indexes: `(ts)`, `(category, ts)`, `(username)`, `(ip)`. Retention via `logs.retention_days` (website job prunes).

### filter_config_version  (one row per configuration area)
`area nvarchar(16) PK` (`settings`, `lists`, `area_rules`, `cooldowns`, `modules`, `ga_permissions`),
`version bigint not null default 0`, `updated_at datetime2(3) null`.

### Website-only tables (also in VFILTER)
`users` (panel admins: role `owner` \| `admin` \| `viewer`), `password_reset_tokens`, `sessions`,
`audit_log` (who changed which setting, old → new value). Created by the website; the filter ignores them.
Their columns are defined by the website in `VFilterWebsite/specs/001-auth-and-shell/data-model.md`
(the filter never reads them, so they are not repeated here).

## Changelog
- 0.3.3 (2026-10-17, PATCH): command expiry, interim notice/ban results, database-clock timestamps (VFilter 001).
- 0.3.2 (2026-10-17, PATCH): list names and formats (CIDR), `cafe_limit` rule (feature 005).
- 0.3.1 (2026-10-17, PATCH): kick/ban payload shapes (feature 004).
- 0.3.0 (2026-10-17, MINOR): `filter_commands.status` gains `cancelled` (website, only from
  `pending`); claim/cancel rule; `download` listed as a target; `set_gateway_mode` payload shape
  (feature 003).
- 0.2.0 (2026-10-17, MINOR): change counter per configuration area; exact types, nullability,
  defaults and indexes for every table; `nvarchar` everywhere; `filter_commands.target` → nvarchar(64)
  (feature 002).
- 0.1.1 (2026-10-17, PATCH): website-only tables now point to their column definitions (feature 001).
- 0.1.0 (2026-10-17): first draft.
