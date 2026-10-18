# Temporary Endgame compatibility

This bridges Epic Loot PRs [45](https://github.com/MidnightsFX/Randy_Vapok_ValheimMods/pull/45)
and [47](https://github.com/MidnightsFX/Randy_Vapok_ValheimMods/pull/47) for Endgame testing.
Keep the separately built Endgame DLL and its Epic Loot JSON patches in the client
package. This change does not publish or bundle an Endgame build.

## Scope

- Runs only when BepInEx loads both Epic Loot and Endgame. The optional dependency
  makes Endgame load first; no source or binary reference to Endgame is needed.
- Supports the exact production Epic Loot 0.14.13 binary with SHA-256
  `802790e0cbd8a6f951627619a488b84e6a406e8770e2687b2fff8b63d2adf9ea`.
  An unrecognized older binary gets a warning and no compatibility patches.
- Allows enchanting `ve_tablet` and `ve_tablet_tier01` through `ve_tablet_tier15`.
  Other eligibility checks still apply. Keeps these items in the Tablet category
  when Epic Loot rebuilds item configuration.
- Prevents socket rolls, insertion, swaps, chisel use, and crafting socket
  transfers for those items. Omits socket probabilities from their preview.
  Existing socket data is not erased on load; removal of old socket contents
  remains available.
- Restores the crafting result's text and icon when the native fix is absent.
- Does not add general-purpose JSON fields to older Epic Loot. The temporary
  tablet policy is compiled into this bridge, so clients and servers running it
  use the same policy. This is not a backport for arbitrary item categories.
- Uses a separate Harmony owner. Removing this code does not require changing
  Epic Loot's saved-item format.

## Native support and removal

Item patches are skipped when the loaded assembly has both config fields, both
runtime properties, and the native checks in all five socket paths and the
enchanting eligibility path. The crafting result fix is checked separately.
Field presence alone is not sufficient.

Keep Endgame's iteminfo JSON patch, including `Enchantable: true` and
`Socketable: false`, for the eventual native release. Older Epic Loot can discard
unknown fields when it rewrites its generated config; this bridge does not rely
on those generated fields. On upgrade, verify the Endgame JSON patch has been
applied again and the native runtime contains both settings for every tablet.

Before removing the bridge, test the upgraded Epic Loot with tablets saved by
the old build. Confirm values, effects, socket rules, save/restart, and further
enchanting. Then remove `EndgameCompatibility.cs`, its startup/shutdown calls,
its optional Endgame dependency, and this test project in one change. No live
upgrade or removal proof has been completed yet.

## Local checks

Build the client and test harness, then run under Mono (not the .NET Core runtime).
The harness loads the actual client DLL and an actual Epic Loot DLL. It invokes
the production compatibility code; it does not copy that code into a test model.

```sh
dotnet build PraetorisClient.csproj -c Release
dotnet build tests/EndgameCompatibility
mono tests/EndgameCompatibility/bin/Debug/net481/EndgameCompatibility.exe \
  bin/Release/PraetorisClient.dll /path/to/production/EpicLoot.dll /path/to/Valheim
mono tests/EndgameCompatibility/bin/Debug/net481/EndgameCompatibility.exe \
  bin/Release/PraetorisClient.dll /path/to/native-support/EpicLoot.dll /path/to/Valheim native
```

On 2026-10-18: Release build passed with 83 existing warnings and zero errors.
The harness build had zero warnings and errors. The production binary passed 51
checks for item names, native detection, patch targets, parameter binding, three
real IL transformations, and rejection of changed method bodies. A local Epic
Loot build containing PR 45 (SHA-256
`1d3244a7387458bf4b056387ef8707d6172733ad0d6fabc63327dce289fc5e3b`)
passed 25 checks, including native detection.

These checks do not attach runtime patches or exercise Unity gameplay. An
attempt to attach patches outside Unity failed in MonoMod's runtime detour
initialization. Actual attachment, gameplay, and saved-item migration must be
tested in Valheim.

## Remaining live proof

Valnet Client 01 was started and inspected on 2026-10-18. Production was Season 8
8.0.46 with Epic Loot 0.14.13 and Praetoris Client 0.1.101. Valnet's maintained
profile reported Season 8 8.0.34; its Epic Loot binary matched production.
The profile and mmcli registry were backed up before attempting alignment.
The SSH key agent then refused signing requests. Alignment, baseline
reproduction, candidate deployment, and live proof did not run.

Resume with a device lease and a verified current production mirror:

1. Run the ordinary enchanting-table flow with Endgame and the unchanged client.
   Record tablet eligibility and socket behavior before deploying this change.
2. Deploy the candidate client to that same profile. Keep Epic Loot unchanged.
3. Confirm the compatibility startup log and absence of new errors. Check all
   tablet names and a normal weapon as a control.
4. Use the existing `cli_epicloot_item`, `cli_epicloot_enchant_ui`,
   `cli_epicloot_roll`, and UI inspection commands from valheimCLI. Exercise the
   actual enchanting button, materials consumption, preview, and result dialog.
5. Exercise socket insertion/swaps, chisels, and crafting transfers. Check that
   rejected actions consume no items and normal weapons retain their behavior.
6. Save/restart with an enchanted tablet. Upgrade to the native-support build,
   verify the skip log and loaded JSON flags, and repeat the checks.
7. Repeat the server/client flow on the current production mirror. Inspect both
   logs, restore profile backups, verify restoration, and release devices.
