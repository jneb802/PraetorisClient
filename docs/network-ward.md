# Network Ward

Build **Network Ward** from the hammer's Misc category (10 core wood and 2 greydwarf eyes).
Interact with it to inspect loaded networked objects within 20, 40, 80 or 160 metres of the ward.
The piece has a cyan light and a Greydwarf Shaman trophy above it, at the same position
as the Creature Owner Ward's Surtling trophy. Each custom ward's build-menu icon combines
the ward icon with its own trophy sprite. The vanilla ward icon is unchanged.
The Network Ward does not protect structures or reassign object ownership.

The window uses Valheim's wood panels, fonts, buttons and scrollbar. It has no subtitle.
It ranks object types by combined traffic. Expand a type to inspect instances, or switch
to the individual-object view. Select an instance and choose **Show in world** for an eight-second
marker. Close with the button, Escape or controller B. Closing stops sampling and releases input.

## Server approval

Network Ward access is denied by default, including for admins. On the dedicated server,
edit `BepInEx/config/warpalicious.PraetorisClient.cfg`:

```ini
[NetworkWard]
AllowedSteamIds = 76561198000000001, 76561198000000002
```

Replace these example values with approved players' SteamID64 values. `Steam_` prefixes
are also accepted. Authorization checks reload a changed server config without a restart,
including when the config directory is linked by a profile manager.
An empty list denies everyone. Client config values cannot grant access.

Interaction requests approval over the direct server connection. The server checks the
authenticated Steam socket identity, not an ID in the request. Unlisted players see an
access-denied message and no object list. The console interaction command uses the same
check. While open, the window renews approval every three seconds. A denial, disconnect,
or approval timeout closes access and stops sampling. World markers also require approval.

This approval path supports dedicated-server Steam connections. PlayFab/crossplay, TCP,
and local-host sessions are denied because they do not provide the authenticated Steam
connection used by this check. The whitelist is kept on the server and is not synchronized
to clients. It controls this mod's UI; it cannot prevent a modified client from inspecting
object data already sent by the game.

## Measurement contract

- Sent and received each combine serialized ZDO state records and object-targeted routed RPCs.
- Scope is **this client's connection traffic**, not the server's total traffic to all players.
- Each ZDO record includes its ID, revisions, owner, position, payload length and serialized payload.
- Each targeted routed RPC includes its routing envelope and serialized parameters.
- Shared message headers, sector invalidations, untargeted RPCs, transport headers, retransmissions
  and compression overhead are excluded. The total is application bytes, not wire throughput.
- Local-only RPC execution is excluded. Each actual outgoing socket submission or incoming RPC
  frame is observed once. A received record counts even when its revision is too old to apply.
- Rates use up to ten completed one-second buckets. The sample fills after opening/resetting.
- Object membership is refreshed twice per second from the loaded scene and selected radius.
  Objects that leave the area or unload are removed, including their sample history. Newly loaded
  objects enter at the next refresh. Their initial creation packet can precede that refresh.
- Quiet loaded objects remain visible with zero traffic. Share is a fraction of the listed objects'
  combined sent and received traffic, before rounding.
- The parser restores the packet cursor on success and failure. It skips payloads without copying
  them and does not create game ZDO IDs. Parse failures are counted in the window and logged once
  per measurement session; they never prevent the game from handling the packet.

## Compatibility and overhead

Outgoing hooks observe `ISocket.Send(ZPackage)` implementations for Steam, PlayFab and TCP.
Incoming hooks observe `ZRpc.HandlePackage`. This includes Network Performance System's direct
socket sends, which can bypass `ZRpc.Invoke`. No reference to the NPS assembly is required.
Other mods that replace the application framing at these boundaries need separate validation.

Outside an open window, hooks return without parsing. During sampling, storage is bounded by
currently loaded objects in the area. The UI draws only visible table rows. The ward adds no
telemetry RPCs. Opening and maintaining access uses small server authorization RPCs;
these are excluded from the per-object counters.

## Validation commands

- `networkward_open`: calls the nearest ward's normal interaction within five metres.
- `networkward_status`: prints the current sample, including separate state/RPC byte counts
  for verification. These are bytes in the sample, not rates. No raw owner account IDs are printed.
- `dotnet run --project tests/NetworkWard/NetworkWard.Tests.csproj`: checks binary attribution,
  byte sizes, untargeted-message exclusion, malformed records and cursor restoration.

Install the candidate on server and clients so all participants register the new piece.
