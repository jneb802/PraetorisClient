# Builder boat access validation

Validated on 1 October 2026 with Valheim 1.0.16, Valdev, and both Valnet clients.
The deployed production release was Praetoris Season 8 **8.0.31**.
The candidate was commit `1b5b8f9` on `feature/builder-only-password-boats`.

## Test environment

- Used the maintained `praetoris-season-8` profiles and a copied season world.
- Verified 472 server plugin/patcher files and 425 intended client files against the production release before applying candidates. Excluded the changing profiler log from the file comparison.
- Retained the full mod set, including ValheimEnforcer, Epic Loot, StarLevelSystem, and the production network mods. Preserved server-only and client-only differences.
- Installed the matching PraetorisClient candidate on the server and both clients. The ServerSideTweaks PR #47 candidate was also installed for its separate global-key tests.
- Added temporary command probes and valheimCLI. Allowed their hashes and the candidate hash in the test server's mod policy. Disabled external metric upload and removed external service credentials from copied test configs.
- Used two different development characters. Created ships through the game's prefab and `Piece.SetCreator` methods. Used actual helm and container interactions, password input, and multiplayer requests. Probes froze the test ships for stable interaction checks.
- Client 01 initially launched its previously selected 8.0.15 profile. That join failed version checks. Switched to the prepared 8.0.31 mirror before the feature proof. Early probes before character arrival are setup failures, not feature results.

## Results

| Check | Result |
| --- | --- |
| Builder uses a new Karve helm and storage without a password | Passed |
| Guest uses helm or storage before a password exists | Rejected |
| Guest sends native open, stack, take-all, or helm requests | Rejected; cargo unchanged |
| Guest attempts to set the password | Rejected |
| Builder sets the password through the normal input | Passed |
| Guest enters the wrong password | Rejected |
| Guest enters the correct password through storage input | Saved access granted; storage opened |
| Authorized guest uses helm and storage again | Passed without another password prompt |
| Second driver requests an occupied helm | Rejected |
| Builder changes and then clears the password | Existing guest access retained |
| Boat ownership moves to the guest | Existing access retained |
| Owner receives requests with a false sender identity | Rejected |
| Server saves and restarts; both clients reconnect | Saved guest helm and storage access retained |
| Previously authorized guest approaches a fresh longship | Helm and storage rejected |
| Builder steers the fresh longship without a password | Passed |

No feature change was required by this repeat test. The Release build passed with no errors and 83 existing warnings. Runtime logs contained no exception from the boat feature. Existing startup errors included PieceManager preview snapshots reaching `ZDOMan.AddIfPortal`, shader errors, and a MoreVanillaBuildPrefabs Trailership patch error. Existing asset, audio, character-join, and test-helper audit warnings also remained.

Local commands, sanitized logs, baseline hashes, and restoration evidence are stored at:

`/Users/benjmarston/Develop/valheim-validation-evidence/pr-followup-20261001/`

The test probes and game assemblies are not included in this pull request. Production was not joined or changed.
