# Ship rudder ownership validation

Validated on 2026-09-18 with Valdev and both Valnet clients. All three used isolated
`ship-rudder-s8-8.0.21` profiles copied from the verified Season 8 8.0.21 profiles.
The maintained profiles were not changed. Both clients used development characters.

## Build

`dotnet build -c Release --nologo` passed with 0 errors and 85 existing warnings.
The new patch produced no warnings. `git diff --check` passed.

The final DLL had the same SHA-256 on the build machine, Valdev, and both clients:

```text
fa6f679f561b9df1a5f66d2160b18d8d653160f7a4c08cee008162bc2733e6df
```

## Live results

A temporary test plugin staged a Karve and called its normal `Interact` method.
It observed control responses without granting control itself. Password tests used
the existing password input and request flow. The plugin also queried Valdev's
ship ownership independently. It is not included in the feature DLL.

| Check | Result |
| --- | --- |
| Baseline, ownership patch disabled | Client 2 steered while client 1 retained ownership. Valdev confirmed client 2 was not the owner. |
| Successful rudder control | Ownership transferred to the steering client. Both clients and Valdev agreed. |
| Reverse transfer | Client 1 regained ownership after client 2 released the rudder. |
| Occupied rudder after transfer | The former owner received `granted=False`. Ownership and the active user remained unchanged. |
| Wrong ship password | No steering control or ownership transfer. |
| Correct ship password | Steering control and ownership transferred; the active user remained valid. |
| Ship chest | Opening the chest transferred ownership to the player opening it, preserving existing game behavior. |
| Rudder use after chest access | A subsequent granted rudder request transferred ownership back to the steering client. |

The first implementation exposed a replication timing problem: the successful
response could arrive before the previous owner's user value. Claiming ownership
alone left the rudder without a valid user and allowed both clients to steer.
The final patch writes the granted player's user value after claiming ownership.
The final test confirmed `ownerLocal=True`, `steeringLocal=True`, and
`validUser=True`, followed by rejection of the other player's request.

Final interaction checks ran from 21:16 to 21:20 UTC. No rudder-related exceptions
occurred. Valdev's final interaction log contained no warnings or errors.
The full client sessions retained known modpack issues: ValheimEnforcer menu
exceptions, missing FastLink server configuration, shader warnings, and retries
of old telemetry uploads. Clients also reported candidate DLL hash differences;
Valdev validated their mod lists and both joined successfully. These unrelated
issues were not changed by this feature.

Coverage was a staged Karve over Steam. This validates ownership and control
requests, not sailing latency or every modded ship. Ownership is transferred when
control is granted; it is not kept fixed to the steering player afterward.

## Evidence and cleanup

Build output, DLL, test helper source, timestamped commands, and client/server logs
are retained at:

```text
/Users/benjmarston/Develop/valheim-validation-evidence/ship-rudder-20260918/
```

The test ships were removed and both characters logged out with saves. The
original profiles, Valdev profile links, administrator list, and stopped server
state were restored. The test profiles were retained for review.
