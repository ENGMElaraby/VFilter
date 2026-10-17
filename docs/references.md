# External references

Outside repos to read when we need facts about the vSRO protocol, game files or game systems.
This file is identical in VFilter and VFilterWebsite. Change it in both or neither.

**Licenses matter.** Read anything here, but only copy code from repos marked MIT (keep the notice)
or from our own repos. GPL-3.0 code must not be copied into our code. Repos with no license are
read-only: learn from them, write our own code.

## Ranked: check in this order

| # | Repo | What it is | Use it for | License |
| --- | --- | --- | --- | --- |
| 1 | [ENGMElaraby/AegisCore-SRO](https://github.com/ENGMElaraby/AegisCore-SRO) | Our Go server for vSRO 1.188 (Gateway, Agent, Download, MySQL). Active. | Opcodes, packet layouts, handshake, Gateway → Agent handoff, game-system notes | Ours |
| 2 | [ferdoran/silkroad-docs](https://github.com/ferdoran/silkroad-docs) | Reverse-engineered docs of the SRO client: protocol, encryption, PK2, assets, anti-cheat. Updated 2026-04. | Per-opcode packet field layouts (`docs/messages/`, 289 opcodes indexed in `messages/INDEX.md`), `network_protocol.md`, `protocol_reference.md`, `anticheat.md`, `pk2_format.md` | None (read-only) |
| 3 | [ferdoran/openroad](https://github.com/ferdoran/openroad) | Rust/Bevy clone of the SRO **client** for v1.188. Very active (2026-10). | Client-side view of the wire format (`packets/`), what a real client sends and expects | GPL-3.0 (read-only for us) |
| 4 | [ENGMElaraby/go-sro](https://github.com/ENGMElaraby/go-sro) | Our fork of `ferdoran/go-sro`, the older Go server AegisCore grew from. Last update 2024-10. | Fallback when AegisCore lacks something. If they disagree, AegisCore wins. | Ours (upstream: DBAD) |
| 5 | [ferdoran/pk2](https://github.com/ferdoran/pk2) | Rust crate for reading/writing PK2 archives, plus `pk2_mate` (extract/pack/repack). Fork of `veykril/pk2`, last update 2022. | How PK2 archives work; extracting `Media.pk2` to read text data (region, item, skill names) | MIT |
| 6 | [ferdoran/joymesh](https://github.com/ferdoran/joymesh) | Web navmesh viewer/editor (TypeScript, 2022). Live at joymesh.ferdoran.de. | Understanding regions and navmesh (`.nvm`) when building region pickers | None (read-only) |
| 7 | [ferdoran/navmesh-editor](https://github.com/ferdoran/navmesh-editor) | Older Vue navmesh editor (2022). Superseded by joymesh. | Same as joymesh; rarely needed | None (read-only) |

## Other ferdoran repos ([profile](https://github.com/ferdoran?tab=repositories))
- `sro-load-tester` (Go, 2021): load-tests SRO servers. Useful idea for load-testing VFilter.
- `go-sro-framework`, `go-sro-gateway-server`, `go-sro-agent-server`, `go-sro-db`, `go-sro-fileutils`
  (2020–2021): the split-repo version of go-sro before it became one repo. Use go-sro instead.
- `sro-navmesh-viewer` (JS, 2021): predecessor of joymesh.
- `rustyroad` (Rust, 2022), `sro-kot` (Kotlin, 2022): early experiments, superseded by openroad.
- Everything else there (Advent of Code, timey, uml-editor, uni projects) is unrelated to SRO.

## Feature sources (forum releases)
- Silk Stall, elitepvpers, `bimbum*`, 2022-11: https://www.elitepvpers.com/forum/sro-pserver-guides-releases/5087904-release-silk-stall.html
  (Cloudflare-protected; no license, read-only). Our plan: `docs/features/silk-stall.md`.

## Caveats
- Everything above targets **vSRO 1.188**. Confirm our server's client version before trusting a
  packet layout.
- AegisCore's `db/` is its own MySQL schema, not `SRO_VT_SHARD`. Check table names against our real DB.
