# Spawn island validation — 2026-10-01

The later `SpawnIslandWards.Enabled` toggle addition passed the Release build
and the 29 existing boundary tests. The live results below cover the feature
before that addition. Live synchronization and placement tests for toggling
off and on have not been repeated.

Validation uses Valdev and Valnet client 01. All game connections target Valdev
at `178.156.172.16:2456`. Production was not joined or changed.

## Environment

- Valheim 1.0.16 and Praetoris Season 8 modpack 8.0.31.
- Maintained `praetoris-season-8` profiles, with the candidate mod and temporary
  `valheimCLI` and `SpawnIslandProbe` helpers.
- Plugin files checked against the deployed production snapshot before applying
  the candidate. Intended server-only and client-only differences were preserved.
- Copied world `praetorisSeason8v2`, ID `17889786518376477`.
- Test character `developisland`, without server administrator rights.
- Test-only ValheimEnforcer settings allow the candidate hash and test helpers.

## Results

- Release build passed with 84 existing warnings and no errors.
- All 29 geometry and serialization checks passed.
- The live survey completed after 15,953 terrain samples.
- The saved outline was compared with the in-game world map. It covers the main
  spawn land mass and excludes neighboring land across water. River-separated
  land needs operator review; the algorithm follows connected dry ground.
- The saved candidate survived a restart unchanged. A repeated generate command
  refused to overwrite it.
- Initial live activation exposed a synchronization defect: the connected client
  retained the empty boundary. Activation now reloads the saved config, which
  triggers Jotunn synchronization. The repeated live test passed without reconnecting.
- A ward created before activation remained on the server afterward.
- A directly created ward inside the active boundary was rejected by the server.
  The server log identified the ward and the spawn island restriction.
- Inside placement at `(386, -61)` showed the correct message, created no ward,
  and left wood unchanged at 100. The protected ward preview was red.
- Outside placement at `(-500, -45)` created a ward confirmed on the server and
  reduced wood from 100 to 80. A directly created outside ward also survived.
- A wood wall, Creature Owner Ward, and Network Ward placed inside at `(20, 950)`
  and consumed their normal materials. A vanilla ward at that location was blocked.
  Earlier ordinary-piece attempts encountered existing private-zone restrictions;
  the successful checks used unclaimed ground.
- Shoreline samples at Z=950 changed from blocked to allowed across the western
  edge (X=-44/-52) and eastern edge (X=364/372). A western coastal placement was blocked.
- The active rule survived a server restart and client reconnect. Its candidate
  hash remained unchanged; another generation attempt refused recalculation.
- Invalid and wrong-world data blocked outside placement without consuming wood
  (78 before and after). The client displayed the setup message. The server also
  rejected a directly created outside ward while the boundary was invalid.
- Restoring the saved map restored outside placement: the server received the
  ward and wood decreased from 78 to 58.

The live checks cover both ordinary placement and the server receive check.
Terrain-edit persistence is covered by the standalone saved-map test; a live
terrain edit was not performed. The one-time operator map review remains required
for each season, especially where rivers divide land or custom terrain is present.

## Log review

No exception from the feature's boundary, survey, or placement code appeared in
the completed placement checks. The helper initially used the wrong piece
selection sequence; it was corrected before the passing material tests. An
unknown `wood_wall` prefab request failed in the helper; the correct vanilla name
is `woodwall`, and the helper now reports unknown names explicitly.

The profile logs also contain shader/video errors, the existing Trailership
prefab warning, and PieceManager thumbnail errors during logout. The thumbnail
stack enters `ZDOMan.AddIfPortal` from `PieceManager.BuildPiece.SnapshotPiece`;
this separate issue was not fixed here. Temporary removal of FastLink destinations
caused menu configuration errors; the original file was restored. ValheimEnforcer
logged the test assemblies without rejecting the validated connections.

Candidate SHA-256:
`f2267969a91b8ffd1a74b5830983905c4a5c6485a217082b2161e1d5384a7708`.

## Evidence

Local evidence directory:
`/Users/benjmarston/Develop/valheim-validation-evidence/spawn-island-20261001`.

`commands.jsonl` records commands and results. `map-review.png`, `map-detail.png`,
and `boundary-preview.png` show the map review. Raw backups and logs remain local
because they can contain private configuration. They are not release artifacts.

## Restoration

Valnet client 01: all 513 original profile files matched their saved SHA-256
hashes, with no extra files. The original profile selection was restored. The
test character, helpers, staging files, and backups were removed. The machine
was powered off and its lease released.

Valdev: all 869 original profile files matched before the restored startup check.
The restored server loaded its original `mwlPortIconTest` world and original
PraetorisClient 0.1.82. It was then stopped, matching its original state. The
profile was restored again after that startup check to remove generated changes.
All 869 files matched again, with no extra files. The original launch config,
start script, agent config, and profile links were verified. The copied world,
staging files, and remote backups were removed, and the lease was released.
