# Live validation helper

Build with `dotnet build tests/SpawnIslandLive/Probe.csproj -c Release`.
Install `SpawnIslandProbe.dll` only on the leased Valdev and Valnet test profiles.
The helper is excluded from the released mod. Remove it after validation.

Use a disposable test character and a copied test world. These commands grant
items, move the character, and create objects. They are not production commands.

- `island_probe status`: print the world ID, spawn position, and active rule.
- `island_probe point x z`: compare generated and loaded terrain at a point.
- `island_probe sites`: find dry test points inside and outside an active boundary.
- `island_probe prepare`: give the test character a hammer and building materials.
- `island_probe goto x z`: move near a test point. Wait for terrain to load.
- `island_probe place prefab x z`: exercise the player's placement update and
  report placement status, created objects, and material counts.
- `island_probe bypass prefab x z`: create a piece directly to test server rejection.
- `island_probe count x z`: list ward objects near the point. Run on the server
  after allowing time for network updates.
- `island_probe cleanup`: remove objects tracked by this helper session.
- `island_probe config invalid|wrongworld|saved`: on the test server only, apply
  invalid data, data for another world, or the saved candidate. This updates and
  synchronizes the mod config. Always restore `saved` after these checks.

Run cleanup before restarting the client. Tracking does not survive a restart.
Inspect the placement result: an unrelated build restriction can also prevent a
ward. Confirm that the actual placement preview position matches the test area.
