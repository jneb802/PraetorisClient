# Shipping port validation — 2026-10-04

Both changes passed live tests on Valdev with two Valnet clients. Production was read only. All world changes used a copy of the production backup `praetorisSeason8v2_backup_auto-20261004-201539`.

The test used the installed Season 8 production libraries and modpack 8.0.34, including MWL 5.1.8. The candidate PraetorisClient replaced 0.1.89 on all three devices. Its SHA-256 was `1c1eb0b8799fde9c1db16f3ddaf3d208c2a92af24ceb07cdfa1520bedf43424d`. The server accepted both client mod lists. Temporary helpers controlled development characters and clicked the normal port buttons. Teleport price and payment tests used the real MWL interface with the no-cost cheat disabled.

## Existing port icon

The manually placed `MWL_Port1` west of the start temple has these records:

| Record | Position X, Y, Z | Zone |
|---|---|---|
| Port location proxy | 197.784, 29.076, -17.274 | 3, 0 |
| Port trader | 197.912, 34.704, -24.641 | 3, 0 |
| Start temple registration | 257.946, 34.592, -61.363 | 4, -1 |

The port and temple occupy different zones. Before registration, the port zone had no registered location. Both clients had the temple pin and lacked the port pin while at the port.

The server ran:

```text
location_register MWL_Port1 pos=197.783569,-17.274458,29.075895
```

The saved location count changed from 14,342 to 14,343. The only added nearby registration was the port. All existing registrations within 200 metres of the temple, all nearby location proxies, and the port trader GUID and name stayed the same. A server restart preserved the added registration. Running the registration command again produced the same report.

Both clients displayed the port pin and temple pin after reconnecting and approaching the port. A client still at the port immediately after registration retained its old icon list until reconnecting. The test therefore requires reconnecting and retains the normal server rules for icon discovery.

This result applies to this backup and port. It does not prove that replacing another location registration in a shared zone is safe.

## Free departures

Port A was the manually placed port, GUID `ba2c0eba-e552-428c-a6a4-577e3b91bff5`. Port B was Mistenheim, GUID `81f7d360-d037-4469-aaf9-8a360e2cc5dc`. Port C was Mistskarheim, GUID `23779275-a41a-46df-accb-db46629cc71f`.

| Test | Result |
|---|---|
| Read A's GUID beside its trader | Command returned the saved GUID above |
| Select A and travel A to B | Both clients arrived at B with zero coins |
| Travel B to A with zero coins | Normal price was 12; neither client travelled |
| Travel B to A with 20 coins | Both arrived at A with 8 coins |
| Clear and select A while both clients were connected | Both prices changed from 0 to 12, then back to 0 |
| Carry copper ore at A | Teleport remained blocked despite the zero price |
| Rename A | GUID and zero price remained; original name was restored |
| Restart server with A selected | Server retained A's GUID; client received the zero price on reconnect |
| Change selection to B | B became free; A resumed charging 12 |
| Travel B to C, then quote C to B | B's price changed from 19 to 0; C's price stayed 19 |
| Free travel while carrying 8 coins | All 8 coins remained after arrival |
| Non-admin clear command | Client rejected the command |
| Non-admin direct clear network request | Request was sent; server retained B's GUID and free price |

Live tests found and fixed two issues: the native client admin check rejected a server-recognized administrator, and saving alone did not send configuration changes to connected clients. The command now uses Jötunn's synchronized admin status. Accepted server changes save and reload configuration to trigger synchronization. The server still independently verifies administrator access and reads the port GUID from its own network record.

The Release build passed. All 27 local checks passed. Local checks simulate game records and patch application; the multiplayer results above prove application in the game.

Evidence is retained locally in `/Users/benjmarston/Develop/validation-evidence/shipping-ports-20261004`, including command logs, screenshots, world record comparisons, build output, and restoration reports. No production deployment was performed.

## Restoration

Valdev's 928 profile files matched the original backup archive. Its original launch script, agent configuration, and profile links were restored. The server reopened `mwlPortIconTest` from the original save directory with MWL 5.1.7 and PraetorisClient 0.1.88. All 477 plugin and patcher files still matched the original hashes after startup. The test helper was absent.

Client 01's 654 profile files and client 02's 648 profile files matched their original hash records. Both clients' original active profile configuration and package registry were restored. Development characters were removed. Both Valnet machines were verified off.
