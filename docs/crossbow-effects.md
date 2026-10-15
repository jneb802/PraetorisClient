# Crossbow effects

The effects are separate from their acquisition rules. `CrossbowEffectDefinitions`
registers the effects and Arbalist set. `PraetorisShardstones` registers the two
Unique shardstones.

| Effect | Acquisition | Initial values |
| --- | --- | --- |
| Recoil Dash | Unique shardstone, ID `0x50520009` | Epic 200%, Legendary 225%, Mythic 250%, Ancient 300% recoil force |
| Heavy Load | Arbalist, 2 pieces | One extra loaded shot; twice the reload duration |
| Active Reload | Arbalist, 4 pieces | Ability 1 during 50–65% of the reload; immediate completion and +35% damage on the next shot |
| Grappling Hook | Unique shardstone, ID `0x5052000A` | Crossbow special attack; native hook stamina cost; no bolts spent |

Arbalist has helmet, chest, legs, and crossbow pieces. Heavy Load supports values
from one to three extra shots. Each extra shot adds one normal reload duration.
It stacks with Extra Shots. Active Reload permits one attempt per reload. An
early or late attempt leaves the normal reload running. The damage bonus applies
to one shot, belongs to that weapon, and is cleared when its loaded state resets.

Recoil moves the player opposite the firing direction. Its distance depends on
equipment movement modifiers, terrain, and other normal movement conditions.
Grappling Hook uses the game's hook projectile and rope. Block releases the rope.
Switching weapons or removing the effect also releases it. The hook does not
inherit the weapon's offensive shot effects or consume the crossbow's loaded shots.
The native pull velocity is preserved after ground steering so a shallow rope
pull can move the player. Terrain and collisions still use normal physics.

## Ammo selection fix

The reproduced failure occurs when a player chooses different compatible bolts
during the firing animation. Native equipment handling rejects that input. For
crossbows with multiple loaded shots, the fix remembers the latest explicit
choice and applies it after the animation through normal equipment handling.
It discards stale choices and choices invalidated by a weapon switch or removal
of the ammo from inventory.

The reported persistent lock during a full reload or after a weapon switch was
not reproduced. Do not describe that broader report as conclusively fixed.

## Validation

Live checks use Valnet client 01, local TestingWorld, with the maintained
Praetoris Season 8 profile aligned to production release 8.0.43. The production
baseline has PraetorisClient 0.1.97, Epic Loot 0.14.13, and Valheim 1.0.17.
SaveCrossbowState 1.0.4 was added for reporter compatibility testing.
The client's profile metadata still said 8.0.34. Common mod DLL hashes and the
relevant Epic Loot data files were compared with production and aligned for this
test. Original files were backed up for restoration. These are local client
checks; they do not constitute a two-client multiplayer test.

The reporter's crossbow effects and sockets were recreated from the saved item.
The lost ammo choice was reproduced before the fix and accepted after it.
Ammo changes between shots, during a full reload, and after a weapon switch
also worked. SaveCrossbowState does not read or replace selected ammo.

Two actual Arbalist pieces changed the measured reload from 3.483 to 6.965 seconds
and allowed two shots before reloading. Four actual pieces enabled Active Reload.
A timed input completed the reload; the next attack multiplier was 1.350 and
the following one was 1.000. An early input used the attempt and could not be
retried during the same reload.

An Epic recoil shard moved the test player approximately 3.1 metres compared
with 1.6 metres without the shard, from the same position with the same gear.

The grappling hook pulled the player 13.5 metres on flat ground. The crossbow
kept its loaded state and all 100 bolts. This test included Extra Shots,
Triple Shot, and Ammo Conservation on the same weapon. The normal hook's
ground-steering problem was reproduced separately with the native hook item.
Block and switching to a sword both released the rope.
With only Extra Shots and the hook, all three loaded shots remained available
after grappling: bolt counts were 100, 99, 98, and 97, with reload starting on
the third normal shot. Special attack also interrupted a running reload without
spending bolts; blocking released the rope and normal reloading resumed.

The client has a key conflict: Epic Loot defaults Ability 1 to G, while this
Valheim version uses G for the radial hotbar. Validation uses F6 for Ability 1.
The effect reads the configured Epic Loot key and does not change player bindings.

The build passes. Its 84 warnings predate this change. Runtime log comparison
found the baseline shader messages, asset-name warnings, Deathlink fallback
warning, Autosorter compatibility warning, and world-generation timing-patch
warnings. No new crossbow exception was found during the completed checks.

All five captioned 1080p/60 fps demo exports passed visual review and include
game audio: ammo selection, Recoil Dash, Heavy Load, Active Reload, and Grappling
Hook. The hook demo uses the real special-attack and block mouse buttons.

Cleanup removed the three test props and test character. All 35 original DLLs
and 254 config files were verified against their saved content or hashes.
The optional test mod was removed, and the original preferences, Ability 1
binding, OBS recording directory, and active profile were restored.
