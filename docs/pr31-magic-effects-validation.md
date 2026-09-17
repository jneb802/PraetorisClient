# PR 31 validation — 2026-09-17

## Branch and environment

- Merged `main` at `d26640b2b6116916838f9f7fcaf81a8e5d8e54c9` into `feature/epicloot-api-magic-effects`.
- Used Valnet Client 02 with the isolated `pr31-magic-effects-20260917` profile.
- Imported the current production modpack, Praetoris Season 8 `8.0.21`.
- Loaded Epic Loot `0.14.8`, Jotunn `2.30.0`, and BepInEx `5.4.23.5`.
- Entered `TestingWorld` with local development character `PR31Test` on Valheim
  `l-1.0.14`, Steam build `25364265`, network version 40.

## Reproduced failure and correction

The merged branch registered all 12 magic effects and both proxy abilities,
then failed during Harmony patch initialization. Both `SEMan.AddStatusEffect`
patch declarations still specified the old four-argument signatures. Current
Valheim adds a fifth `short variant` argument.

Updated both declarations. The Release build passed with 84 warnings and zero
errors. The subsequent live startup registered all 12 effects and both abilities
without the previous Harmony exception. This proves registration and patch
initialization only; it does not prove effect behavior in a world.

## Measured results for retained effects

| Effect | Live result |
| --- | --- |
| IncreaseEffectDuration | 20 seconds became 30 with a 50% bonus; refresh stayed at 30 |
| ModifyTrinketDuration | 60 seconds became 90 with a 50% bonus |
| ModifyAdrenalineCost | Maximum adrenaline changed from 50 to 37.5 with a 25% reduction |
| ReloadOnKill | Without the effect, a lethal shot left the crossbow unloaded for eight samples; with the effect, the first sample already showed loaded while the attack animation continued |
| PiercingShot | A normal arrow damaged only the first aligned Troll; an enchanted arrow carried the piercing component and damaged both aligned Trolls |
| ArrowRain | One arrow impact spawned ten additional projectiles, damaged the target, and put the ability on cooldown |

The table includes six effects retained from the original live run. The current
branch also includes Point Blank, covered in the shardstone report. Tests used
the game CLI and a separate temporary measurement plugin. The plugin is not
included in the mod source or release DLL. Early measurement-plugin access
errors were corrected before accepting measurements.

Early reload checks waited long enough for normal reloads, so the final
comparison sampled the state throughout the attack instead.

These results predate the removal of six effects. The removal build passed
locally; live tests were not repeated for that build.

Original tested Release DLL SHA-256:
`5e1bbf117e09242ef023e26d74b5235938f11efbf02d12a3fa762ebeffc19e15`.

## Game installation repair

Steam updated the client from build `25253764` to `25364265` during testing.
The updated installation split data between `Valheim_Data` and `valheim_Data`.
The native Linux launcher then exited with `FindDataPath: Player data not found!`.
Steam file verification completed successfully but did not resolve the split.

A reversible directory merge allowed startup to proceed. The game without mods
completed loading after a long initial load. Missing-script messages appeared
during this successful startup as well.

The startup log contains shader-platform errors, Protective Wards patch warnings,
Jotunn asset/mock warnings, Balrond fallback/metadata warnings, StarLevelSystem
duplicate reset-group warnings, and one unknown prefab in the existing test
world. FastLink also logs failed lookups for its default example addresses.
The effect-test window had no new warnings, errors, or exceptions. The entire
profile startup is not warning-free.

These are controlled tests with one client. PvP, dedicated
server behavior, latency, every item/rarity combination were not tested.

Local evidence is retained at
`/Users/benjmarston/Develop/valheim-validation-evidence/pr31-magic-effects-20260917/`.
The temporary test profile is retained on Client 02. Its previous active profile
was `praetoris-season-8-8.0.15-ship-hover-0.1.66`.
The directory repair preserves Steam's new files and keeps the original lowercase
directory in `/home/paperspace/pr31-valheim_Data-before-case-repair`.
Removed the spawned test enemies, cleared the test character's inventory, saved
the world and character, removed the measurement plugin from the test profile,
and restored the previous active profile.
