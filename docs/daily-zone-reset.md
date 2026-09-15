# Daily zone reset process

Status: maintenance-mode and Valdev validation complete. Production automation is not implemented or enabled.

## Goal

Reset one world quadrant each day with Upgrade World. Protect player bases with `safeZones=2`. Do not run a reset while a player is connected or can complete a new connection.

The rotation is clockwise:

1. Northeast (`ne`)
2. Southeast (`se`)
3. Southwest (`sw`)
4. Northwest (`nw`)

Advance the rotation only after a verified successful reset. If a run fails, keep the same quadrant for the next attempt.

## Maintenance mode

PraetorisClient will provide the maintenance control on both the server and client.

The server stores an absolute maintenance end time in Coordinated Universal Time (UTC). An active end time survives a server restart. The state expires automatically at that time so that a failed automation process cannot keep the server closed without a limit.

Server controls:

- `maintenance_start <minutes>` starts maintenance for the specified duration.
- `maintenance_status` reports whether maintenance is active, its end time, remaining time, and connected player count.
- `maintenance_end` ends maintenance early.
- The same controls are available through Remote Console (RCON). This lets the daily process run without an administrator player connected.

When maintenance starts, the server must:

1. Reject all new player handshakes before they enter the world.
2. Ask each connected client to save its player profile.
3. Send each connected client the maintenance end time.
4. Disconnect each connected player.
5. Report how many players it disconnected.

When a player tries to connect during maintenance, PraetorisClient on the client must show:

```text
Server maintenance is in progress.
Maintenance ends at <date and time in the player's local time>.
```

A client without the matching PraetorisClient version can still be disconnected, but it will see Valheim's generic kicked message. The Season 8 modpack must include the matching client version before production maintenance is enabled.

## Daily operation

The production automation must use a durable state file. The file records the last successful quadrant, start and end times, backup path, command result, and validation result. Do not derive the next quadrant only from the weekday because a failed or missed run would skip work.

For each run:

1. Select the next quadrant from the last successful state.
2. Start maintenance with a duration based on the Valdev benchmark.
3. Wait until `maintenance_status` reports zero connected players.
4. Request a world save and wait for the save-complete log entry.
5. Create a dated backup of the complete chunked world directory and its metadata.
6. Verify that the backup exists, is readable, and has the expected file count and byte count.
7. Check again that maintenance is active and the player count is zero.
8. Run `zones_reset quadrant=<quadrant> safeZones=2 start` through RCON `consoleCommand`.
9. Capture the Upgrade World result and server resource measurements.
10. Request another world save and wait for completion.
11. Restart the server through `valheim-server.service`.
12. Wait for the world-ready log entry and the game port.
13. Confirm that maintenance still rejects a Valnet client.
14. End maintenance.
15. Confirm that the Valnet client can connect and enter the world.
16. Record success and advance the quadrant rotation.

The process must stop before the reset if any of these checks fail:

- Maintenance is not active.
- A player is still connected.
- The pre-reset save did not complete.
- The backup is missing or incomplete.
- The selected quadrant is not the expected next quadrant.

Do not queue a preview on the production process before the reset. Upgrade World keeps previews in its operation queue. If an operator performs a preview, use the Upgrade World `stop` command to clear the queue and verify that it is empty before any `start`. The daily automation must use the single command with its `start` parameter and must not depend on a queued operation.

If the reset or post-reset validation fails, keep maintenance active and do not advance the rotation. Restore the pre-reset backup while the server is stopped if the world cannot load or the reset result is invalid.

## Valdev duration benchmark

Use a read-only copy of the current production Season 8 world. Keep the source production world unchanged. Use the Season 8 profile on Valdev and a separate benchmark world name.

Test each quadrant from the same baseline backup. Restore the baseline before each run. Consecutive runs on one modified copy would measure less work and give an invalid comparison.

For each quadrant, record:

- Previewed zone count.
- Protected zone count.
- Reset zone count.
- Reset wall-clock duration.
- Peak server resident memory.
- Garbage collection or out-of-memory errors.
- Pre-reset and post-reset save duration.
- World file count and byte count before and after.
- Server restart and world-load duration.
- Relevant Upgrade World warnings or exceptions.

The initial benchmark used the September 15, 2026 production backup. The baseline contained 202 files, 140,958,517 bytes, and 3,290,089 loaded world objects. Each row used a fresh restore of the same baseline.

| Quadrant | Reset zones | Protected zones | Reset | Post-reset save | Peak resident memory | Restart to RCON ready |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Northeast | 10,486 | 867 | 1.182 s | 11.073 s | 4,515,252 KiB | Not sampled |
| Southeast | 9,189 | 1,037 | 0.957 s | 10.746 s | 4,510,824 KiB | Not sampled |
| Southwest | 7,121 | 832 | 0.999 s | 3.648 s | 4,541,440 KiB | 96.582 s |
| Northwest | 11,885 | 683 | 1.289 s | 8.525 s | 4,554,588 KiB | 96.347 s |

The longest measured reset plus save was 12.255 seconds. Valdev needed at most 102.100 seconds to start and make RCON ready. The current production server needed approximately 67 seconds in a read-only journal sample. No reset crashed the server. The northwest result also saved, restarted, loaded, and accepted a Valnet client.

Use a 15-minute maintenance duration for the first production dry run. This includes time for player disconnect, the pre-reset save, backup and verification, reset, post-reset save, restart, and connection validation. The automation must extend maintenance before the published end time if a run is still active.

Upgrade World reported negative `kept` values in some asynchronous save summaries even though it wrote the world chunks and the tested world restarted. Treat this as an unresolved counter or log issue. Keep the post-save restart and connection checks as required safeguards.

## Valdev and Valnet proof

Use Valdev with the Season 8 server profile and Valnet with the matching Season 8 client profile. Use a development character.

Required proof:

1. A connected client receives the maintenance message and disconnects when maintenance starts.
2. A new connection during maintenance does not enter the world and shows the correct local end time.
3. Maintenance remains active across a server restart.
4. A connection succeeds after `maintenance_end` or automatic expiry.
5. The server reports zero players immediately before `zones_reset` starts.
6. Each quadrant reset completes from the same production-world baseline without a crash.
7. The post-reset world saves, restarts, and accepts a client.
8. Logs contain no new unexplained PraetorisClient or Upgrade World warning, error, or exception.

Save the command output, relevant server and client log windows, benchmark table, and client screenshot with the validation artifacts.

## Production activation

Keep scheduling disabled until:

- The maintenance feature is merged and included in the Season 8 server and client packages.
- Valdev and Valnet validation passes.
- The benchmark defines the maintenance duration.
- Backup restore has been tested on Valdev.
- The daily script has a dry-run mode and passes a production-path dry run.
- The operator approves the maintenance time and enables the schedule.
