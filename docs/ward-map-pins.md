# Automatic ward map pins

The small and large maps show a banded shield icon at each ordinary ward built by the current character. Disabled wards are included. Wards built by other players, Creature Owner Wards, and Network Wards are excluded.

Install the updated PraetorisClient on both the server and client. The server supplies ward positions across the whole world. The client does not need to visit a ward in the current session to see its pin.

When the local player spawns, the client requests one list of owned wards and subscribes to changes. The server adds pins when it receives a newly placed ward and removes pins when the ward's network object is destroyed. It sends each change only to the ward's owner. Unloading an area does not remove its pins.

There is no map-pin Update call, refresh timer, or recurring world scan. The initial list scans world ward data once per subscription. Placement and destruction send only the changed ward. A startup coroutine waits for the map and shield icon, sends the subscription, and then ends. If the request reaches the server before character data, the character-data event completes the pending request.

Pins are removed when their wards are destroyed. Reconnecting rebuilds the pin list. Switching character or world clears the old pins. Automatic ward pins are not saved as manual pins, cannot be deleted through the normal manual-pin action, and are excluded from cartography-table sharing. Existing manual pins are unchanged.

The icon comes from the `ShieldBanded` item. The feature does not change the ward prefab icon or the Forge of Potential icon.

![Two owned wards marked with shield icons during the Valnet test](images/ward-map-pins.png)

## Live validation procedure

Use Valdev and one Valnet client with the current production mod set and a development character.

1. Confirm that a character with no wards has no automatic ward pins.
2. Build two ordinary wards through the Hammer placement path. Confirm that both maps show shield pins, including while the wards are disabled.
3. Add a ward with another creator. Confirm that its position does not receive a pin.
4. Move far enough away to unload the owned wards. Confirm that their pins remain.
5. Log out and reconnect. Confirm that the pins return without duplicates.
6. Destroy an owned ward. Confirm that only its pin disappears when the server receives the destruction. Repeat with another player destroying the ward while its owner is far away.
7. Remove the remaining test wards. Confirm that the automatic pin list is empty.
8. Check client and server logs. Restore the original profile files and remove test helpers.

## Validation result — 2026-10-09

These live results apply to the earlier polling implementation at `b18e0b8`. They do not validate the later event-based revision.

Tested on `valnet-client-01` connected to Valdev with the deployed Season 8 8.0.39 plugin binaries and the candidate PraetorisClient build. All matching plugin DLL hashes were identical to production except PraetorisClient. Client test helpers were valheimCLI, Server Devcommands, and a temporary ward inspection plugin.

- A new development character started with zero pins.
- Hammer placement created two disabled wards and two shield pins. A third ward with another creator had no pin.
- Both pins remained at a distant position with zero loaded owned wards. Reconnecting there restored exactly two pins.
- Destroying one owned ward reduced the pin count to one. Removing the remaining fixtures reduced it to zero.
- Screenshots verified the large map and small minimap. Inspection confirmed `save=False` and the `shield_banded0` sprite.
- The Release build passed with no errors. The build reported 84 warnings in existing code.

The test logs contained no ward-pin exceptions. Other logs included Linux shader warnings, PieceManager icon-snapshot exceptions during session transitions, and an empty Discord Screenshots webhook error after the development character died. The server's shader and More Vanilla Build Prefabs `Trailership` warnings also occur on production. These unrelated messages mean the complete mod set does not have a clean log.

## Event-based revision checks

- Release build passed with zero errors and 84 existing warnings.
- A local harness compiled the actual `GuardStoneMapPins.cs` against game API stubs and passed 14 checks: one initial subscription, deferred character readiness, owner filtering, duplicate subscription handling, placement and destruction without world scans, no updates for ordinary object synchronization, disconnect cleanup, player teardown, new request IDs on reconnect, rejection of stale snapshots, and local-host initialization.
- Reviewed the game call paths for `Player.OnSpawned`, `Piece.SetCreator`, `ZDO.Deserialize`, `ZDOMan.HandleDestroyedZDO`, and character-data receipt. Area unloading does not use the destruction hook.
- Live Valnet validation of this revision remains pending, including destruction by another player while the ward owner is elsewhere. The offline harness does not execute Unity or Harmony patches.
