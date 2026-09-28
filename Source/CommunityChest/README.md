# Community Chest

`CommunityChest` clones `TreasureChest_dvergrtower`. It has no build recipe or
piece component. It starts empty and has no damage component. The original
Dvergr chest is unchanged.

Install the same PraetorisClient build on the dedicated server and clients.
An admin can use `communitychest_place` to place a persistent chest two metres
north of their character. Players open it with the normal use key.

Each verified account has one balance per world. All Community Chests in that
world show that balance. Characters on the same account share it. Different
players can open the same physical chest at the same time. The chest accepts
only coins, up to 20,000. Players must withdraw coins before paying for a port.

The normal inventory window displays the coins. While this chest is open:

- Drag coins between the inventories, split stacks, or use the normal
  quick-transfer control.
- Take All withdraws all coins if the player has room.
- Stack All deposits coins, up to the chest limit.
- Other items cannot be deposited. Withdraw coins before dropping them into
  the world. Displayed coins are arranged from the first slot after a transfer.
- `communitychest_transfer <amount>` provides exact amounts. Positive amounts
  deposit coins; negative amounts withdraw them. The chest must be open nearby.
- `communitychest_open` opens the nearest chest within five metres.
- `communitychest_status` reports the open chest balance and transfer state.

## Storage and recovery

The server writes one JSON record per account under
`<Valheim save directory>/PraetorisCommunityChests/<world UID>/<account hash>.json`.
Include this directory in server backups. Contents do not depend on a chest's
position or existence. Deleting or replacing a chest does not delete balances.

Transfers have three steps:

1. The server saves a pending transfer, after checking the account, character,
   chest, distance, balance, capacity, and record revision.
2. The client changes its coins and saves a transfer receipt with the character.
   It reads the character save back to verify the saved bytes.
3. The server applies the balance change once and saves the completed transfer.

If the connection fails, reconnect with the same character and open a Community
Chest. Its saved receipt determines whether to apply the character change or
only finish the server change. A pending transfer blocks other characters on
the account until the original character recovers it. A failed character save
does not confirm the transfer. A corrupt server record blocks access instead
of resetting the balance or restoring an older balance automatically.

The `.bak` record is for operator recovery only. Restore character and chest
data together when rolling back saves. Independently restoring an old character
or server backup can restore previously spent coins or lose later transfers.
This follows Valheim's trust in the client-owned character inventory; it does
not make modified clients or manually edited character saves trustworthy.

## Validation

Run storage tests with:

```sh
dotnet run --project Tests/CommunityChest/CommunityChest.Tests.csproj
dotnet build -c Release
```

`Tests/LiveProbe` is a separate test-only plugin. It drives actual inventory
callbacks, reports inventory counts, inspects prefab isolation, drops a commit
after a character save, and replays a commit. It is excluded from the release
assembly. Never deploy it to production.
