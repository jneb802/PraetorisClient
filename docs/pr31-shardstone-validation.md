# PR 31 shardstone validation — 2026-09-17

Environment: Valnet Client 02, local `TestingWorld`, character `PR31Test`.
The isolated `pr31-magic-effects-20260917` profile uses Praetoris Season 8
8.0.21, Epic Loot 0.14.8, Jotunn 2.30.0, and Valheim Steam build 25364265.
The candidate replaces only PraetorisClient; a separate temporary plugin measures
native socket operations and projectile damage. No production server was changed.

## Initial live results

- All 14 item variants registered, had icons, and accepted native socketing.
- Native reconstruction returned the matching prefab for every rarity and type.
- Point Blank socket values were 10, 16, 22, 28, 34, and 40.
- A Reload on Kill socket caused a second unique shard to be rejected with
  `socket_uniquelimit`.
- All three effect definitions had `NoRoll=true`, `CanBeAugmented=false`,
  `CanBeDisenchanted=false`, and `CanBeRunified=false`.
- The first registration probe ran before world loading finished and failed.
  Repeating it after `inWorld=true` passed all 14 variants.

Initial bow hits with an Ancient Point Blank shard used provisional 5/30-metre
thresholds. The user subsequently selected 2/20 metres, so the following numbers
record the earlier implementation and do not establish the final curve:

| Distance from launch to impact | Original projectile damage | Damage at impact | Multiplier |
| --- | --- | --- | --- |
| 1.408898 m | 30.95405 | 43.33567 | 1.400000 |
| 15.43331 m | 35.29330 | 39.83674 | 1.128734 |
| 35.80433 m | 52.23919 | 39.17939 | 0.750000 |

A bow with Point Blank and Piercing Shot hit two aligned Trolls with one arrow.
The original projectile damage was 41.66218. The first impact at 3.408973 metres
used 58.32705 damage (1.4×). The second at 15.43331 metres used 47.02551 damage
(1.128734×). Both Trolls lost health. Repeated collider callbacks did not compound
the distance multiplier.

The first build exposed missing effect rarity metadata: Point Blank incorrectly
required breaking the shard to remove it under the profile's `BreakValueless`
setting. The final implementation uses the same value arrays for the effect
metadata and shard definitions. Reload on Kill remains a binary effect.

## Metadata, save, and reload checks

The next full restart preserved the bow's Ancient Point Blank socket at 40 and
Epic Piercing Shot socket at 2. All 14 socket/reconstruction checks passed again.
Point Blank and Piercing Shot now had `Free` removal at every supported rarity;
Reload on Kill retained `BreakOnly` under the profile's `BreakValueless` setting.

Removing the three definitions from the live config and reinitializing it
restored all three. Removing the custom loot entries and reinitializing the loot
config restored five entries in this profile's active pools. A second reload
kept the count at five. All 11 upgrade recipes were present with the correct
preceding shard and enchanting-shard cost.

A Magic Point Blank bow hit at 1.408873 metres used 1.1× damage. A crossbow with
Ancient Point Blank and an Epic Reload on Kill shard killed a one-health
Greydwarf. The first post-shot sample showed ammunition reduced from 100 to 99,
`loaded=True`, and `inAttack=True`, proving the socketed effect reloaded during
the attack. The first fire command only completed the initial load; the test
repeated the shot after the crossbow reported loaded.

## Range validation build: user-selected 2/20-metre range

Release build: zero errors; 85 existing compiler/build warnings. No warnings
reference the new shardstone or Point Blank classes.

SHA-256: `3005001f4097b3204f65f3b56e3d7b965f87fac9fb5e46617bbc656f23cf19f7`.

One real bow shot with Ancient Point Blank and Epic Piercing Shot passed through
three aligned Trolls. All three lost health. The projectile's original damage
was 49.77823:

| Impact distance | Damage at impact | Multiplier |
| --- | --- | --- |
| 1.26696 m | 69.68952 | 1.400000 |
| 9.415281 m | 56.36021 | 1.132226 |
| 25.52417 m | 37.33367 | 0.750000 |

These values match the final 2/20-metre curve. Each hit used the original damage;
the multiplier did not accumulate across targets or repeated collider callbacks.

The conversion to a uniform unique shard also requires a runtime weapon check:
Piercing Shot now explicitly accepts only Bows and Crossbows. Previously its
enchantment requirements supplied that restriction. The final bow proof passed
with this check in place. The attempted negative Staff of Frost check did not
produce a projectile in this profile, including after temporarily removing its
eitr/stamina costs and repairing it. Staff exclusion therefore remains unverified
in a live cast; absence of a projectile is not counted as a passed test.

The final gameplay log window contained no new warnings or errors. The previous
save/reload run logged the expected `Local player destroyed` warning at logout.

Evidence directory:
`/Users/benjmarston/Develop/valheim-validation-evidence/pr31-shardstones-20260917/`.

These are local controlled tests. Dedicated-server synchronization, multiplayer
combat, every terrain collision, natural loot frequencies, and upgrade-table UI
interaction require separate coverage. Existing profile startup warnings from
other mods and platform shaders remain; see the earlier magic-effect report.

## Follow-up: Siedrweaver and Arrow Rain unique shards

Added unique Siedrweaver and Arrow Rain shards at Epic through Ancient rarity.
All five shard effects now explicitly block normal rolls, augmentation,
disenchantment, and runestone creation. The player-facing feature list separates
three regular magic effects from five shardstones.

Repeated live checks on the same Season 8 8.0.21 profile and Valheim build:

- All 22 variants had icons, accepted native socketing, and reconstructed the
  correct prefab. All five effects passed the shard-only flag assertions.
- All six pairs among the four unique shards rejected a second unique socket.
- Config reload restored five definitions and seven loot entries. A second
  reload left the loot count at seven. All 17 upgrade recipes were registered.
- Equipped a bow socketed with Siedrweaver. Activation consumed 30 eitr,
  started cooldown, applied the 12-second status, and raised health from 10
  to 18.33333 after two seconds.
- Equipped a bow socketed with Arrow Rain. A real arrow hit triggered the
  ability; sampling observed ten simultaneous projectiles. The ability changed
  from available to on cooldown.
- No warnings, errors, or exceptions occurred in the gameplay test window.

The initial test script tried to equip the already equipped bow and stopped on
the CLI's false result. The completed test switched to a club and back to the
bow after socketing, exercising normal equipment activation.

Release build passed with zero errors and 85 existing warnings. Tested DLL SHA-256:
`46c9e6f4a9f37d92248f2cc522a8c1e9e42c87ff95150f78630a31dde93781bc`.
Evidence: `/Users/benjmarston/Develop/valheim-validation-evidence/pr31-unique-abilities-20260917/`.
The prior multiplayer and UI coverage limits still apply.
