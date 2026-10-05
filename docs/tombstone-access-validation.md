# Tombstone access validation

Validated on October 5, 2026 with Valdev and both Valnet clients. The test used
the deployed Praetoris Season 8 release, 8.0.35, with matching server and client
content and the candidate PraetorisClient DLL. External reporting destinations
were disabled. Production was not changed.

## Results

- The creator recovered items from generated tombstones and from a real death.
- A stranger could not open, take all items, or stack items into a tombstone.
- Requests with a forged creator ID were rejected on all three request paths.
- A forged permission snapshot and one-sided group reports did not grant access.
- A server administrator could open another character's tombstone.
- Two real Smoothbrain Groups members could open and recover items from each
  other's tombstones. Group membership remained stable through death and respawn.
- Leaving the group or removing administrator status closed an open inventory
  and blocked further access.
- Each of the three server settings was changed during the live session.
  Disabling the relevant exception removed access. Disabling the owner
  restriction allowed stranger access. Restoring it closed that inventory.
- ExtraSlots items appeared in the grave's extended rows. A group member used
  Take all to recover the real grave's items, including 15 Wood.

The scripted suite recorded 14 passing checks. Real death, item recovery, group
stability, and chat screenshots were also inspected. Release build passed with
zero errors and 83 existing warnings, mainly in Epic Loot code.

## RCON compatibility

ValheimRcon 1.5.3 gives its synthetic Server character the first real player's
account identity. Smoothbrain Groups caches that account identity against the
synthetic character when two players join. The creator then disappears from the
group. The test reproduced this before applying the separate RCON correction.

The correction keeps the complete account-data copy from
[ValheimRcon PR #8](https://github.com/jneb802/ValheimRcon/pull/8). It supplies a
synthetic character ID only when exactly one real character is present. Both
solo and multiplayer RCON messages appeared as Server. A repeated multiplayer
measurement recorded zero HistoricalPlayerList calls and 180 PlayerList calls
over 178.9554 seconds. An earlier measurement recorded zero history calls and
82 PlayerList calls over 82.48242 seconds.

The tombstone group proof therefore requires the companion
[RCON correction](https://github.com/jneb802/ValheimRcon/pull/9)
when the solo chat relay is enabled. Owner and administrator access do not
require Groups. Group access requires both characters to be connected and both
clients to report membership. Mutual client reports prevent one client from
granting itself access; they do not establish trust against two modified
clients that agree to forge membership.

## Deployment and evidence

Install PraetorisClient on the server and every client. Remove
`expand_prefabs_player_tombstone_is_not_yours.yaml` during deployment. Its old
creator-only interaction script blocks the new exceptions.

Tested PraetorisClient SHA-256:
`22e8955b190c170f249c2e8bb25f391e95fbd881810bdd09a7eaf123d1635939`.
The same binary was verified on all three devices.

Local evidence is under
`/Users/benjmarston/Develop/valheim-validation-evidence/tombstone-access-20261005`.
It includes `results.json`, `suite-complete.log`, `commands.jsonl`, final server
and client logs, and inspected screenshots `real-grave-middle.png`,
`rcon-solo.png`, and `rcon-multiplayer-confirmed.png`.

The logs contain existing shader and asset warnings, the existing Trailership
prefab exception, and test-helper/optional Groups audit notices. No tombstone
access or RCON exception appeared in the final run. NetworkPerformanceSystem
reported two brief periods with both peers quiet and then recovered. The
permission and traffic checks passed after those periods.

Original profiles, metadata, administrator entries, character files, and server
links were restored and verified. The restored profiles loaded through mmcli on
both clients and the mmcli-agent API on Valdev. Startup's normal append to the
StarLevelSystem location-reset log was the only permitted hash difference on
the running restored server. Temporary backups were removed after verification.
Valdev retained its original running state.
