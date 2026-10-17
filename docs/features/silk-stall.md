# Planned feature: Silk Stall

**Status:** planned, not specified yet. Build it after the client DLL exists (see "Depends on").
This file is identical in VFilter and VFilterWebsite. Change it in both or neither.

## What it is
A player opens a normal stall but marks it a **silk stall**: items are priced in silk instead of gold.
When someone buys, silk moves from the buyer's account to the seller's account, and the item moves
through the normal game stall flow.

Source: elitepvpers release by `bimbum*`, 2022-11-27,
https://www.elitepvpers.com/forum/sro-pserver-guides-releases/5087904-release-silk-stall.html
(Cloudflare-protected; open it in a real browser). The code is on justpaste.it, linked from the post:
- Client DLL (C++): `IFStallSlot.cpp/.h`, `IFMessageBox.cpp`, `Util.cpp`, `CPSMission.cpp`.
  Adds the silk/gold toggle and silk price display to the stall UI.
- Filter (C#): `AgentServer.cs`, `SilkStall.cs`, `SharedObjects.cs`.

No license is stated, so we read it for ideas and write our own code.

## How the release works
| Opcode | Direction | What the filter does |
| --- | --- | --- |
| `0x186D` (custom) | client → filter | Client DLL says "this stall is silk" (1 bool). Filter stores it on the session and **blocks** the packet. |
| `0xB0B1` STALL_CREATE_RESPONSE | server → client | On success, if the session flagged silk, register the seller's stall as a silk stall. |
| `0xB0B2` STALL_DESTROY_RESPONSE | server → client | On success, forget the stall. |
| `0x70BA` STALL_UPDATE_REQUEST | client → server | Type 1 = update price, 2 = add item, 3 = remove item. Filter records `slot → silk price`. |
| `0xB0B3` STALL_TALK_RESPONSE | server → client | When a buyer opens a stall, filter sends custom `0x186D` (bool: is it a silk stall) so the buyer's DLL shows silk prices, and remembers which stall the buyer is looking at. |
| `0x70B4` STALL_BUY_REQUEST | client → server | Check buyer inventory has space, buyer not dead, buyer silk ≥ price, seller online. Then take silk from buyer and give to seller (`CGI.CGI_WebPurchaseSilk` twice), then let the buy through. |

Field layouts for the standard opcodes: `ferdoran/silkroad-docs` `docs/messages/` (see `docs/references.md`).

## Known problems in the release (we must fix these)
1. **Gold is charged too.** The item still has its gold price in the game server, so the buyer pays
   gold *and* silk. Fix (the author's own note): send price **0** to the server in `0x70BA`
   (add and update), and rewrite `0xB0BA` STALL_UPDATE_RESPONSE to the client with the silk price.
2. **Charges silk before the game confirms the buy.** If the buy then fails (item already sold, slot
   empty, stall closed), the silk is gone. We must charge only after `0xB0B4` BUY_RESPONSE succeeds,
   or hold the silk and refund on failure.
3. **No atomic transfer.** Two separate silk calls can half-succeed. Do it in one SQL transaction.
4. **SQL built by string interpolation.** Use parameters.
5. **Crashes on missing keys** (stall or slot not registered). Treat missing as "not a silk stall".
6. **Order number race:** `MAX(OrderNumber)+1` from `SK_SilkBuyList` collides under concurrent buys.
7. **No audit log.** Every silk stall sale must be logged (buyer, seller, item, slot, price, result).
8. State is in memory only, so a filter restart forgets all silk stalls. Sellers must reopen, or we
   persist it.

## Economy concerns from the thread
Replies warn it can kill the gold economy. Make it configurable from the website:
- Master on/off switch (default **off**).
- Which silk type can be used (normal silk vs gift/premium silk). A suggestion in the thread: allow
  only gift silk, making it a second currency players earn from events.
- Optional: min level, allowed regions (reuse `stall.allow` area rules), max price, a silk fee/tax
  burned per sale.

## Depends on
- **Client DLL** (separate, later project, see the constitution): silk/gold toggle, silk price display,
  and the custom `0x186D` packet. Without it, players can't mark a stall as silk.
- Filter features 001 (proxy core), 002 (control/telemetry), 004 (rules engine).
- Website: settings catalog (006) for the switches, logs (004) for the audit trail.
- `SRO_VT_ACCOUNT` silk tables (`SK_Silk`, `SK_SilkBuyList`, `CGI.CGI_WebPurchaseSilk`): confirm
  names and silk types against our real DB before writing the spec.

## Shared contract changes (to add when this is specified)
- Settings: `silk_stall.enabled`, `silk_stall.silk_type`, `silk_stall.min_level`,
  `silk_stall.max_price`, `silk_stall.fee_percent`.
- New table, e.g. `filter_silk_stall_sales` (audit), written by the filter, read by the website.
- Custom opcode `0x186D` documented with the client DLL protocol.

## Draft prompts (run when the client DLL exists)
VFilter:
```
/speckit-specify Silk Stall per docs/features/silk-stall.md: stalls priced in silk. Track silk stalls from the client DLL's 0x186D flag and stall create/destroy/update packets; send price 0 to the game server and show the silk price to clients; on buy, transfer silk buyer→seller in one SQL transaction only after the game confirms the purchase; block if disabled, wrong silk type, low level, region not allowed, insufficient silk, full inventory or dead; log every sale. Fix every "known problem" listed in the doc.
```
VFilterWebsite:
```
/speckit-specify Silk Stall admin per docs/features/silk-stall.md: settings page (enable, silk type, min level, max price, fee) saved via SettingsRepository, and a sales log page over filter_silk_stall_sales with filters and CSV export.
```
