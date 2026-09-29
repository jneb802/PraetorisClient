# Builder camera live validation

Build the main project first, then this project. Install `BuilderCameraProof.dll` only on the leased test client, alongside the candidate `PraetorisClient.dll`. Remove the helper after validation. It is excluded from the main assembly.

Use the current production mirror profile and back up the candidate DLL, configs, and proof world before testing. The helper requires developer commands. It supplies observation and setup commands; it does not grant camera access or bypass the camera's movement, collision, fuel, or equipment checks.

`builderproof status` prints camera state, player health, inventory, and the nearest ward's fuel. Other actions:

- `refuel`: call the ward's normal interaction.
- `unequip <prefab>`: remove equipment through the normal unequip method.
- `aim <x> <y> <z>`: aim the camera at a point without changing its position.
- `damage`: apply one point of damage through the game's damage method.
- `purchase`: buy the belt from the nearest trader through `StoreGui.OnBuyItem`.
- `emptywood`: remove wood to test the missing-resource check.
- `reload`: reload the candidate configuration after changing test limits.
- `protect <x> <y> <z>`: create a foreign ward for a permission test. Other mods can change its radius.

Use keyboard and mouse input for movement and building. `cli_build_try_place` calls placement directly and does not exercise the normal resource-payment path, so it is suitable only for staging.

Required checks:

1. Hildir lists the belt at the configured price. Buying it removes that many coins.
2. Camera activation fails with a missing or unequipped belt, missing build tool, missing ward, or empty ward.
3. Adding an eye consumes one inventory item and adds one eye of fuel. The maximum prevents further payment.
4. Active camera movement leaves the player's body in place. Fuel decreases only while active, in one-second portions per player.
5. Camera movement stops at terrain, walls, and each configured distance limit. Build targets must also satisfy both limits.
6. Mouse building consumes normal resources. Missing resources and protected areas prevent building.
7. Damage, belt removal, tool removal, ward removal, teleporting, and logout end camera mode. Normal movement and camera behavior return.
8. Save/reload preserves ward fuel. Check the client log for new exceptions.
9. Capture and inspect a screenshot and recording. Restore the original profile and world content, verify hashes, and remove the helper.

See [RESULTS.md](RESULTS.md) for the recorded client validation and the remaining multiplayer checks.
