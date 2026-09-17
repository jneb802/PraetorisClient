# Praetoris shardstones

These items extend Epic Loot's shard system. Install PraetorisClient on every
participating client and server. The integration targets Epic Loot 0.14.8.

| Shard | Category | Magic | Rare | Epic | Legendary | Mythic | Ancient |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Point Blank | Core | +5% | +7% | +10% | +15% | +20% | +25% |
| Reload on Kill | Unique | — | — | Enabled | Enabled | Enabled | Enabled |
| Piercing Shot | Unique | — | — | 2 | 3 | 3 | 4 |
| Arrow Rain | Unique | — | — | Enabled | Enabled | Enabled | Enabled |

Point Blank uses fixed values per rarity, taking the upper value of the regular
effect's balance range. The three unique shards have no Magic or Rare variants;
those rarities cannot drop or be crafted for these shards.

Point Blank affects bow and crossbow projectiles. Its listed damage bonus applies
within 2 metres of launch. Damage decreases linearly to a fixed −25% penalty at
20 metres. Longer shots retain the −25% penalty. Range uses the distance from the
launch position to each hit, so movement and weapon changes after firing do not
change the result. Each pierced target receives its own distance calculation.
Epic Loot's existing rules control stacking across sockets and equipment.

Reload on Kill reloads the current crossbow after a projectile kill. Piercing
Shot's value is the number of enemies a projectile can pass through before its
final stopping hit. Epic Loot permits only one equipped Unique-category shard,
including its existing unique shards. The three unique shards therefore cannot be
equipped together. All four effects are shard-only and cannot roll, augment, disenchant,
or become runestones. Existing gear with the old effects continues to function.

Arrow Rain grants Hrafnstorm: an arrow impact calls down ten spectral arrows,
each using 50% of the triggering projectile's original hit damage, with a
10-second cooldown. Higher rarity does not change its power or cooldown.

Point Blank joins the normal shard loot pools using Orange's weights and rarity
distribution. The three unique shards join the unique pool using Firewalker and
Stormcaller's weights. There are 18 item variants and 14 upgrades. Each upgrades through its available
rarities using the existing shard upgrade costs: one preceding shard and 4–8
enchanting shards of the preceding rarity. The items reuse existing shard icons.

## Saved identities

Epic Loot currently has no public API for registering new shard types. This
integration adds definitions through its native configuration dictionaries and
registers items through Jotunn. Numeric enum values allow the existing socket
serialization, reconstruction, tooltip, and exclusive-category code to operate.
The config and loot hooks also run after config reload or server synchronization.

| Shard | Permanent numeric type |
| --- | --- |
| Point Blank | 1347551233 |
| Reload on Kill | 1347551234 |
| Piercing Shot | 1347551235 |
| Arrow Rain | 1347551237 |

Prefab names follow Epic Loot's required format, for example
`1347551233_Ancient_ShardStone`. Never renumber or reuse these saved identifiers.
The retired numeric type 1347551236 must remain unused.
Removing this mod removes its item prefabs and definitions; retain it when
loading saves containing these shards. In-game names use “Shard of” followed by
the name in the table, with the rarity prefix.
