# Spawn island ward restriction

This feature blocks new vanilla protective wards (`guard_stone`) inside a saved
spawn island boundary. The placement preview turns red. A placement attempt shows
`You cannot build wards on spawn island.` and returns before the game creates the
ward or consumes its materials. Other building pieces, Creature Owner Wards, and
Network Wards are unchanged. Existing wards are not removed.

The server also rejects newly received ward objects inside the boundary. This
uses the existing guard stone receive checks. A client that bypasses placement
checks can lose its materials when the server rejects its ward. Both server and
clients must run this version for the normal placement message and prevention.

## Enable or disable

Use the server config:

```ini
[SpawnIslandWards]
Enabled = true
```

The default is `true`. Set `Enabled = false` to disable the spawn island rule
on the server and connected clients. The saved boundary remains unchanged.
Set it back to `true` to use the same boundary again. Changes apply after config
reload without a restart. Other ward rules, including the player build limit,
still apply. When disabled, invalid or wrong-world boundary data does not block
placement through this rule.

## Once per season

Run these commands on the server with the season world loaded. They do not accept
activation requests from ordinary connected clients.

1. Run `spawnisland_generate` (default scan radius: 2,048 metres).
2. Wait for the completion message. The survey runs over multiple frames.
3. Inspect `BepInEx/config/PraetorisClient/spawn-island/<world-id>.boundary.svg`.
   Gold shows the protected area. Blue shows the unprotected area, not necessarily
   water. North is up. The red cross marks the spawn cell. The SVG description
   gives the southwest world coordinates and cell size.
4. Compare the shape with the season's world map. Check rivers, narrow channels,
   offshore islands, and any custom terrain. Do not activate an incorrect shape.
5. Run `spawnisland_activate` only after the boundary is correct.
6. Run `spawnisland_status`. Verify it reports an active boundary for this world.
7. Back up the mod config and the `.boundary` and `.svg` files with the season world.

Activation stores the compressed map in the server-controlled, synchronized
`SpawnIslandWards.Boundary` config entry. Clients receive the saved map. Neither
joining, restarting, nor changing terrain recalculates it. A second generate or
activate command will not replace an existing boundary for the same world.

If the island reaches the scan limit, the survey saves nothing. Retry explicitly
with `spawnisland_generate 4096`. If it still reaches the limit, do not use a
truncated boundary. A different boundary method is needed for that world.

At season rollover, load the new world and repeat generation, review, and
activation once. The world ID prevents reuse of the old world's boundary. Keep
the old files as the previous season's record. World copies that preserve the
world ID share the same boundary, including a production world copied to Valdev.

## Terrain limitations

The survey samples the world generator's terrain on an 8-metre grid, starting at
the original `StartTemple` location. It follows dry land through shared cell
edges, fills enclosed lakes, and adds one cell around the coast. The coastal
margin includes nearby water and can include land across a very narrow channel.
The saved boundary applies at every height, including buildings above the island.

This is an approximation that requires the one-time map review. Channels and land
bridges narrower than the grid can be missed. A river can divide land that an
operator considers one island. Player terrain changes, imported terrain, and
custom islands are not reliably represented by the world generator. Such worlds
need a separately verified boundary method; this version does not provide manual
boundary editing. A missing spawn location or an underwater spawn sample stops
generation instead of guessing.

An empty boundary config entry also disables this rule. When `Enabled = true`, a nonempty but invalid entry, or an
entry for another world, blocks all new protective wards until corrected. The
status command explains this state. Use `Enabled = false` to disable the rule
without clearing the boundary. Do not edit the encoded map by hand.

## Validation

Run the standalone geometry and persistence checks:

```sh
dotnet run --project tests/SpawnIsland/SpawnIsland.Tests.csproj --configuration Release
dotnet build --configuration Release
```

The [2026-10-01 live validation report](spawn-island-validation.md) records the
Valdev and Valnet results with Season 8 release 8.0.31. For future validation,
claim both devices and record the release versions
before replacing any files. Back up the candidate DLL/config destinations and
record all new boundary files. Restore and verify them after testing.

Required live checks:

- Generate the actual world boundary and compare the preview with its coastline.
- Activate it on the server and verify the client receives it.
- Set `Enabled = false` and reload the server config. Verify client placement
  and server receipt are allowed inside the boundary, and the boundary is unchanged.
  Set it back to `true`, reload, and verify both checks block inside placement again.
- Place a ward well inside the island. Verify a red preview, the correct message,
  unchanged materials, and no ward object on the server.
- Repeat at multiple coastline points, across a river, and on a nearby island.
- Place a ward outside the boundary. Verify normal placement and material use.
- Place ordinary building pieces and utility wards inside the boundary.
- Reconnect the client and restart the server through the mmcli-agent API. Verify
  that the saved boundary is unchanged and no terrain survey starts.
- Retry generation. Verify it preserves the saved map.
- Change terrain in the test world. Verify it does not change the restriction.
- Submit a new ward through the server receive path without the client check.
  Verify rejection inside and acceptance outside the boundary. Preserve preexisting
  wards in both areas.
- Verify that an invalid or different-world config blocks new wards with the
  setup message. Restore the correct config afterward.
- Inspect both logs for new errors and exceptions. Restore the original profile
  files and remove test additions before releasing device leases.

Local checks do not prove Harmony patch behavior, config synchronization, server
object rejection, or the actual season coastline. Those remain live-test claims.
