# Data Model: VFilter 001

These are the in-memory types (`VFilter.Core`). Tables are per the shared contract 0.3.2.

| type | fields |
| --- | --- |
| `ConfigArea` | Settings, Lists, AreaRules, Cooldowns, Modules, GaPermissions (names = contract area keys) |
| `ModuleConfig` | Id, Type (Gateway/Agent/Download), Name, ModuleIp, ModulePort, GuardIp, GuardPort, Enabled; Target = lower(Name) |
| `SettingsSnapshot` | Version, `FrozenDictionary<string, SettingValue>` (typed bool/int/string/json/color) |
| `ListsSnapshot` | Version, `FrozenDictionary<string list, ImmutableArray<ListEntry{Value, Comment}>>` |
| `AreaRulesSnapshot` | Version, `ImmutableArray<AreaRule{Id, Rule, Area, Target, ParamsJson, Enabled}>` |
| `CooldownsSnapshot` | Version, `FrozenDictionary<string action, int seconds>` |
| `ModulesSnapshot` | Version, `ImmutableArray<ModuleConfig>` |
| `GaPermissionsSnapshot` | Version, `FrozenDictionary<string username (case-insensitive), FrozenSet<string command>>` |
| `ModuleState` | Stopped, Starting, Running, Error |
| `ModuleStatus` | Name, State, StartedAt, LastError, Connections, OnlineCount, NeedsRestart |
| `GatewayMode` | Gm, Inspection, Public |
| `PendingCommand` | Id, Command, Target, PayloadJson, CreatedAt, DbNow |
| `CommandOutcome` | Done/Failed + Result (≤ 500 chars) |
| `LiveSession` | LocalId, DbId?, Module, Ip, Hwid?, Username?, Charname?, ConnectedAt, LastPacketAt |
| `FilterLogEvent` | Ts, Level, Module, Category, Ip?, Hwid?, Username?, Charname?, Opcode?, Message (≤ 1024), DataJson? |

Command results are short English text; the website shows them as is.
