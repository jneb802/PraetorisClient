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
- Client-side RPC trace capture with deferred HTTP upload to the ValheimTracer relay using short-lived server-issued tokens.
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

PraetorisClient can capture routed RPC and ZDO trace rows locally and upload compressed JSONL batches to the ValheimTracer HTTP relay. The server issues short-lived upload tokens over small Valheim RPC messages. Bulk trace data is not sent through Valheim routed RPC. HTTP upload is deferred while the client is actively in-world, then runs from menu/background when possible. If HTTP upload fails, the client logs the failure and keeps the local trace file for a later retry.

Pending trace files are stored locally as `.jsonl.gz` files to reduce disk usage and backlog upload pressure. Existing legacy `.jsonl` pending files are still read and uploaded.

Trace rows and upload token requests include Steam ID, platform user ID, trace player ID, and player name when available so server-side storage can group traces by player.

Server-synced config controls whether tracing is enabled and whether HTTP upload is preferred.

## Network Tweaks

`Network.SuppressEnvironmentDamageText` is enabled by default. When enabled, the client suppresses low-value environment damage text such as AoE hits against build pieces and non-player vegetation damage while preserving character combat damage numbers.
