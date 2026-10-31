## 0.1.114

- Update bundled Valheim Endgame to 1.3.0 (from 1.0.2). Update the server and clients together, and finish or close active encounters before updating. Custom YAML users must apply the new reward and event settings to their own files.
- Add the room altar interface: a tablet slot, the tablet's modifiers with their reward bonus, the reward budget, and each rarity's chance per roll.
- Reward rolls are the reward budget divided by the cost per roll, rounded up. Every totem type adds 5% reward budget per totem, and population modifiers scale with their strength.
- Give each player their own encounter rewards, collected from the Rewards tab at the Dungeon Master. Collecting no longer forces a character save.
- Add Fire, Fireball, Ice Shard and Mistile totems and remove mist totems. Totems roll up to four per wave, their effects stack, and hazard damage scales with tier. Frost and fire totems pulse every 4 seconds within 12 meters.
- Show a range ring around slowing, attack-slowing, Eitr and health totems, the totem's trophy as its status icon, and the protection totem's trophy above each monster it shields.
- Remove native loot from every creature that dies in an encounter room, including Splitter copies and boss summons. Splitter copies no longer split again or have their level reset.
- Fix a repeating stutter in totem rooms, a per-frame room music warning, and Ctrl + left-click to place a tablet on the altar.
- Avoid repeating the previous room within a theme, widen standard and support population ranges, add entrance sliding doors, and include updated rooms and tablet particles.

## 0.1.113

- Update bundled Valheim Endgame to 1.0.2.
- Remove extra prefabs from the shrine room.
- Correct the environment box height in Arcane Sanctuary 3.

## 0.1.112

- Update bundled Valheim Endgame to 1.0.1.
- Set Mistlands trial stars: Seeker 5–7, Seeker Soldier 3–5, Tick 6–8, Gjall 4–5, Queen 7–10. Each integer in the range has an equal chance, independent of altar distance.
- Craft the Mistlands Tablet with 10 Majestic carapaces and 3 Sealbreaker fragments at a level 1 Galdr table. Custom event YAML users must update their own configuration.

## 0.1.111

- Update bundled Valheim Endgame to 1.0.0.
- Add the trader quest panel, tier completion tracking, and quest reward delivery.
- New quest configurations award one tablet of the next tier for tiers 1–14, and five tier 15 tablets for tier 15. Existing quest configurations keep their rewards.
- Reduce encounter startup, room lookup, object tracking, environment checks, and cleanup work. Include encounter lifecycle fixes and updated room assets.

## 0.1.110

- Update bundled Valheim Endgame to 0.11.0.
- Add a default-off server setting, `[Features] EnableMistlandsTablet`, for Mistlands tablet crafting and use. Enable it and restart the server/world to make the trial available. Existing tablets are preserved.
- Expand the Mistlands trial to seven waves across three rooms: two normal waves and a Seeker Soldier miniboss, two normal waves and a Gjall miniboss, then the Queen. The first six waves contain ten creatures each. The Queen wave contains one boss.
- Award one random Legendary set runestone for Mistlands trial completion. Require Epic Loot 0.15.4 for its item-creation API.
- Craft the Mistlands tablet at a Galdr Table with one Queen drop and two Sealbreaker fragments. Use StarLevelSystem world rules at the source location for Mistlands creature stars.
- Remove Magic and Rare enchanting-material rewards and the Rare sword reward from regular encounters. Coin rewards give 100–150 coins per selected coin roll.
- Add Infested Arena 2 and include the updated Mine Trial room. Preserve Arcane Sanctuary 1's authored Mistlands thunder environment.
- Add encounter instructions and map-room discovery improvements. Recover encounter tombstones at the source altar and remove collected reward chests while retaining payment records.
- Require StarLevelSystem 1.29.0. Update the server and clients together, and finish or close active encounters before updating. Custom YAML users must apply the new encounter and reward settings to their own files.


## 0.1.109

- Update bundled Valheim Endgame to 0.10.0. Move combat rooms away from staging areas to reduce navigation work and heavy frames.
- Preserve encounter progress, shared attempts, rewards, and return links through native network state. Keep occupied rooms loaded and recover belongings when an encounter closes.
- Allocate separate encounter positions beyond the configured world boundary. Show players at the exterior location on the map while they are inside an encounter.
- Add configurable Serpent world-spawn exclusion around the starting stones, enabled by default within 500 metres. Existing creatures are unaffected.
- Update the server and clients together. Finish or close active Endgame encounters before the update.

## 0.1.108

- Update bundled Valheim Endgame to 0.9.0. Add Arcane Sanctuary 2, Arcane Sanctuary 3, and Grausten Jail 1 rooms.
- Add room catcher boxes that return living players and creatures that leave a room to a valid room spawn. Normal stage and exit teleports are excluded.
- Let administrators select a room with `ve_givetablet <tier> <theme> [room id]`.
- Let the local world host enter the Map Room while the Endgame beta gate is enabled.
- Add shipping port repair and rebuild commands that select a port by name or GUID and preserve its identity.
- Update the server and clients together. Finish or close active Endgame encounters before the update.

## 0.1.107

- Fix a startup exception caused by Epic Loot 0.15.1 removing the welcome-message setting used by PraetorisClient.
- Restore initialization of PraetorisClient features, including the ten-slot personal chest layout.
- Keep the bundled Valheim Endgame files unchanged from 0.1.106.

Update the server and clients together. Missing chest items can return when the expanded slots are restored if the chest inventory was not changed on the broken release. Items already removed from saved chest data require separate recovery; this update does not reconstruct them.

## 0.1.106

- Update bundled Valheim Endgame to 0.8.0.
- Require Epic Loot 0.15.1 and BepInExPack 5.4.2351. Remove the temporary tablet enchanting, socket restriction, and crafting-result patches from PraetorisClient; official Epic Loot now provides them.
- Cache encounter membership instead of parsing the saved player list during every membership check.
- Track active participation through portal entry, departure, death, and re-entry. Only players alive inside the encounter when it finishes receive completion credit.
- Apply player totem effects through native effect areas and status effects. Restore saved totem strengths after reloading.
- Add a mist totem and the Infested Arena room.
- Remove room-wide wet, freezing, rain, mist, hunger, and mead modifiers. Keep room environments and totem effects.
- Resolve saved hazard owners using the permanent encounter ID.
- Replace per-wave loot drops with configurable completion rewards in one shared chest. Only the server or player host deposits loot, with a saved encounter ID to prevent duplicate deposits.
- Add tablet theme autocomplete, `ve_nextwave`, and `ve_rewardodds` developer commands.
- Standardize room names and themes, update room layouts, enable Arcane Sanctuary, improve arcane gate interaction and feedback, and repair bundled scenery shaders.
- Remove fixed hazard placements. Hazards use configured spawn points.

Upgrade requirements: finish or close active encounters before updating. Update the server and clients together. Removed room effects and fixed hazards are not migrated. If custom configuration is enabled, use events.yaml schema 6, remove the old loot section, add rewards.yaml, and align rooms.yaml with the renamed room IDs and themes. Embedded configuration already includes these changes.

## 0.1.105

- Update bundled Valheim Endgame to 0.7.0.
- Add themed tablets, room retirement, and embedded encounter configuration.
- Show wave progress as "Wave X of Y".
- Remove Ghost Void from encounter pools and update room assets.
- Temporarily disable Arcane Sanctuary in the default room configuration.

## 0.1.104

- Update bundled Valheim Endgame to 0.5.4.
- Fix Silent Enclave bear-trap placement and clean up failed tablet preparation without consuming the tablet.
- Remove ShadowPerson from encounter pools and future balancing. Existing servers must also install the revised creature configuration.
- Additional Deep North effect and corpse cleanup cases remain under investigation in Endgame issue #78.

## 0.1.103

- Update bundled Valheim Endgame from 0.5.0 to 0.5.2, including the October 20 Tier 1 balance defaults. Deathsquito and Fuling Archer both use 11–13 stars. Existing servers must also install the reviewed creature configuration to apply these balance changes.
- Send encounter progress when an encounter completes and request leaderboard data when its view opens, instead of periodic leaderboard traffic.
- Keep three waves in each encounter room before moving players to the next room.
- Add four Dvergr arena rooms with animated door gates and correct the ice entrance.
- Fix portal control retries and arrival checks, and preserve wall spike contact damage.
- Let nearby players close failed or recovery encounters. Clean up leftover timed visual effects so they cannot keep recovery portals open, and clarify altar failure messages.
- Shorten the encounter monster-count label.

## 0.1.102

- Require server approval from the Endgame beta Steam ID list before entering MWL_MapRoom1. Leaving remains unrestricted.
- Add a server-synced Endgame beta enable setting that can restore normal Map Room entry without a restart.
- Reserve rights to original PraetorisClient and Valheim Endgame code and assets while preserving third-party license notices.
- Include Valheim Endgame 0.5.0 and its tablet configuration in this package.
- Allow tablets to be enchanted with the released Epic Loot 0.14.13, prevent new tablet sockets and shardstones, and restore the enchanting result display.
- Add Star Level System and YamlDotNet as package dependencies for Endgame.

## 0.1.101

- Allow everyone to gather loose flint, branches, and stones inside Protective Wards. Other pickables and ward protections are unchanged.

## 0.1.100

- Append active alternate biome names to the biome label beside the small minimap. The local `[Minimap] ShowAlternateBiomeName` setting defaults to true.
- Align alternate biome labels to the top so wrapped lines extend down inside the minimap border. Preserve the normal label alignment when no alternate name is shown.

## 0.1.99

- Fix tombstone access in local games and for the host of a multiplayer game. Reset locally delivered message data before reading requests and access decisions.

## 0.1.98

- Move the Server Guide button below Repair and use the same button style, with a clearer book icon.

## 0.1.97

- Add Extra Shots for crossbows and Dundr: Mythic grants one extra shot before reloading, and Ancient grants two. The effect is not available at lower rarities.

## 0.1.96

- Queue Adrenaline Echo activations and release them one at a time, with a 0.35-second delay before each Echo.
- Allow adrenaline gained from an Echo to queue more Echoes when it fills the adrenaline bar.
- Before releasing a queued Echo, check that the player is alive and still has the same weapon and Echo trinket equipped.

## 0.1.95

- Replace the Blood Oath legendary set with a Unique shardstone, available from Epic through Ancient rarity through shard loot and upgrades.
- Socketing Blood Oath into equipped gear reduces current health to 1 and blocks natural health regeneration. Maximum health is unchanged; potions and life steal still heal normally.
- Removing the shardstone or unequipping its item restores natural regeneration. Activating it again reduces health to 1. Healed health is preserved across saves and rejoining while the effect remains active.
- Automatically mark your ordinary wards on both maps with a shield icon, including disabled and distant wards. Load existing pins on spawn and update them through placement and destruction events without periodic polling.
- Keep ward pins separate from saved manual pins and cartography-table sharing. Install matching versions on the server and clients.

## 0.1.94

- Add the Blood Oath legendary set: Vestments for chest armor and Leggings for leg armor.
- Equipping both pieces reduces current health to 1 and blocks natural health regeneration. Maximum health is unchanged; potions and life steal still heal normally.
- Removing either piece restores natural regeneration. Completing the set again reduces health to 1. Healed health is preserved across saves and rejoining while the set remains equipped.

## 0.1.93

- Make group access to tombstones a creator opt-in. The local `[Tombstones] AllowGroupTombstoneAccess` setting defaults to false and is not synchronized by the server.
- Check the creator's choice in the existing group confirmation RPC. The server must permit group access, and both players must be connected and in the same group. Changes apply on the next interaction.
- Keep creator access and the separate admin exception. Add no periodic network messages.
- Install matching versions on the server and all clients. Older creator clients do not check the new opt-in setting.

## 0.1.92

- Restrict tombstone access to the creator by default. Admins and confirmed members of the creator's Groups group can also access it. Each exception has a server setting.
- Check permissions when players open, take all, or stack items. The feature sends no periodic network messages.
- Install matching versions on the server and clients. Remove the old Expand World Prefabs tombstone restriction script before enabling these overrides.
- When using ValheimRcon with Groups, disable its solo chat relay and restart the server, or install the group membership fix from ValheimRcon PR #9.

## 0.1.91

- Add Molten Cores as preferred boat fuel when both core types are in storage. Show the active fuel icon.
- Molten fuel force values: reverse 2.0, rowing 1.02, half sail 1.4, full sail 2.0. Each core supplies 300 seconds of active motor use.
- Preserve the active fuel type when ship control changes or the world reloads. Keep existing Surtling fuel values.

## 0.1.90

- Add an optional server-controlled setting for free departures from one MWL shipping port. Travel to that port retains its normal gold cost. The feature is disabled by default.
- Add `shippingport_free guid|set|clear|status` administrator commands. Open the port panel and use `shippingport_free set` to select that port. The selection survives restarts and port name changes.
- Install matching versions on the server and clients to use this feature.

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
