# Server Chest live validation

This is a separate test plugin. It is excluded from the PraetorisClient build and release package. Use it only on a leased test client with a development character and the matching candidate on the test server.

Build with `dotnet build tests/ServerChestProof/Probe.csproj -c Release`. Install `ServerChestProof.dll` on the test client and allow `praetoris.validation.serverchest` version `1.0.0` in the test server's admin-only mod list. Back up and restore the affected profile files after testing.

Run these console commands after connecting:

```text
chest_proof roundtrip 0
chest_proof roundtrip 1
chest_proof roundtrip 64
chest_proof roundtrip 65
chest_proof roundtrip 2048
chest_proof roundtrip 2049
chest_proof saveonly 65537
chest_proof spawn
chest_proof register
```

Each round-trip check verifies stack count, row count, positions, quality, durability, crafter name, and custom item data. The 2,049-stack check also verifies that a truncated save is rejected without replacing existing contents. The save-only check verifies that more than 65,535 stacks can be encoded without count truncation.

Send items through `serverchest_send <registered-character-name> SwordIron 65`. Wait for delivery, then run `chest_proof open`. Use the mouse wheel and scrollbar to reach the last row. Capture and inspect screenshots. `chest_proof state` reports item count, unique positions, grid elements, and scroll position. `chest_proof take` withdraws the last stack through the inventory UI callback. `chest_proof deposit` checks that a deposit remains blocked.

Repeat with 2,049 stacks. Also send a small delivery while the chest is open and check that the count updates. Run `chest_proof cache` to check that an unrelated network-object change keeps the same loaded item instances. Reconnect and restart the test server to check persistence. Use `chest_proof normal` to check the ordinary wooden chest grid after the server chest. Finish with `chest_proof cleanup`. This deletes the marked proof chest and its test items. Restore the development character and profile backups.
