# PraetorisClient

PraetorisClient is the shared client-and-server mod for Praetoris-specific gameplay, creative-zone, administration, and network measurement features.

## Requirements and integrations

- [BepInEx](https://github.com/BepInEx/BepInEx) is required.
- [Jotunn](https://github.com/Valheim-Modding/Jotunn) is required.
- Epic Loot is required by the current build. PraetorisClient starts its Epic Loot extensions during plugin startup.
- ValheimRcon is optional. When present on the server, it receives the Server Chest commands.
- Shudnal ExtraSlots is optional. When present on a client, its items are included in creative inventory reports.
- VBNetTweaks is optional. When present, its `ZDOQueueLimit` supplies the socket metric queue budget.
- ValheimTracer is optional. A compatible server can issue tokens and receive uploaded network metric batches.

## Features

### Player features

#### Discord account linking

- Enter `!link CODE` in game chat to link a Valheim character to Discord.
- The server sends the code and player identity to the configured Praetoris bot endpoint.
- The result appears in game chat.
- The command name is configurable.

#### Cleanse Mead

- Adds `Mead Base: Cleanse` to the Mead Ketill.
- The recipe requires 6 Pukeberries, 1 Fresh Seaweed, and 1 Fragrant Bundle.
- One mead base ferments into six Cleanse Mead.
- Drinking it removes every other active status effect once. This includes beneficial and harmful effects.
- The mead leaves its own 120-second cooldown effect.

#### Server Chest

- Adds a buildable Server Chest for administrator deliveries.
- Alternate interact registers the chest to the player's current character.
- Each player can register only one Server Chest.
- Each registered chest can belong to only one player.
- Administrators can deliver items while the player is offline.
- Players can remove delivered items, but they cannot place items into the chest.
- The chest supports up to 64 item slots and saves its contents in the world.
- See the [Server Chest command guide](docs/server-chest.md) for delivery and lookup commands.

#### Creature Owner Ward

- Adds a Hammer > Misc piece made with 10 Core Wood.
- The piece assigns network ownership of nearby hostile creatures and bosses to a selected connected player.
- It does not tame creatures and does not provide normal ward protection.
- It excludes players, tamed creatures, and creatures in player-controlled factions.
- Use toggles the ward. Alternate use opens the owner-name input.
- Any connected player can use these ward controls. They are not limited to the piece creator or an administrator.
- Its hover text shows its state, selected owner, and radius.
- The default radius is 40 metres. The default update interval is 2 seconds.

#### World and display changes

- Prevents building in selected Hildir locations, crypts, caves, and Mistlands Dvergr entrances.
- Prevents the specific attackerless water-impact damage applied to boats. Other boat damage still applies.
- Hides area damage numbers on building pieces and non-player damage numbers on trees and logs.
- Preserves player combat damage numbers. This display option does not change damage.

### Epic Loot additions

PraetorisClient adds seven Epic Loot magic effects:

- **Ancestral Slam:** High-rarity two-handed clubs summon an ancestral copy that repeats the player's attack near the target.
- **Glancing Blows:** Increases block power by 50%, but applies additional health damage after a block or parry. Armor can reduce this additional damage, but resistances cannot.
- **Retaliation:** Successful blocks add stacks. A later melee attack consumes a stack and increases the speed of that attack string. Higher rarities support more stacks.
- **Infusion:** An activated melee-weapon ability that converts recognized fire, frost, lightning, poison, or wind effects into temporary elemental damage or attack-speed bonuses. Its cooldown is 120 seconds.
- **Whirlwind:** High-rarity two-handed polearms gain a rapid secondary spin attack with reduced stamina cost and adjusted damage and stagger values.
- **Onslaught:** Club combo finishers and attacks against staggered enemies start a sequence of repeated final-combo attacks with increasing damage.
- **Perfect Strike:** Two-handed axes gain a timing window that starts the next combo attack and increases its damage when the player attacks at the correct time.

The mod also changes Epic Loot rune behavior:

- Extracted runes keep the effect value from the source item and round it to two decimal places.
- Etching a rune that is at least 10% stronger than the normal maximum can destroy both the rune and target item.
- The destruction chance ranges from 10% to 90% based on the rune's strength.
- The enchanting interface shows a red warning and requires a second confirmation.
- Epic Loot treasure-map chests ignore ward access checks. Ordinary chests are unchanged.

### Creative-zone integration

PraetorisClient supplies the client-side parts required by the Praetoris creative server systems:

- Reports normal inventory and Shudnal ExtraSlots counts to the server.
- Can include item name, stack, quality, equipped state, source inventory, and grid position.
- Can restrict selected console commands to the player's assigned creative zone.
- Prevents skill gains while the player is inside the active creative zone.

These features receive their state from server remote procedure call (RPC) messages. They do not have local creative-zone configuration entries.

### Siege portal integration

- Detects portals marked with a siege ID in their network data or with a `siege:<id>` portal tag.
- Stops normal portal travel and sends the character and siege ID to the server.
- Shows the selected siege ID to the player.
- Requires a compatible server-side siege system to perform the actual entry or teleport.
- Includes two development commands for marking and testing nearby siege portals.

### Network measurement and stability

#### Frame-time monitoring

- Writes local frame-rate and frame-time summaries every 30 seconds by default.
- Records average frames per second, average frame time, percentiles, maximum frame time, and long-frame counts.
- Can write a second CSV with each frame that exceeds the configured long-frame threshold.
- These CSV files remain local and are not included in network metric uploads.

#### Socket and ZDO transport metrics

- Samples socket state every 5 seconds by default.
- Measures ping, connection quality, send queues, queue headroom, byte rates, ZDO package rates, send timing, and socket pressure.
- Records aggregate ZDO transport values. It does not record ZDO contents, prefab data, individual ZDO fields, or receive/apply events.

#### Active RPC probes

- Sends a small synthetic probe every 2 seconds by default.
- Measures client-to-server and relayed client round-trip time.
- Records timeouts, server relay time, target peer, payload size, and send-queue state.

#### Metric storage and upload

- Writes pending socket and RPC probe metrics as compressed JSON Lines files.
- Can upload the files to a compatible ValheimTracer HTTP endpoint during an active server connection.
- Uses a durable, server-issued upload token.
- Deletes a pending file only after its final batch succeeds.
- Keeps failed files for a later retry.
- Supports legacy uncompressed `.jsonl` pending files.

#### ServerSync protection

- Blocks clients from sending peer-to-peer ServerSync configuration broadcasts.
- The server controls this setting. It is enabled by default.

## Commands

### Player chat

```text
!link CODE
```

### Administrator console

```text
ownerward_set_nearest <ownerName> [radius]
ownerward_enable_nearest [true|false] [radius]
ownerward_status_nearest [radius]

serverchest_send <characterName> <itemPrefab> <amount> [quality]
serverchest_send_bulk <characterName> <itemPrefab>:<amount>[:quality] ...
serverchest_status <characterName>
serverchest_find [characterName]
```

The four Server Chest commands are also registered with ValheimRcon when it is installed on the server.

### Siege development commands

```text
dt_mark_siege_portal <siegeId> [radius]
dt_enter_nearest_siege_portal [radius]
```

These two commands are development tools and are not currently restricted to administrators.

## Configuration

The plugin GUID is `warpalicious.PraetorisClient`. Configure the generated file:

```text
BepInEx/config/warpalicious.PraetorisClient.cfg
```

PraetorisClient watches this file and reloads changes after approximately one second. Jotunn synchronizes selected server-controlled settings to connected clients.

Important settings include:

| Setting | Default | Purpose |
| --- | ---: | --- |
| `Linking.LinkCommand` | `!link` | Sets the in-game account-link command. |
| `Network.SuppressEnvironmentDamageText` | `true` | Hides low-value environment damage numbers. |
| `Ships.DisableBoatWaterImpactDamage` | `true` | Prevents boat water-impact damage. |
| `CreatureOwnerWard.Radius` | `40` | Sets the owner ward radius in metres. |
| `CreatureOwnerWard.UpdateIntervalSeconds` | `2` | Sets the delay between ownership checks. |
| `FrameMetrics.Enabled` | `true` | Enables local frame-time CSV files. |
| `FrameMetrics.SummaryIntervalSeconds` | `30` | Sets the frame summary interval. |
| `FrameMetrics.LongFrameThresholdMs` | `150` | Sets the long-frame threshold. |
| `SocketMetrics.Enabled` | `true` | Enables socket and ZDO transport samples. |
| `SocketMetrics.SampleIntervalSeconds` | `5` | Sets the socket sample interval. |
| `RpcProbe.Enabled` | `true` | Enables active network probes. |
| `RpcProbe.IntervalSeconds` | `2` | Sets the probe interval. |
| `NetworkMetrics.HttpUploadPreferred` | `true` | Allows upload through server-issued ValheimTracer tokens. |
| `Measurement.DisableNetworkMetrics` | `false` | Local override that disables socket metrics and RPC probes. |
| `Measurement.DisableNetworkMetricHttpUpload` | `false` | Local override that keeps network metrics on disk. |
| `ServerSyncProtection.BlockPeerServerSyncConfigSync` | `true` | Blocks peer configuration broadcasts. |

### Dedicated-server account-link configuration

Clients do not need the bot URL or API key. Set these values only on a server that handles link requests:

```bash
export PRAETORISCLIENT_LINK_API_URL="https://your-bot-host.example.com/api/valheim-link"
export PRAETORISCLIENT_BOT_API_KEY="shared-secret"
```

These environment variables override `BotApi.LinkApiUrl` and `BotApi.ApiKey`. PraetorisClient does not load `.env` files itself. The service that starts the server must export the variables.

## Data written by diagnostics

Frame metrics are stored under:

```text
BepInEx/logs/PraetorisClient/FrameMetrics
```

Pending network metrics and the upload-token cache are stored under:

```text
BepInEx/logs/PraetorisClient/NetworkMetrics
```

Network metric rows can include the player name, Steam or platform identifiers, peer identifier, world name, world identifier, Valheim version, and installed plugin information. Frame metric rows can include the world name and exact player position.

## Integration protocols

Some migrated RPC messages intentionally keep their existing `DiscordTools_*` names so current server integrations can continue to use them. Newer features use `PraetorisClient_*` names.

### Creative inventory request

Request RPC:

```text
DiscordTools_CreativeInventoryRequest
```

Response RPC:

```text
DiscordTools_CreativeInventoryResponse
```

The protocol reports the player identity, normal inventory count, ExtraSlots availability and count, and a duplicate-free combined item count. The server can request detailed item entries when required.

### Discord link API

When a player enters `!link CODE`, the server posts JSON to the configured link endpoint:

```json
{
  "requestId": "6b7b8d9c0f2a4d7ca5f8c37e87b6fd13",
  "code": "PRAE-482913",
  "playerId": "76561198000000000",
  "playerName": "Player",
  "endpoint": "76561198000000000",
  "platformDisplayName": "SteamName",
  "receivedAtUtc": "2026-05-28T18:42:00.0000000Z"
}
```

The endpoint must return a successful HTTP status when it accepts the code. PraetorisClient shows the plain-text response body to the player. `playerId` is the server's stable peer identifier and is not guaranteed to be a Steam ID.

## Build

Use Valheim 1.0 game assemblies and regenerate their publicized assemblies before
building. Older assemblies can produce a DLL that compiles but fails during
console-command registration on Valheim 1.0.

```bash
dotnet build PraetorisClient.csproj
```

The built DLL is written to `bin/Debug/PraetorisClient.dll`.

To build against assemblies copied from a test client, override both paths:

```bash
dotnet build PraetorisClient.csproj -c Release \
  -p:VALHEIM_MANAGED=/path/to/Managed \
  -p:PUBLICIZED_PATH=/path/to/Managed/publicized_assemblies
```

## Additional documentation

- [Server Chest command guide](docs/server-chest.md)
