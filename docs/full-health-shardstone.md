# Full Health shardstone

Initial feature implementation. Name, appearance, and balance values are provisional.

- Permanent ID: `0x50520007`. Never renumber or reuse this ID.
- Category: Core. Multiple copies can be equipped under Epic Loot's normal socket rules.
- Uses White shard items as appearance and loot templates.
- Supports Magic, Rare, Epic, Legendary, Mythic, and Ancient.
- Each effect uses 5%, 7%, 10%, 15%, 20%, and 25% respectively.
- Uses the existing shard upgrade recipe registration.

| Gear group | Bonus |
| --- | --- |
| Melee weapons | Increased damage from this weapon |
| Ranged weapons | Increased projectile damage from this weapon |
| Magic weapons | Reduced attack eitr cost for this weapon |
| Shields | Increased block armor for this shield |
| Armor | Increased stamina regeneration |
| Trinkets | Increased positive adrenaline gain |
| Utility | Reduced running stamina cost |

The player must be alive and within 0.01 health of their current maximum health.
Damage checks health when the attack creates damage. Projectiles retain that damage after launch.
Other effects check health when the relevant calculation runs.
Weapon and shield bonuses use only that item's effects. Other bonuses use equipped effects.
Copies add together under Epic Loot's configured shard stacking rules.
Eitr and running cost reductions from this shard are capped at 80%. Other mods can modify the resulting costs.
Adrenaline loss is unchanged. Eitr discounts apply to attack cost queries, including affordability checks.

## Local validation

```sh
dotnet build -c Release
dotnet run --project tests/FullHealthShard/FullHealthShard.Tests.csproj -c Release
```

The handler checks compile the production effect code with small substitutes for the game and Epic Loot API.
They cover health thresholds, death, each effect, damage group selection, cost caps, negative adrenaline,
unequipped block items, non-player regeneration, and effect definitions. They do not run Harmony or Unity.

## Required live validation before release

Use the current Praetoris Season 8 baseline and record the server and client profile versions.

1. Confirm all six items register, display their effects, and survive save/load while socketed.
2. Confirm each gear group selects its effect, including crossbows, staffs, armor slots, and modded utility items.
3. Measure each bonus at full health, below full health, and after healing or changing food.
4. Confirm ranged damage remains fixed when health or equipment changes after launch.
5. Confirm one weapon cannot receive another weapon's bonus.
6. Confirm attack affordability and eitr payment agree near the discounted cost.
7. Confirm multiple armor shards follow the configured stacking rules and cost reductions respect the cap.
8. Confirm upgrades, loot inclusion, tooltips, configuration reload, and server configuration synchronization.
9. Confirm multiplayer damage and blocking with the production mod set.

Live validation is pending. This branch is not a production release.
