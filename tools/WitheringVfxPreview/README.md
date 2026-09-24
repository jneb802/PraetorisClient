# Withering visual comparison tool

This separate BepInEx plugin previews four visual treatments for PR #104. It is excluded from the PraetorisClient build and release package. No visual has been selected as the feature default.

Build with `dotnet build tools/WitheringVfxPreview/WitheringVfxPreview.csproj`. Install the resulting DLL only in a temporary test profile alongside the Withering Bomb feature build.

In a local world, stand near the intended creature and run `wither_preview <style> [durationSeconds]`:

| Style | Treatment |
| --- | --- |
| 0 | Remove the preview and Withered; untreated reference. |
| 1 | Violet smoke using a material from `vfx_Poison`. |
| 2 | Dust using a material from `vfx_Smoked`, plus falling square flecks. |
| 3 | Purple body-material tint and emission adjustment, plus falling flecks. |
| 4 | A floating, camera-facing curse symbol with a slow scale pulse. |

The command applies the real `SE_PraetorisWithered` through the creature's status-effect manager. The preview removes its objects and restores original materials when that status expires or the style changes. `wither_check` reports the status and remaining preview-component count. The command refuses clients connected to a dedicated server or a world with other connected peers.

## Capture run

September 24, 2026: Valnet client 02, Valheim l-1.0.15, character Odev, local TestingWorld. Temporary profile `withering-vfx-s8-8.0.26` copied from the previous Season 8 8.0.26 test profile. Production-mirror package versions were retained except the Withering feature candidate, plus valheimCLI, Server Devcommands, and this preview plugin. No Valdev or production server participated.

The feature branch was merged with main before building. Candidate PraetorisClient DLL SHA-256: `1dd3a1f1c8fce1fd7bd4efd8e9064bc3b71370e9662714db8aee10b7db6ef9bb`.

Subjects: `Lox`, `SeekerQueen`, and `GoblinKing`. Spawned in a cleared Meadows area near `(490, 56, -510)` and held in place with `cli_freeze_nearest_character`. Idle animations and native boss effects remained active. Clear weather, time 0.5, HUD hidden. All four styles used the same camera for each subject:

| Subject | Camera | Look target |
| --- | --- | --- |
| Lox | 496, 61, -499 | 489.2, 58.8, -509.3 |
| Queen | 501, 64, -498 | 492, 61, -514 |
| Yagluth | 499, 64, -503 | 489.4, 61, -518.8 |

For each subject, applied styles 0 through 4 and captured each after four seconds with `cli_screenshot`. Then applied each of styles 1 through 4 with a two-second duration and checked cleanup after three seconds. Raw command results are in `evidence/`. Screenshots are under `docs/withering-vfx/` at the repository root.

These are local appearance and expiry tests. They do not validate multiplayer replication, natural boss arenas, combat readability while moving, or the complete bomb attack path. The coating intentionally changes body materials; its brightness differs across creature shaders. The dust and square flecks are prototypes, not finished art.

An old cameraTools DLL failed during initial setup and was removed before the final run. A preview camera command also failed on private game fields; it was removed from the submitted source. Final screenshots used valheimCLI's working camera and screenshot commands. Removing that unused command did not change the visual implementation. Existing Linux shader-platform warnings remained at startup.

All 12 expiry checks passed: Withered was absent and the preview-component count was zero. No errors or warnings appeared in the final capture log interval. Both projects built successfully. After capture, the test world was restored from its pre-clearing backup and verified with a recursive file comparison. The previous active profile was restored. The temporary profile and capture world were retained for follow-up work.
