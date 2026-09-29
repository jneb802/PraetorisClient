# Builder camera validation — 2026-09-28

Candidate: `feature/builder-belt-camera`, based on `e47bc1a` (0.1.83).

Tested DLL SHA-256: `e5dc1dccfed48d51661a3c3f1fc9cd4c66ed6a47ec8d88c7ff2d2ed55e757f76`.

## Environment

- Valnet client 01, Linux, local `TestingWorld`, character `BuilderProof`.
- Maintained `praetoris-season-8` profile, full production 8.0.29 mod set. Read-only production inspection confirmed that 8.0.30 was staged, not deployed.
- Candidate replaced PraetorisClient 0.1.82. Jotunn 2.30.2, EpicLoot 0.14.13, ExtraSlots 1.2.14, Protective Wards 2.0.15 remained installed.
- Developer commands supplied fixtures and observations. Camera movement used keyboard input. Placement used mouse input with `noCost=False`. The helper called the normal store, refuel, and damage methods.
- Main Release build passed with zero errors and 84 existing warnings. The separate helper build passed with zero warnings or errors. `git diff --check` passed.

## Observed results

| Check | Result |
| --- | --- |
| Hildir purchase | Belt listed at 1,500 coins. Normal purchase reduced coins from 1,998 to 498 and added the belt. |
| Activation requirements | Missing or unequipped belt, missing tool, absent ward, and empty ward each prevented activation. |
| Refuel | One eye was removed from inventory and added one eye of fuel. A full ward rejected payment. |
| Fuel lifetime | Idle fuel stayed unchanged. Fuel survived save/reload. Rapid toggling reused the paid portion. Accelerated tests consumed the final fractional eye to zero, ended camera mode, and allowed refueling. |
| Normal building | Detached-camera placement removed 10 Wood, 10 Stone, and 5 Greydwarf Eyes. Missing wood prevented placement without consuming other materials. |
| Protected areas | A foreign ward prevented activation near the source ward. A protected remote target returned `PrivateZone`; clicking did not place a piece or consume resources. |
| Stationary body | Body remained at approximately `(-29, 49.5, -258)` during movement and building. |
| Distance limits | Each default 30-meter limit stopped movement independently. Changed limits of 10 meters from the ward and 8 meters from the body also stopped movement independently. |
| Solid collision | Repeated forward input stopped before a stone wall. Repeated downward input stopped approximately 0.23 meters above the terrain ray hit. Body position did not change. |
| Exit conditions | Damage, belt removal, tool removal, teleport, fuel exhaustion, and active ward removal ended camera mode. Normal camera behavior returned. |
| Damage proof | Final recording shows health 24 → 23 and the camera exit message. No invulnerability was enabled. |

## Evidence

Artifacts are retained at `/Users/benjmarston/Develop/PraetorisClient-builder-camera-evidence/` on the development Mac. They are not release assets.

- `camera.png`: inspected 1920×1080 screenshot showing the camera HUD, player body, ward, and trader.
- `build-camera.mp4`: inspected 31.617-second recording, H.264, 1920×1080, 60 fps container. Shows detached movement, normal placement, and damage exit. The game frame rate is lower than the recording frame rate.
- `video-contact.jpg` and `damage-exit.jpg`: inspected recording samples.
- `final-record.txt`: matching inventory, body position, health, and camera observations.
- `collision-proof-final.json`: keyboard collision assertions and terrain ray evidence.
- `purchase-and-equipment.txt`, `purchase.png`, and `activation-first.txt`: purchase and equipment observations.

Visual review used the contact-sheet tool and manual inspection by the main agent. The automatic review model was unavailable. The accepted recording shows the intended game viewport throughout the inspected samples. Audio was not an acceptance criterion.

The final game log contained no exceptions. Its 44 error lines were shader messages. Warning categories included platform shader support, existing mod registration and asset references, missing audio, local character startup, and save/shutdown messages. No Builder Camera warning or exception was found. The broader mod set still reports missing MWL/DungeonPack assets; this change does not resolve those messages.

## Restoration

After normal logout and game exit, the original candidate slot and entire configuration directory were restored. All 510 original profile files matched their saved SHA-256 values, with zero extra profile files. The world archive contents were restored and compared byte-for-byte. The helper and test character were removed. The prior `praetoris-season-8-8.0.15` selection and game profile links were restored. Recording stopped. Valnet client 01 remains powered on at the user's request. Production was not changed.

## Remaining validation

Dedicated-server and two-player validation are incomplete. Valdev reported an inactive `season8-1-0-migration` selection while its mounted plugin path pointed to `praetoris-season-8`; it was not changed for this client test.

Before production deployment, validate synchronized configuration, remote ward ownership, simultaneous refueling and fuel consumption, ownership transfer, disconnect/reconnect, and normal build permissions with the candidate installed on Valdev and two aligned clients. This client run does not prove those network paths. Repair/removal resource behavior and alternate build tools also need explicit live coverage.
