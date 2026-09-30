# Boat password validation

Run the isolated data checks with:

```sh
dotnet run --project Tests/ShipPassword/ShipPassword.Tests.csproj
dotnet build -c Release
```

The data checks use an in-memory substitute for ZDO, Valheim's saved network object. They check password verification, character separation, repeated grants, password changes, and independent permissions for a replacement boat. They do not prove world-save persistence or network behavior.

## Live validation pending

Valdev and Valnet client 01 were reserved for server chest tests when this feature was implemented. No candidate files were deployed. Install the candidate on both the server and participating clients; the owner grant messages now use version 2 payloads.

Use the current production Season 8 mirror after acquiring the device leases. Back up candidate destinations and restore them after testing.

1. Record the active profiles, production release, installed mod versions, and initial log positions.
2. Build a boat with storage. Set a password through alternate use at the helm. Confirm the creator can immediately use the helm and storage.
3. With a second character, enter a wrong password at both the helm and chest. Confirm access is denied. Cancel a prompt and confirm access remains denied.
4. Enter the correct password at the chest. Confirm it opens after authorization, and the helm then works without another prompt.
5. Repeat on a second boat, starting at the helm. Confirm its storage works without another prompt.
6. With an unauthorized character, try opening, stacking, and taking all items. Confirm each operation is denied, including direct container requests and requests with another character's ID.
7. Alternate boat ownership between authorized characters. Confirm both retain access and occupied helm behavior is unchanged.
8. Reconnect the clients. Save and restart the server through the documented lifecycle API. Confirm both characters still have access without a password prompt.
9. Change, clear, and set the password again. Confirm existing access remains. Confirm a new character needs the current password.
10. Destroy the boat and build a replacement. Set its password. Confirm the second character must authenticate again. Confirm any dropped cargo follows normal destruction behavior.
11. Confirm unprotected boats, ordinary chests, and ward restrictions retain their normal behavior.
12. Check the relevant server and client logs. Capture proof of the prompt, successful storage access, and access after reconnect and restart.
13. Remove test boats and helpers. Restore and verify all replaced files and profiles before releasing leases.

Remaining risks are live Harmony patch behavior, owner changes, persistence across world saves, and interaction with the full Season 8 mod set. A successful build and the isolated checks do not resolve these risks.
