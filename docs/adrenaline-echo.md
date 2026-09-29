# Adrenaline Echo

Adrenaline Echo is a Unique shardstone for trinkets. Its permanent shard ID is
`0x50520006`. It uses the existing Stormcaller shard visuals and drop distribution.
Epic, Legendary, Mythic, and Ancient shards each grant the same effect.

When the equipped trinket activates its full-adrenaline status effect, the shard
creates the equipped weapon's projectile at the player's feet and immediately
triggers its impact. It does not travel toward the player's aim or an enemy.
Staff of Embers therefore explodes around the player, using its normal area
radius and enchanted damage. The weapon must have a projectile primary attack.
Bows and crossbows need compatible ammunition
in the inventory. Melee weapons and empty hands do not create a projectile.
Projectiles without area damage do not gain an invented damage radius.

The bonus shot costs no ammunition, durability, stamina, health, or eitr. It does
not change the current weapon's reload state or interrupt its attack animation.
Epic Loot multishot can increase the projectile count through its normal roll.

The shot uses the actual equipped `ItemData` and the normal patched attack and
projectile setup methods. This preserves weapon damage enchantments, projectile
modifiers, and the weapon identity that Epic Loot uses when the projectile hits.
Projectile speed has no effect because the projectile has zero velocity.
The impact cannot hit its owner. All projectile setup and burst hooks finish
before impact so effects added late by Epic Loot remain available.
Attack hooks use an isolated copy of the weapon's attack definition. Epic Loot's
pending multishot state is restored after the bonus shot.

The activation hook runs at the game's full-adrenaline equipment activation
branch. It also covers a fill from zero and a refresh of an existing trinket
effect. Partial gains and adrenaline loss do not activate the shard. The shot
runs on the next frame to avoid nested changes to shared weapon damage. If the
player dies or switches weapons before that frame, the shot is cancelled.

## Validation

Run the live test with the currently deployed Praetoris client mod set and the
candidate DLL. Socket the actual shard prefab, `1347551238_Epic_ShardStone`, into
a magic trinket that has a full-adrenaline status effect.

- Fill adrenaline with an unenchanted Staff of Embers equipped. Confirm one
  immediate impact at the player's position, with zero projectile velocity.
- Fill again while the trinket effect is active. Confirm one more impact.
- Repeat with damage, frost damage, and projectile speed enchantments. Inspect
  the projectile damage, and confirm the impact retains frost damage and remains
  at the player even with a projectile speed enchantment.
- Place enemies on both sides of the player and one outside the explosion radius.
  Confirm nearby enemies take damage, the distant enemy does not, and the player's
  health does not change with god mode disabled.
- Check partial gains, adrenaline loss, an unsocketed trinket, melee weapons,
  empty hands, and a bow without ammunition. Confirm no bonus shot.
- Check a bow with ammunition, a staggered player, and the Double Magic Shot
  effect. Confirm ammunition and weapon durability remain unchanged.
- Compare startup and test logs with the unmodified production baseline.
- Capture and inspect images and a video of the real activation and impact.

This is a client feature. A local Valnet proof world can test the firing and
damage paths. It does not prove remote target ownership or multiplayer behavior.

### Recorded result — 2026-09-28

Built in Release and tested on `valnet-client-02`, in local `TestingWorld`, with
Valheim 1.0.16 and the deployed Season 8 release 8.0.29 (Epic Loot 0.14.13).
Thirty shared DLLs matched production hashes. PraetorisClient was the candidate.
The maintained profile needed temporary repairs to reach this baseline.

All 12 live cases passed: partial gain, drain, plain staff, status refresh,
enchanted staff, stagger, no shard, melee, unarmed, missing ammunition, bow,
and Double Magic Shot. Every bonus projectile had zero speed and zero distance
from the player. Every impact occurred at the player's position. The staff
projectile kept the real weapon reference and its 3-metre explosion radius.
The forced Double Magic Shot roll produced two immediate impacts.
Weapon durability and ammunition did not change. Resource checks account for
normal food cap decay.

The area test hit both Trolls placed 3 metres to either side of the player and
did not hit the Troll placed 15 metres away. Both hits carried fire and frost
damage of 90.55515 each. Player health did not change, with god mode disabled.
The four shard rarity prefabs registered, and the socket reported Unique category.
The proof uses real `Player.AddAdrenaline` calls to activate the trinket. It does
not test every way that combat can grant adrenaline or every Epic Loot effect.

The final log contained existing Linux shader errors, Jotunn command constructor
errors, and location registration errors. No Adrenaline Echo exception occurred.
An enemy killed the character during a rejected recording attempt. The existing
death-screenshot mod then logged an empty webhook error. The accepted recording
uses god and ghost modes for staging; the separate area test used neither.
The maintained profile had pre-existing Epic Loot patch errors before the test.
This older profile still needs separate maintenance; a clean restored startup
is not claimed.
After the restored-startup check, all 483 original profile files and both profile
metadata files were restored exactly. Test plugins and temporary backups were
removed. OBS scene inputs and its recording path were restored.

Evidence is stored outside the repository at:
`/Users/benjmarston/Develop/valheim-validation-evidence/adrenaline-echo-aoe-20260928`.
The original `adrenaline-echo-20260928` folder contains the superseded travelling
projectile proof. Use the `aoe` folder for this behavior.
The accepted `adrenaline-echo-aoe-proof.mp4` is 6.883 seconds at 1080p/60 fps.
The explosion occurs near 3.2 seconds. `player-centered-explosion.png` shows the
impact. The video contact sheet and both equipment tooltips were inspected
manually because the automated review models were unavailable.

### Repeat the live probe

Build `tests/AdrenalineEchoProbe/Probe.csproj` in Release. Install its DLL only
in the leased test client's profile beside the candidate. The main project
excludes the probe source. Use a disposable character in a local proof world.
`seed` adds food, a staff, and a socketed trinket; `suite` changes that inventory.

```text
cli_clear_inventory
puke
echo_probe seed
echo_probe suite
```

Wait for `ECHO_PROBE SUITE_DONE` in the BepInEx log. Require 12 `CASE` lines
with `PASS`, and inspect the projectile and impact lines. `echo_probe fill`
activates full adrenaline for manual proof. Remove the probe after testing.
`echo_probe area` creates two nearby Trolls and one distant Troll, then checks
the area damage and player health. `echo_probe cleanup` removes these targets.
