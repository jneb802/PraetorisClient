# Valheim 1.0 validation

Tested on Valnet Client 02 on 2026-09-09 with Valheim `l-1.0.7`
(Steam build `25185596`, network version 39), BepInEx 5.4.23.3,
Jotunn 2.29.2, and the isolated `praetorisclient-1.0-validation` profile.
Epic Loot was absent. Tests used a local world and the development character
`Prae107Test`.

## Reproduced failures

- Packaged 0.1.60 failed in `Awake` because its console-command constructor
  reference no longer matched the game.
- Recompilation exposed changed hover, terrain, sector-query, and inventory APIs.
- Startup then failed while applying patches for the absent Epic Loot dependency.
- The registration panel did not capture game input.
- Delivery reported success, but the live chest stayed empty because the mod
  stored text where Valheim 1.0 expects an inventory byte array.
- Opening the chest logged a missing `InventoryElement.m_go` field and left
  unused slots visible.

## Final results

| Check | Result |
| --- | --- |
| Start modded game and enter local world | Passed |
| Ward hover interface, set owner, activate, read status | Passed |
| Register ServerChest using its visible interface | Passed with mouse and Enter |
| Deliver `Wood:75 Stone:3` through `serverchest_send_bulk` | 78 items in three stacks |
| Read live container contents | 75 Wood and 3 Stone |
| Save, restart, reload, and read chest contents | Same 78 items preserved |
| Consume Cleanse Mead through `Player.UseItem` | Removed Rested and applied Cleanse |
| Display occupied chest slots | Exactly three slots visible; screenshot inspected |
| Click Take all | Items moved to player; saved chest reported zero items |

The tests used valheimCLI, real mouse/keyboard input, and a temporary separate
validation plugin for interaction calls and inventory/status assertions. The
temporary plugin is not part of PraetorisClient or its release output.

The final game log had no PraetorisClient exceptions or missing-field warnings.
Eight Jotunn warnings about duplicate asset names also occurred in the baseline
profile without PraetorisClient. The build completed with 65 warnings, including
nullable fields, unused fields, and a duplicate `Environment.props` import.

The tested DLL SHA-256 is
`8ee076278a1ba497e29e38241dbe3adcd040c40f56d39f2c6b61876e35b5b0ad`.
It was built against game assemblies copied from Client 02 and freshly
publicized copies of those assemblies. See the README for path overrides.

This does not validate Epic Loot with the dependency installed, dedicated-server
RPC flows, multiplayer ownership transfer, or the full production modpack.

## Rebase after PR #73

PR #73 removed the creative biome feature. The rebase preserves that removal
and drops the compatibility changes to the deleted file. The rebased Release
build passed with zero errors and 65 warnings against the same Client 02
assemblies. The live results and DLL hash above describe the build before this
rebase; live tests were not repeated after the feature removal.
