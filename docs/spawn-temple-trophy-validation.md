# Spawn temple trophy validation

Validated on 2026-09-28 with PraetorisClient 0.1.83 on `valnet-client-02`.

The test profile, `temple-trophies-s8-20260928`, uses the pinned Praetoris Season 8
8.0.29 mod set, including ValheimResetNow 0.2.0, plus BuildableBossStones 1.1.2.
The game reports `l-1.0.16`. Tests ran in the local world `TempleProof0928b` with
a development character. The maintained Season 8 profile was not changed.
The world-generation helper was removed before the candidate run.

The live probe calls `ItemStand.UseItem` and `ItemStand.Interact` on game
components. It waits for the attachment operation, then checks inventory counts
and the attached item stored in the network object.

| Check | Cases | Result |
| --- | ---: | --- |
| Seven natural temple trophy stones, direct item use | 7 | Blocked; trophy retained; attachment empty |
| Seven natural temple trophy stones, automatic placement with Use | 7 | Blocked; trophy retained; attachment empty |
| Seven BuildableBossStones trophy variants, both methods | 14 | Accepted; one trophy consumed |
| Six ValheimResetNow variants, both methods | 12 | Accepted; one trophy consumed |
| Ordinary item stand inside the temple | 1 | Accepted; one trophy consumed |

All modded variants in this suite were instantiated inside the temple boundary.
This checks that location alone does not cause a rejection.

```text
SUITE COMPLETE originals=7 clones=13 failures=0
POWER result=True actual=GP_Eikthyr expected=GP_Eikthyr
```

Additional live checks:

- Released 0.1.82 attached an Eikthyr trophy at the natural temple. Inventory
  changed from 1 to 0. After saving and loading that world with 0.1.83, the
  existing trophy still selected `GP_Eikthyr`.
- A natural ValheimResetNow stone had `VRN_location=Crypt2`. Automatic placement
  changed its attachment from empty to `TrophyEikthyr`, consumed one trophy, and
  changed its hover text to `Trophy reset available`.
- A BuildableBossStones Eikthyr stone placed through the hammer placement method
  returned `placed=True`. Automatic trophy placement then consumed one trophy
  and populated its attachment.
- An original `BossStone_Eikthyr` spawned outside the temple accepted a trophy.
- The central `StartPlatform` retained its normal `Chiselled Platform` attachment
  prompt. It accepts the quest item `FrozenKingDrop` and is outside this feature.

The validation used one Valnet client hosting a local world. It did not test a
dedicated server or multiple clients. This is a client interaction restriction;
it does not enforce the rule against clients without the mod.

## Logs and build

The Release build passed with 0 errors and 84 warnings in existing build settings
and source files. The new feature produced no compiler warnings.
The release ZIP has matching 0.1.83 version metadata and the tested DLL.
The final rebuild has the same disassembled code as the tested DLL. The staged
ZIP retains the exact tested DLL.

```text
PraetorisClient.dll SHA-256:
263f654f6beb89f62db1dd81693f92a547a0f844ad0455540cc16a5c541b240f
```

The logs are not warning-free. Both baseline and candidate launch logs contained
the same 44 shader-platform error lines, including duplicate console/log output.
The candidate also reported MWL `stone_pi` asset-resolution warnings while loading
the dungeon area, an alternate-biome placement warning, and character-save
warnings. These concern assets, world generation, or saving; this change does not
modify those systems.

Selecting the existing Eikthyr power produced the vanilla warning
`Missing stat for guardian power: itemstand`. The selected power was verified as
`GP_Eikthyr`. Vanilla's power-stat switch expects a `GP_*` object name; the live
hook is named `itemstand`. This feature does not change that name or the power
selection method. No exception from the trophy patches occurred.

Local evidence is retained under
`/Users/benjmarston/Develop/valheim-validation-evidence/temple-trophies-20260928/`:
`baseline-mods.json`, `baseline.log`, `candidate.log`, `candidate-bepinex.log`,
and `build.log`.

## Repeat the live checks

Use a disposable local world and development character. The suite removes
existing trophy attachments after checking their powers and creates temporary
modded stones. Do not run it on a maintained world.

1. Create a separate profile with the Season 8 8.0.29 dependencies and
   BuildableBossStones 1.1.2. Install the candidate PraetorisClient DLL.
2. Build `Validation/SpawnTempleTrophies/TempleTrophyProbe.csproj`.
3. Add `TempleTrophyProbe.dll` and valheimCLI to the test profile.
4. Start the world and wait until the natural spawn temple is fully loaded.
5. Run `temple_probe list` to record the hooks and location boundary.
6. Run `temple_probe suite`. Wait for the `SUITE COMPLETE` log line.
7. Require 41 `PASS` results and `failures=0`. If a trophy was already attached,
   also require the reported actual and expected powers to match.
8. Run `temple_probe goto Crypt2`, wait for the area to load, and run
   `temple_probe list`. Select the index with `resetLocation=Crypt2` and run
   `temple_probe test <index> auto`.
9. Build an Eikthyr stone with the hammer outside the temple. Select its index
   with `temple_probe list` and repeat the automatic-placement check.
10. Inspect the test log for exceptions and unexpected warnings.

The probe is excluded from the production project and release package.
