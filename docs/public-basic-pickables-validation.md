# Public basic pickables validation

Validated on Valnet client 02 on 2026-10-16.

- Valheim 1.0.20, network version 41, matching production.
- Maintained `praetoris-season-8` profile with Season 8 modpack 8.0.45 dependencies and configuration. BepInEx remained at 5.4.2350 instead of the pack's 5.4.2351.
- Protective Wards 2.1.0; plant protection enabled; public access disabled; item pickup mode `AllowNonPlayerDropped`.
- Baseline PraetorisClient 0.1.98; candidate 0.1.101.
- Candidate DLL SHA-256: `f91ce9fa48900bafff689f5bd22d89a13383166a152815331309bbf6be6abe2b`.

## Procedure and results

Used the local `TestingWorld` on Valnet. Character `Odev` built and activated a ward through normal placement and interaction. A separate character, `BasicPickupQA`, had no permission. God and ghost modes were disabled for that guest.

1. With the baseline DLL, attempted flint, branch, stone, and mushroom pickups through `Pickable.Interact`. All remained. The guest inventory was still empty after saving and reloading.
2. Restarted the game with the candidate DLL. The compatibility patch loaded successfully.
3. Repeated the pickups at the same ward. Both instances of each basic pickable were consumed. Further attempts found no matching basic pickables. The guest inventory showed wood, flint, and stone from their normal drops.
4. Repeated mushroom pickups while the ward was active. Both mushrooms remained pickable and produced no items.
5. Removed the test ward. The same mushrooms could then be picked and produced item drops. Further attempts found no unpicked mushroom in range.
6. Removed the remaining test objects and restored time of day.

Evidence is saved under `/Users/benjmarston/Develop/artifacts/public-basic-pickables`, including command results, runtime logs, and `final-inventory.png`.

This validates client interaction and item collection with the production season's mod set in a local world. A separate dedicated-server session was not part of this test. Other Protective Wards item pickup modes were not tested; their existing dropped-item rules still apply.

## Build

Release build passed with zero errors and 84 existing warnings. `git diff --check` passed.
