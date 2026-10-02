## 0.1.89

- Update the required Epic Loot version to 0.14.13 and Jotunn version to 2.30.2.

## 0.1.88

- Hide boss spawn announcements, including Eikthyr and the Elder. The server setting is enabled by default and locked against client overrides.
- Restrict player-built boat steering and storage to the builder and players who entered its password. Saved access survives password changes and restarts.
- Add an optional spawn island vanilla ward restriction. It defaults to disabled. Generate and review the boundary once per world before enabling it.
- Install matching versions on the server and clients.

## 0.1.87

- Enter a boat password once per character. Saved access covers both the helm and storage and remains until the boat is destroyed, including after password changes and server restarts.
- Save creator access when setting a boat password. Reject unauthorized storage requests.
- Remove the duplicate ship ownership patch and let NetworkPerformanceSystem handle helm ownership.
- Refresh Server Chest contents before withdrawals so deliveries received while the chest is open are not overwritten. Require a new selection if a delivery replaces a selected item.
- Install matching versions on the server and clients.
- Known NPS limitation: transferring ownership to the player steering can hide another player's open boat chest panel, including on boats without passwords. Release the helm and reopen storage; saved access is retained.

## 0.1.86

- Remove the Server Chest 64-slot limit and add scrolling rows as deliveries arrive.
- Preserve large chest saves beyond the game's row-coordinate limit.
- Refresh deliveries while the chest is open.
- Return clear send/status errors when chest contents cannot load, without overwriting stored data.
- Install matching versions on server and clients. Older versions cannot read saves above 2,048 stacks. Very large inventories still use more memory and take longer to render.

## 0.1.85

- Limit each player to one Server Chest per world, including unloaded areas.
- Register new Server Chests automatically for item delivery from server admins.
- Use the Dvergr treasure chest appearance. Build with 10 Wood near a Workbench.
- Disable inherited treasure loot, treasure-discovery statistics, and empty-chest destruction.

## 0.1.84

- Add Adrenaline Echo, a Unique trinket shardstone available at Epic, Legendary, Mythic, and Ancient tiers.
- When the trinket activates its full-adrenaline effect, trigger the equipped weapon's projectile area impact at the player's position. Staff of Embers creates an explosion around the player with its Epic Loot damage effects.
- Require a weapon whose own projectile has area damage. The effect costs no durability, stamina, health, or eitr.

## 0.1.82

- Save the builder's character name on newly built ships and carts so their owner labels work when the builder is offline or far away, including after a server restart.
- Keep the existing name lookup for ships and carts built before this update. Saved names reflect the character name at construction time.

## 0.1.81

- Limit each player to a configurable number of vanilla wards. The default limit is 5. Existing wards remain when the limit is reduced.
- Show your world-wide ward count and the server limit when you hover over a ward, while preserving Protective Wards hover text.
- Install this version on both the server and clients to use the ward count display.

## 0.1.80

- Recreate the Ancestral Slam prefab after logging out and joining a world again.
- Restrict Ancestral Slam triggers to hits with a supported combat skill.

## 0.1.79

- Remove the Withering Bomb from the live package. The feature remains in development on a separate feature branch.

## 0.1.78

- Restrict Effect Duration magic-effect rolls to supported status-effect staves, including the Staff of Protection, and to trinkets.
- Move the Server Guide icon away from the crafting-station level display and fix its crafting-window input handling.
- Hide Epic Loot's configuration-choice window on the main menu.
- Add the Withering Bomb, which prevents enemy health regeneration for a configurable duration. Craft three at a Black Forge with 1 Bilebag, 1 Sap, and 3 Resin.
- Update the required Jotunn version to 2.30.1.
