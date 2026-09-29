# Server Chest Commands

Server Chests receive admin-delivered items for one registered player. A player must first build a `Server Chest`, then use alternate interact on it to register it to their character. Each player can only have one registered Server Chest.

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
