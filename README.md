# Praetoris Client

A BepInEx client companion mod for Praetoris gameplay and account-linking features that should not live in DiscordTools.

## Features

- In-game Discord link command:
  ```text
  !link CODE
  ```
- Creative inventory bridge RPC for server-side mods that need a trusted client inventory count.
- Creative biome, terrain, vegetation, drop suppression, and skill suppression bridges for server-side creative zones.
- Siege portal client bridge RPC for server-side siege portal handling.
- ServerChest player registration, admin delivery commands, and ValheimRcon command registration for offline item delivery workflows.
- Client-side RPC trace capture with deferred HTTP upload to the ValheimTracer relay using durable server-issued tokens.
- Client-side ZDO data trace capture for all ZDOData send, receive, apply, and skip events by default.
- Client socket metric samples and active RPC probe samples for lower-volume ValheimTracer latency and socket pressure dashboards.
- Local-only low-value environment damage text suppression while preserving combat damage numbers.

The moved RPC names intentionally keep their existing `DiscordTools_*` wire names so current server-side integrations can keep using the same requests.

## Build

```bash
dotnet build PraetorisClient.csproj
```

The built DLL is written to `bin/Debug/PraetorisClient.dll`.

## Configuration

The plugin GUID is `warpalicious.PraetorisClient`. Configure the generated file:

```text
BepInEx/config/warpalicious.PraetorisClient.cfg
```

Clients do not need the bot URL or API key for local-only features. The Discord link server endpoint and key belong on the dedicated server if PraetorisClient handles link requests there.

Preferred dedicated-server setup:

```bash
export PRAETORISCLIENT_LINK_API_URL="https://your-bot-host.example.com/api/valheim-link"
export PRAETORISCLIENT_BOT_API_KEY="shared-secret"
```

Keep real endpoint and API key values in a local `.env` file or server environment. `.env` files are ignored by git; `.env.example` documents the required variable names without secrets.

## Server Chest Commands

Server Chests receive admin-delivered items for one registered player. A player must first build a `Server Chest`, then use alternate interact on it to register it to their character. Each player can only have one registered Server Chest.

Admins can run these commands from the in-game console. The same command names are also registered with ValheimRcon when `org.tristan.rcon` is installed on the server.

### `serverchest_send`

Sends one item type to a player's registered Server Chest.

```text
serverchest_send <characterName> <itemPrefab> <amount> [quality]
```

Example:

```text
serverchest_send Bjorn Coins 100
serverchest_send Bjorn SwordIron 1 2
```

`amount` must be greater than zero. `quality` is optional and defaults to `1`. The item prefab must exist, and the requested quality must be valid for that item.

### `serverchest_send_bulk`

Sends multiple item types in one command. Each item uses `<itemPrefab>:<amount>[:quality]`.

```text
serverchest_send_bulk <characterName> <itemPrefab>:<amount>[:quality] ...
```

Example:

```text
serverchest_send_bulk Bjorn Coins:100 Wood:50 SwordIron:1:2
```

This is useful for delivery bundles. If any item is invalid, the delivery is rejected and no items are saved.

### `serverchest_status`

Prints the registered chest status for one exact character name.

```text
serverchest_status <characterName>
```

Example:

```text
serverchest_status Bjorn
```

The output includes owner name, platform ID, ZDO ID, world position, item count, stack count, and visible grid size.

### `serverchest_find`

Finds registered Server Chests by character name. The query is optional and matches partial names.

```text
serverchest_find [characterName]
```

Example:

```text
serverchest_find
serverchest_find bjo
```

Use this before sending if you are not sure of the exact character name. `serverchest_send` and `serverchest_status` require one exact registered character name. Exact name matching ignores letter case.

Deliveries only save when the Server Chest has enough remaining capacity. Players can remove items from a Server Chest, but they cannot put items into it.

## Creative Inventory RPC

PraetorisClient registers a client-side request RPC:

```text
DiscordTools_CreativeInventoryRequest
```

Request package:

```text
int    protocolVersion = 1
string requestId
ZDOID  characterId
bool   includeItems
```

The client answers the sender with:

```text
DiscordTools_CreativeInventoryResponse
```

Response package:

```text
int    protocolVersion = 1
string requestId
ZDOID  characterId
bool   available
string error
long   playerId
string playerName
int    playerInventoryCount
bool   extraSlotsLoaded
bool   extraSlotsAvailable
int    extraSlotsCount
int    totalUniqueCount
int    itemEntryCount
```

Each item entry then writes:

```text
string source
string prefabName
string sharedName
int    stack
int    quality
bool   equipped
int    gridX
int    gridY
```

`totalUniqueCount` is the value to enforce for empty-inventory checks. Item entries can include both `player` and `extraSlots` views of the same item for debugging. Shudnal ExtraSlots is read through its public `ExtraSlots.API.GetAllExtraSlotsItems()` method when the mod is loaded.

## Creative Biome Override RPC

PraetorisClient registers a client-side biome override RPC:

```text
DiscordTools_CreativeBiomeOverride
```

Request package:

```text
int     protocolVersion = 1
int     zoneCount
string  zoneId
bool    enabled
Vector3 center
float   radius
int     biome
bool    suppressSpawns
```

The client applies enabled zones by overriding `WorldGenerator.GetBiome`, `Heightmap.GetBiome`, and biome color lookups inside the radius. Disabled zones are removed, and affected heightmaps plus clutter are refreshed. Client-side natural and event spawn points are blocked inside enabled creative biome override zones.

## Link API

When a player enters `!link CODE`, the dedicated server posts JSON to `LinkApiUrl`:

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

The endpoint should return `2xx` when the code is accepted. A plain-text response body is shown to the player in chat.

## RPC Trace Upload

PraetorisClient can capture routed RPC and ZDO trace rows locally and upload compressed JSONL batches to the ValheimTracer HTTP relay. The server issues durable upload tokens over small Valheim RPC messages. Bulk trace data is not sent through Valheim routed RPC. HTTP upload is deferred while the client is actively in-world, then runs from menu/background when possible. If HTTP upload fails, the client logs the failure and keeps the local trace file for a later retry.

Pending trace files are stored locally as `.jsonl.gz` files to reduce disk usage and backlog upload pressure. Existing legacy `.jsonl` pending files are still read and uploaded.

Trace rows and upload token requests include Steam ID, platform user ID, trace player ID, and player name when available so server-side storage can group traces by player.

Server-synced config controls whether tracing is enabled and whether HTTP upload is preferred.

## Network Tweaks

`Network.SuppressEnvironmentDamageText` is enabled by default. When enabled, the client suppresses low-value environment damage text such as AoE hits against build pieces and non-player vegetation damage while preserving character combat damage numbers.
