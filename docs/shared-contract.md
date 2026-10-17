# VFilter Shared Contract (Website ⇄ Filter)

**Version:** 0.1.1 (draft) · **Owner:** both repos · **Last updated:** 2026-10-17

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
   `filter_logs`, `filter_sessions`, and `filter_commands.status/result`.
3. All times are stored in UTC (`datetime2`).
4. Config changes are picked up via `filter_config_version`: the website increments it after every
   save; the filter polls it (every 2 s) and reloads only what changed.
5. Area values follow MaxiGuard's convention: a Region ID, a World ID, or a ContinentName.
   `-1` as a region means "everywhere".

## Tables

### filter_modules — proxy listeners
| column | type | notes |
| --- | --- | --- |
| id | int PK | |
| type | varchar(16) | `gateway` \| `agent` \| `download` |
| name | varchar(32) | `Gateway`, `Agent1`..`Agent5`, `Download` (order matters) |
| module_ip / module_port | varchar(64) / int | real server address (hidden from players) |
| guard_ip / guard_port | varchar(64) / int | public address players connect to |
| enabled | bit | |

Limits: exactly 1 gateway, 1 download, 1–5 agents.

### filter_settings — key/value switches (~300, mirrors MaxiGuard "Other Settings")
| column | type | notes |
| --- | --- | --- |
| key | varchar(128) PK | dotted, e.g. `login.captcha.enabled`, `limits.ip`, `job.disable_trace` |
| value | nvarchar(max) | stored as string; parsed by `value_type` |
| value_type | varchar(16) | `bool` \| `int` \| `string` \| `json` \| `color` |
| group | varchar(32) | tab name: `login`, `server`, `limits`, `gm`, `ctf`, `arena`, `fw`, `guild`, `job`, … |
| requires_restart | varchar(16) | `none` \| `filter` \| `gameserver` \| `client` |
| updated_at / updated_by | datetime2 / int | |

The canonical key list lives in `docs/settings-catalog.md` (to be generated in the first website spec).

### filter_lists — one-value-per-line lists
| column | type | notes |
| --- | --- | --- |
| id | bigint PK | |
| list | varchar(32) | `allowed_ips`, `proxy_bypass_ips`, `proxy_bypass_users`, `blocked_ips`, `blocked_hwids`, `blocked_words`, `forbidden_titles`, `cafe_ips`, … |
| value | nvarchar(256) | |
| comment | nvarchar(256) null | the `// note` part |

### filter_area_rules — every "do X / block X in area Y" rule
| column | type | notes |
| --- | --- | --- |
| id | bigint PK | |
| rule | varchar(48) | e.g. `skill.block`, `skill.only_in`, `item.block`, `party.block`, `pet.block.ride`, `pk.block`, `teleport.block`, `stall.allow`, `pvp.auto`, `name.hide`, `revive.town`, `limit.pc_ip`, … |
| area | varchar(64) | Region ID / World ID / ContinentName / `-1` |
| target | varchar(128) null | skill ID, item ID, teleport ID, codename… when the rule needs one |
| params | nvarchar(max) null | JSON, e.g. `{"pc":2,"ip":3}` or `{"cooldown":30}` |
| enabled | bit | |

### filter_cooldowns
`action varchar(32) PK` (`reverse`, `reverse_job`, `global`, `exit`, `exchange`, `stall`, `ride_pet`,
`grab_pet`, `all_chat`, `all_chat_safe`, `teleport`, `teleport_job`, `berserk_job`, `berserk_fw`,
`res_scroll`, `res_scroll_job`, `friend_request`), `seconds int` (0–32000, 0 = none).

### filter_ga_permissions — Game Assistants
`username varchar(32) PK` + one `bit` column per GM command: make_item, zoe, load_monster, mobkill0,
mobkill1, notice, filter_cmd, invisible, invincible, ban, recall_user, recall_guild, move_to_user,
move_to_npc, to_town, go_town, reset_q, wp, gm_skill, world_status.

### filter_commands — website → filter control queue
| column | type | notes |
| --- | --- | --- |
| id | bigint PK | |
| command | varchar(32) | `start`, `stop`, `restart`, `set_gateway_mode`, `reload`, `kick`, `notice`, `ban` |
| target | varchar(32) | `all`, `gateway`, `agent1`.., or a username/charname for `kick`/`ban` |
| payload | nvarchar(max) null | JSON, e.g. `{"mode":"inspection"}`, `{"text":"..."}` |
| status | varchar(16) | `pending` → `running` → `done` \| `failed` |
| result | nvarchar(max) null | |
| created_by | int | website user id |
| created_at / processed_at | datetime2 | |

Gateway modes: `gm` (only allowed IPs; others see "Check"), `inspection` (others get inspection
error), `public`. The filter starts in `gm` unless `server.start_in_inspection` is true.

### filter_status — filter → website heartbeat (one row per module)
`module varchar(32) PK`, `state` (`stopped`/`starting`/`running`/`error`), `online_count int`,
`connections int`, `gateway_mode`, `version`, `started_at`, `heartbeat_at` (website treats
> 15 s old as offline), `last_error nvarchar(max)`.

### filter_sessions — live connections (filter writes, website reads)
`id`, `module`, `ip`, `hwid`, `username`, `charname`, `connected_at`, `last_packet_at`.

### filter_logs — everything the filter logs
| column | type | notes |
| --- | --- | --- |
| id | bigint PK | |
| ts | datetime2 | UTC |
| level | varchar(8) | `debug` \| `info` \| `warn` \| `error` \| `security` |
| module | varchar(32) | |
| category | varchar(32) | `connection`, `login`, `exploit`, `flood`, `limit`, `restriction`, `command`, `chat`, `gm` |
| ip / hwid / username / charname | varchar | nullable |
| opcode | varchar(8) null | hex, e.g. `0x7021` |
| message | nvarchar(1024) | |
| data | nvarchar(max) null | JSON |

Index on `(ts DESC)`, `(category, ts)`, `(username)`, `(ip)`. Retention configurable via
`logs.retention_days` (website job prunes).

### filter_config_version
Single row: `id = 1`, `version bigint`, `updated_at`. Incremented by the website on every config save.

### Website-only tables (also in VFILTER)
`users` (panel admins: role `owner` \| `admin` \| `viewer`), `password_reset_tokens`, `sessions`,
`audit_log` (who changed which setting, old → new value). Created by the website; the filter ignores them.
Their columns are defined by the website in `VFilterWebsite/specs/001-auth-and-shell/data-model.md`
(the filter never reads them, so they are not repeated here).

## Changelog
- 0.1.1 (2026-10-17, PATCH): website-only tables now point to their column definitions (feature 001).
- 0.1.0 (2026-10-17): first draft.
