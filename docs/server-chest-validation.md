# Server Chest validation — 2026-09-29

Validated on Valdev with Valnet client 02 and the `Developer` test character (`Odev`). Both ran Valheim 1.0.16 and the candidate PraetorisClient 0.1.84.

## Environment

- Production's deployed release was Praetoris Season 8 **8.0.29**, with PraetorisClient 0.1.82 and Jotunn 2.30.2. Release 8.0.30 was only staged.
- The maintained `praetoris-season-8` profiles contained older files. The client registry's 8.0.29 label did not match its installed DLLs.
- Backed up both profiles and loaders. Used production's deployed plugin files and BepInEx 5.4.2351 for this run. Kept the client/server mod differences and the test server's existing configuration overrides. Used production's deployed ValheimEnforcer policy.
- Added the candidate DLL to both sides. Client test helpers were valheimCLI and Server Devcommands. ValheimEnforcer accepted the client mod list.
- Used the existing Valdev test world `mwlPortIconTest`. No production files were changed.

## Results

| Check | Result |
|---|---|
| Build the first Server Chest through `Player.TryPlacePiece` | `placed=True`. The server logged `Automatically registered new Server Chest for Odev.` No registration interaction was used. |
| Send `serverchest_send Odev Coins 7` | The chest contained seven coins. Its appearance was the Dvergr tower treasure chest. |
| Try to build a second chest | `placed=False`. The client displayed the one-chest limit message. |
| Restart the client and server, then reconnect about 580 metres away | The client found no loaded objects at the original chest location. Another build attempt still displayed the one-chest limit message. |
| Return to the original chest after the restart | The original chest still contained seven coins. |
| Destroy the original chest and build a replacement | `placed=True`. The server automatically registered the replacement. |
| Cleanup | Removed both test chests, logged out through the normal save path, and stopped the test server. |

Build commands used `nocost` to isolate placement and registration from recipe requirements. The final candidate also retained the wooden chest's overlap and moving-surface placement checks.

Representative commands:

```text
cli_build_select ServerChest nocost
cli_build_try_place_at ServerChest 623 30.5 -1199 nocost
serverchest_send Odev Coins 7
cli_interact_nearest 8
cli_build_try_place ServerChest nocost
cli_teleport 1200 90 -1200
# Restart both sides and reconnect before the next check.
cli_prefabs_at 623 31 -1199 15
cli_build_try_place ServerChest nocost
cli_teleport 620 35 -1200
cli_interact_nearest 8
cli_destroy_nearby_prefabs ServerChest 8
cli_build_try_place_at ServerChest 623 30.5 -1199 nocost
```

## Screenshots

Each screenshot was copied from Valnet and visually inspected.

- [Automatic registration, successful delivery, and Dvergr appearance](images/server-chest/automatic-delivery.png)
- [Second placement rejected](images/server-chest/placement-limit.png)
- [Placement rejected after restart with the original chest unloaded](images/server-chest/unloaded-limit.png)

## Build and logs

`dotnet build` passed with zero errors and 84 existing warnings. No warning referenced the changed Server Chest files. `git diff --check` passed.

No new Server Chest exceptions or errors appeared during placement, delivery, restart, or replacement. The baseline retained Linux shader errors on the client and intro-video/render errors on the headless server. These did not prevent the tested flows. This was a one-client test; simultaneous placement from two separate accounts was not exercised.

## Restoration

The original plugin, configuration, patcher, and loader trees matched their backups when restored. A brief start through mmcli-agent confirmed that Valdev loaded its original PraetorisClient 0.1.65; that baseline start refreshed its normal configuration and translation files. Valdev was then stopped again. Prior profile selections were restored, temporary backups were removed, and Valnet client 02 was stopped. Test screenshots and command evidence were retained.
