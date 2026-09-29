# Adrenaline Echo

Adrenaline Echo is a Unique shardstone for trinkets. Its permanent shard ID is
`0x50520006`. It uses the existing Stormcaller shard visuals and drop distribution.
Epic, Legendary, Mythic, and Ancient shards each grant the same effect.

When the equipped trinket activates its full-adrenaline status effect, the shard
fires one projectile from the equipped weapon toward the player's aim. The weapon
must have a projectile primary attack. Bows and crossbows need compatible ammunition
in the inventory. Melee weapons and empty hands do not create a projectile.

The bonus shot costs no ammunition, durability, stamina, health, or eitr. It does
not change the current weapon's reload state or interrupt its attack animation.
Epic Loot multishot can increase the projectile count through its normal roll.

The shot uses the actual equipped `ItemData` and the normal patched attack and
projectile setup methods. This preserves weapon damage enchantments, projectile
modifiers, and the weapon identity that Epic Loot uses when the projectile hits.
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

- Fill adrenaline with an unenchanted Staff of Embers equipped. Confirm one shot.
- Fill again while the trinket effect is active. Confirm one more shot.
- Repeat with damage, frost damage, and projectile speed enchantments. Inspect
  the projectile damage and speed, and confirm the impact retains frost damage.
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
and Double Magic Shot. The staff projectile kept the real weapon reference.
Its speed increased from 20 to 40 with the speed enchantment. Frost damage
was present in both projectile setup and Troll impact logs. The forced
Double Magic Shot roll produced two projectiles. Staff durability and ammunition
did not change. Resource checks account for normal food cap decay.

A saved socketed trinket fired an enchanted projectile after a game restart.
The four shard rarity prefabs registered, and the socket reported Unique category.
The proof uses real `Player.AddAdrenaline` calls to activate the trinket. It does
not test every way that combat can grant adrenaline or every Epic Loot effect.

The final log contained existing Linux shader errors, Jotunn command constructor
errors, and location registration errors. No Adrenaline Echo exception occurred.
The original profile files and metadata were restored after testing.
All 483 original file hashes matched, with no extra files. A restored-profile
startup reproduced its pre-existing Epic Loot patch errors. This older profile
still needs separate maintenance; a clean restored startup is not claimed.

Evidence is stored outside the repository at:
`/Users/benjmarston/Develop/valheim-validation-evidence/adrenaline-echo-20260928`.
The main files are `final-proof-run.log`, `first-proof-run.log` (video impacts),
`baseline.json`, `restoration.json`, `trinket-tooltip.png`, `staff-tooltip.png`,
`bonus-projectile.png`, and `adrenaline-echo-proof.mp4`.
The 1080p/60 fps recording shows the bonus activation and fireball near 11 seconds.
The images and video contact sheets were inspected manually. The automated visual
review helper could not run because its requested models were unavailable.

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
