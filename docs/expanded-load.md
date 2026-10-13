# Expanded Load

`PraetorisExpandedLoad` is an Epic Loot magic effect for crossbows that require a reload and for Dundr (`StaffLightning`).

| Rarity | Additional shots | Total shots per load |
| --- | ---: | ---: |
| Legendary | 1 | 2 |
| Mythic | 2 | 3 |
| Ancient | 3 | 4 |

Magic, Rare, and Epic items cannot roll the effect. Bows, other staves, and melee weapons cannot roll it.

Each attack uses the normal ammunition, damage, durability, and attack costs. Dundr retains its normal eitr cost when it reloads. The effect changes the number of attacks available between reloads, not the number of projectiles in an attack.

The load belongs to the current weapon. Switching weapons clears it. A failed ammunition check does not consume a loaded shot. Completing a reload fills the load again. Reload on Kill also fills the load through the normal `SetWeaponLoaded` method.

The effect reads its value from the weapon. Effects on other equipped items do not add shots. Runtime state is not written into the item or its shared prefab data.

## Live validation procedure

1. Use matching current Praetoris profiles on Valdev and a Valnet client.
2. Join Valdev with a development character.
3. Check the effect's live Epic Loot requirements for each rarity on Arbalest, Dundr, a bow, another staff, and a melee weapon.
4. Test normal, Legendary, Mythic, and Ancient versions of Arbalest and Dundr. Fire through two complete loads per case.
5. Record each successful attack, the remaining ammunition, the loaded flag, and completed reloads. Crossbows must consume one bolt per attack. The loaded flag must clear on the last attack in each load.
6. Check the other crossbows, weapon switching, insufficient ammunition, and Reload on Kill.
7. Inspect the client and server logs and visual proof. Restore the original test files and remove test helpers.

## Validation results — 2026-10-12

Tested on Valnet client 01 connected to Valdev with `DEVELOP_LoadProof`. The maintained Season 8 profiles were aligned with deployed release 8.0.41 for the test, including BepInEx 5.4.2351, Valheim 1.0.16, and Epic Loot 0.14.13. The candidate Praetoris Client version was 0.1.96.

Candidate DLL SHA-256: `75bc19720918e4a96dcba9ad26c144407e8c99a509a001a9a637f99c6e6242ef`.

| Weapon | Normal | Legendary | Mythic | Ancient |
| --- | --- | --- | --- | --- |
| Arbalest | 1 shot, twice | 2 shots, twice | 3 shots, twice | 4 shots, twice |
| Dundr | 1 shot, twice | 2 shots, twice | 3 shots, twice | 4 shots, twice |
| CrossbowGold | — | — | — | 4 shots |
| CrossbowGold_FrostFire | — | — | — | 4 shots |
| CrossbowGold_BloodLightning | — | — | — | 4 shots |

All 11 cases passed automated checks over 52 successful attacks. Each crossbow attack consumed exactly one bolt. The loaded flag remained set until the final shot in each load. Normal reloads then restored the full capacity. Dundr retained its normal eitr reload cost.

All 30 live eligibility checks passed: Arbalest and Dundr accepted Legendary, Mythic, and Ancient values of 1, 2, and 3. They rejected lower rarities. A bow, Staff of Embers, and an iron sword rejected all six rarities.

Additional live checks passed:

- Empty ammunition: after firing the only bolt, an attempted attack did not consume a loaded shot. Adding three bolts allowed the remaining three shots without a reload.
- Weapon switching: switching to an iron sword cleared the load. Equipping the crossbow again required a reload and restored four shots.
- Reload on Kill: a bolt killed a staged boar. The kill filled the load immediately. Four more shots were available before the next normal reload.

Selected Ancient Arbalest observations from the live attack observer:

```text
LOAD weapon=CrossbowArbalest time=193.520
SHOT index=1 loaded=True  ammoBefore=50 ammoAfter=49 time=194.720
SHOT index=2 loaded=True  ammoBefore=49 ammoAfter=48 time=195.460
SHOT index=3 loaded=True  ammoBefore=48 ammoAfter=47 time=196.200
SHOT index=4 loaded=False ammoBefore=47 ammoAfter=46 time=196.940
LOAD weapon=CrossbowArbalest time=200.940
```

The observation helper recorded normal animation-triggered attacks and reloads. It did not alter ammunition, damage, reload duration, or loaded state. Test setup supplied equipment and food before each case.

Log review found no errors during the 82 successful attacks across the main cases, edge checks, and recorded demonstration. Startup and join logs contained eight `NullReferenceException` entries in `PieceManager.BuildPiece.SnapshotPiece` through `ZNetView.Awake` and `ZDOMan.AddIfPortal`, plus asset/shader and mod hash warnings. The server accepted the client mod list. The same snapshot exception stack recurred after restoring the original profiles, joining, and logging out. These errors therefore also occur without the candidate or observation plugin. Startup was not error-free.

After testing, 953 checked files on Valdev and 660 on Valnet matched their backups. The original BepInEx loaders were also restored. Both restored profiles loaded Praetoris Client 0.1.94, and the restored client joined Valdev successfully. The temporary observation plugin and configuration additions were removed.
