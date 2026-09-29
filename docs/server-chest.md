# Server Chest Commands

Server Chests receive admin-delivered items for one registered player. Building a `Server Chest` automatically registers it to the builder. Each player can place one Server Chest in the world. The limit includes unloaded areas and existing registered chests. Remove the existing chest before building a replacement.

The Server Chest uses a clone of the Dvergr tower treasure chest. Existing Server Chests keep their saved contents and registration. The vanilla treasure chest is unchanged. Alternate interact remains available to register old, unregistered Server Chests.

See the [Valdev and Valnet validation report](server-chest-validation.md) for test results and screenshots.

Admins can run these commands from the in-game console. The same command names are also registered with ValheimRcon when `org.tristan.rcon` is installed on the server.

## `serverchest_send`

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

## `serverchest_send_bulk`

Sends multiple item types in one command. Each item uses `<itemPrefab>:<amount>[:quality]`.

```text
serverchest_send_bulk <characterName> <itemPrefab>:<amount>[:quality] ...
```

Example:

```text
serverchest_send_bulk Bjorn Coins:100 Wood:50 SwordIron:1:2
```

This is useful for delivery bundles. If any item is invalid, the delivery is rejected and no items are saved.

## `serverchest_status`

Prints the registered chest status for one exact character name.

```text
serverchest_status <characterName>
```

Example:

```text
serverchest_status Bjorn
```

The output includes owner name, platform ID, ZDO ID, world position, item count, stack count, and visible grid size.

## `serverchest_find`

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
