# PvP Arena Ward

Admins can select `PraetorisPvpWard` with Infinity Hammer:

```text
hammer PraetorisPvpWard
```

Place it like a normal ward. It starts enabled. Use the ward to enable or disable it. The server checks admin access for these requests. The prefab has no build-menu entry or crafting recipe. Normal clients reject non-admin placement and hammer removal.

The default radius is 40 metres. The server controls `[PvpWard] Radius` in `warpalicious.PraetorisClient.cfg`, from 1 to 100 metres. Distance includes height. A ward at dungeon height does not affect the ground below it.

Inside any enabled ward:

- PvP stays on and the PvP toggle is disabled.
- The player sees the **PvP Arena** status effect.
- Normal skill experience and positive grants through `Skills.CheatRaiseSkill` are blocked.
- Death keeps skill levels and accumulated experience. This also applies when the world uses `DeathSkillsReset`.

Leaving all enabled wards restores the previous PvP setting and normal skill rules. Disabling or removing the last ward has the same effect. Overlapping wards use the same area rule. Removing the status effect does not bypass the rule; the next player update restores the effect.

This ward does not provide building protection. It retains the base ward's appearance and normal damage behaviour. Tombstones, item drops, food loss, respawn, and other death behaviour stay under their existing rules. Install the candidate PraetorisClient build on the server and all clients before placing this prefab.

## Validation

Run the isolated player-policy checks:

```sh
dotnet run --project tests/PvpWardPolicy/PvpWardPolicy.csproj
dotnet build PraetorisClient.csproj -c Release
```

The policy checks compile the production player policy against small game substitutes. They cover PvP restoration, status removal, skill gating, death protection, and exception cleanup. They do not validate Unity prefab creation, Harmony patch execution, networking, or other mods.

Required live checks with the current Season 8 server and two clients:

1. Place the ward through Infinity Hammer as an admin. Confirm the normal build menu has no entry.
2. Confirm a non-admin cannot place or toggle the ward. Confirm an admin can toggle it and both clients see the same enabled state.
3. Enter with PvP off. Confirm the status icon, disabled PvP toggle, and damage between two players.
4. Record skill levels and accumulated experience. Attack, run, and jump inside. Confirm neither value increases. Leave and confirm experience increases again.
5. Die inside after the normal death cooldown. Confirm skills and accumulated experience stay unchanged. Confirm the normal tombstone and respawn flow completes.
6. Repeat with `DeathSkillsReset` enabled in the test world. Restore the world setting afterward.
7. Check exit with PvP originally off and originally on. Check overlapping wards, disabling the last ward, and destroying the last ward.
8. Place a ward at dungeon height. Confirm it affects nearby players but not players at the same ground coordinates.
9. Reload the world and verify the ward's saved enabled state. Inspect client and server logs for new errors.

Live validation was blocked on 2026-10-07: the Valnet SSH agent refused authentication. Valdev's installed mod versions were also behind the running production release (8.0.36). The Valnet machine was stopped and its lease released without changing profiles. Live placement, multiplayer combat, and compatibility with the production mod set remain unverified.
