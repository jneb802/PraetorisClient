# Boss spawn message validation

Validated on 1 October 2026 with Valheim 1.0.16, Valdev, and both Valnet clients.
Production still ran Praetoris Season 8 **8.0.31**. Tests used the maintained
`praetoris-season-8` profiles and a copied season world. Production was not joined or changed.

The setting defaults to enabled on the server:

```ini
[BossMessages]
SuppressBossSpawnMessages = true
```

The server and clients must run the matching PraetorisClient feature. Connected
clients cannot change this setting, including administrator clients. Other
administrator-controlled settings keep their existing behavior. The client
restores its previous local setting after leaving the server.

## Full Season 8 results

| Check | Result |
| --- | --- |
| Server starts enabled; both client files start disabled | Both clients received the enabled server value |
| Connected client assigns a different value | Rejected |
| Client edits its file and reloads it | Runtime retained the server value |
| Administrator client submits a config package with suppression disabled | Server retained enabled; other client retained enabled |
| Same administrator package changes an unrelated synced setting | Unrelated value changed on server and other client |
| Server disables suppression | Connected clients received disabled |
| Administrator client attempts to enable it against server policy | Rejected on client, server, and receiving client |
| Server enables suppression again | Both clients received enabled |
| Display assertions while enabled | 71 assertions passed per client; repeated on final build |
| Display assertions while disabled | 43 assertions passed |
| Actual Eikthyr and Elder spawn/alert callbacks while enabled | Previous center message remained unchanged |
| Actual Eikthyr and Elder spawn/alert callbacks while disabled | Both normal announcements displayed |
| Logout with server enabled and original local value disabled | Both clients restored disabled locally |

The display assertions cover the legacy boss events, all seven inspected vanilla
bosses, received ShowMessage calls, localized messages, local event activation,
message logs, boss death text, sacrifice feedback, ordinary raids, and top-left
notifications. The server accepted the administrator's actual network package;
the unrelated setting in that package proved the request reached the receiver.

The logout test first exposed a defect in the strict lock: Jotunn restores cached
local values before ZNet is destroyed, so the lock also blocked restoration.
The final build permits that managed restoration. The repeat test confirmed the
disabled local value returned on both clients after logout.

Normal Eikthyr and Elder altar offerings passed in the earlier reduced-set proof.
The new full-mirror test could not generate those temporary location IDs through
`TestSpawnLocation`; no full-mirror altar-offering success is claimed. Actual boss
spawns and the shared display paths passed with the full mod set.

## Environment and evidence

Before candidates were applied, 472 server plugin/patcher files and 425 intended
client files matched the production release hashes. The changing profiler log
was excluded. The full production mod set remained installed, with its intended
server-only and client-only differences. The ServerSideTweaks PR #47 candidate
was also installed for separate global-key validation.

Recorded test additions were command probes and valheimCLI, candidate/helper hash
exceptions in the test mod policy, disabled external metric upload, and removed
external service credentials in copied configs. Administrator grants applied only
to the test server's scratch save directory. Temporary probes were corrected to
use reflection for private game members before the final administrator proof.

The Release build passed with zero errors and 85 existing warnings. No warning
came from the new boss feature files. The final logs contained no feature
exception. Existing startup shader/video errors, PieceManager snapshot errors,
asset/audio warnings, and test-helper audit warnings remained.

Commands, sanitized logs, baseline hashes, and restoration evidence:

`/Users/benjmarston/Develop/valheim-validation-evidence/pr-followup-20261001/`

Test probes and game assemblies are not included in this pull request.
