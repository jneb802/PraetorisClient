# Server Chest capacity validation

Validated version 0.1.86 on 2026-09-30 UTC with Valdev and Valnet client 01.

## Baseline

Both devices used the maintained `praetoris-season-8` profile with the deployed production release, 8.0.29. The original PraetorisClient version was 0.1.82. Server plugin and patcher hashes matched production. Shared client DLLs matched production; server-only omissions and the existing client command helper were retained.

Only the candidate DLL, the separate client proof plugin, and their test configuration were added. Production was not changed.

## Results

- Release build passed with zero errors and 84 existing warnings. The separate proof plugin also built successfully.
- Real-game save/load checks passed with 0, 64, 65, 2,048, and 2,049 stacks. They checked positions and item metadata. A truncated grouped save was rejected without replacing existing contents.
- A save-only check encoded 65,537 stacks without truncating the count. This was not a live rendering test at that size.
- A delivery of 65 unstackable items appeared while the previously empty chest remained open.
- After one withdrawal and another delivery, the chest held 2,049 stacks across 257 rows, with unique positions for every stack.
- Mouse-wheel scrolling reached the last row. The final screenshot showed the last item and the scrollbar at the bottom.
- A server restart and client reconnect preserved all 2,049 stacks. Both server commands and the client reported the same contents.
- Withdrawing to 2,048 stacks saved and reloaded correctly in the native format. Player deposits remained blocked.
- An unrelated network-object revision preserved loaded item instances instead of decoding the inventory again.
- Opening a normal wooden chest afterwards restored its normal 10-slot grid.

The final test flow produced no ServerChest exceptions. Startup graphics and asset warnings remained. Server graphics errors also appeared in the original baseline log.

## Limits and compatibility

There is no configured slot cap. Normal stack limits still apply. The native inventory grid creates elements for all rows, so large inventories cost memory, rendering time, save size, and network traffic. The 1,985-item delivery took about 34 seconds in this test environment.

More than 2,048 stacks use the new grouped save format. Older mod versions cannot read that format. Deploy matching versions to the server and clients.

## Reproduction and evidence

See [the proof plugin instructions](../tests/ServerChestProof/README.md). The helper is excluded from the main build and release package.

Local command captures and inspected screenshots are in `/Users/benjmarston/Develop/valheim-validation-evidence/server-chest-capacity-20260930`. The final evidence files use the `final-` prefix.

Test containers and items were removed. Original profile files and the development character were restored. All 446 server and 312 client plugin/patcher files matched their original hashes after restoration. The restored server loaded PraetorisClient 0.1.82 and accepted a player-count query. Valdev was returned to its original stopped state, and the Valnet client was shut down. Both device leases were released.
