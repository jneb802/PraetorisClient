# Community Chest validation — 2026-09-28

## Environment

- Valdev dedicated server and both Valnet clients, using separate Steam accounts.
- Isolated test profiles copied from the live Season 8 production DLLs and game
  configuration. The live baseline was 8.0.29; 8.0.30 was staged, not deployed.
  Client 01's test profile name contains `8.0.30`, but its files were replaced
  with the live production snapshot before the tests.
- Valheim client build 25527674. Both clients connected to Valdev's public game
  endpoint and used new local development characters, CommunityProofA and B.
- Test additions: candidate PraetorisClient, valheimCLI, and the separate
  CommunityChestLiveProbe assembly. Admin mod-validation exemption allowed the
  test helpers. The initial join reported zero missing mods and zero version
  mismatches. Production notification, monitoring, and scheduled-job services
  were excluded. Maintained profiles were not edited.

## Observed results

| Check | Result |
| --- | --- |
| Prefab registration | `CommunityChest` exists; zero build tables, default loot, Piece, WearNTear, or Destructible components. |
| Original chest | `TreasureChest_dvergrtower` retains its Container, Piece, and WearNTear components. |
| Two accounts | A: 100 carried / 200 stored. B: 45 carried / 75 stored, simultaneously. |
| Coin-only filter | Wood rejected with a message. Counts unchanged. |
| Normal controls | Quick transfer, Take All, Place Stacks, and dragging one coin in each direction succeeded. |
| Repeated commit | Replaying the last withdrawal did not change A's 80 carried / 220 stored. |
| Interrupted deposit | Dropped the commit after the character saved. A had 50 carried, server had 220 stored and a pending deposit of 30. |
| Full restart recovery | Restarted Valdev and both game processes. Opening the chest completed the pending deposit: A remained at 50 carried and reached 250 stored. B retained 45 carried / 75 stored. |
| Full inventory | With no coins carried and zero free slots, a withdrawal of 10 was refused; 300 stored coins remained. |
| World drop | Dropping coins directly from the private display was refused. |
| Multiple chests | A newly placed chest showed A's existing 250 stored coins. |
| Chest replacement | Destroying and replacing the physical chest retained A's 250 and B's 75 stored coins. |
| Ordinary chest | A wood chest accepted 50 coins through normal quick transfer and returned all 50 through Take All. |
| Visual inspection | Inspected the Dvergr chest model, private-access hover text, title, and coin grids in captured screenshots. |

The storage executable checks interrupted deposits and withdrawals, repeated
commits, account/world separation, character binding, stale requests, capacity
and integer limits, durable replacement, and malformed or incomplete records.
All 18 checks pass. The Release build passes with 84 warnings in existing code
and no errors.

## Log review

The server's feature-validation window contained no warnings or errors. Client
windows contained retries for old HTTP telemetry uploads and a Unity collider
warning from valheimCLI's nearby-prefab inspection (`Collider.ClosestPoint` on
the spawn stones). Neither came from Community Chest operations. The final
integration profile disables the existing HTTP telemetry upload option.

The final integrated build includes `main` through `e47bc1a` and the incomplete
record check. Its SHA-256 is
`84209f0162009d25760a4c54c3b643843157aeb0add6b69176fa9f79bf00f8b7`,
identical on Valdev and both clients. After another full restart, A retained
50 carried / 250 stored and B retained 45 carried / 75 stored. Split transfers
in both directions changed only the requesting account, then restored those
counts. The final server and both client feature windows had zero warnings
and errors.

Screenshots, command output, hashes, and filtered logs are retained locally at
`/Users/benjmarston/Develop/valheim-validation-evidence/community-chest-20260928/`.

## Repeating the live checks

Build `Tests/LiveProbe/LiveProbe.csproj` separately and install its DLL only in
the test client profiles. Both clients must use the same candidate as Valdev.

1. Place a chest with `communitychest_place` on client A.
2. Open it on both clients with the normal use key or `communitychest_open`.
3. Use `cli_give_item Coins 300` on A and `cli_give_item Coins 120` on B.
4. Deposit different amounts with `communitychest_transfer <amount>`.
5. Check `ccprobe status` and the actual inventory windows on both clients.
6. Exercise `ccprobe click player`, `ccprobe drag chest 1`, `ccprobe takeall`,
   `ccprobe stackall`, `ccprobe wood`, and `ccprobe dropworld chest`.
7. Run `ccprobe dropcommit`, then a deposit. Verify the pending server record.
8. Save/logout and restart both clients and Valdev. Reopen with the same
   character. Verify that carried coins are not deducted a second time.
9. Use `ccprobe fill` with no carried coins to check a full inventory, then
   `ccprobe unfill` to remove the test stone.

Character files and the server ledger must be backed up and restored together.
These checks cover normal clients and interrupted communication; they do not
establish protection against edited character saves or modified clients.
