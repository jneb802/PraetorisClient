# Server guide validation — 2026-09-16 Pacific

## Environment

- Valdev dedicated server and Valnet Client 01, with a development character.
- Isolated `server-guide-s8-8.0.19` profiles on both hosts.
- Mod files copied from the running Season 8 production server. Its loaded package versions matched the 8.0.19 release. The later 8.0.20 staging directory did not match the running server.
- Candidate PraetorisClient 0.1.73 installed on both sides. The server's test-only enforcement file accepted the candidate hash and allowed the valheimCLI test helper.
- Client FastLink and BepInEx came from the existing installation. Test configuration came from the previous local profiles; this was not a byte-for-byte copy of production configuration.
- Original profiles: Valdev `season8-1-0-migration`; client `praetoris-season-8-8.0.15`.
- Candidate DLL SHA-256: `82d1a0d7c4a6844bd14cc69a146a29bac741fa4c3f28f9db6f8603c563149d6b`.

## Results

| Check | Result |
| --- | --- |
| Release build | Passed, 0 errors. The same 85 warnings also occurred before this feature was added. |
| Parser checks | Passed: page order, line endings, Unicode, empty guide, duplicate titles, title constraints, body size, byte size, and page-count limits. |
| Dedicated join | Connected through the public game address; server logged `Client mod list validated successfully`. The Tailscale game-address attempt failed. |
| Initial synchronization | Both sides reported three pages with revision `Gn4NLi7oPOf06ZLG4G0yu83eHu5uX2z5tQsKNCweslI=`. |
| Browse pages | Opened the compendium and clicked Welcome, Getting started, and Building guide. Inspected screenshots of the first two pages. |
| Long page | Scrolled to the end of the Building guide, including section 24. Inspected screenshot. |
| Live edit | Changed the Welcome body on the server without restarting. Both sides reported revision `Pb/QeXjDwEFVIjE8wSKxWhB3+8dmXjwdZA9jrWWUFkk=`. Reopened the compendium and inspected the updated text. |
| Invalid edit | Saved text without a heading. Server logged the validation reason once; client retained all three pages and the previous revision. |
| Empty file | Client received zero pages with revision `47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU=`. Screenshot showed normal compendium entries without guide pages. |
| Recovery | Restored the valid file. Client received the original three-page revision again. |
| Logout | Returned to the main menu. `praetoris_guide_status` reported `pages=0, received=False, revision=`. |

The checked-in screenshot shows the Welcome page. Additional screenshots and trimmed
guide logs are retained locally at
`/Users/benjmarston/Develop/artifacts/server-guide-20260916/`.

## Log limits

No guide exception occurred. The malformed-file test produced its intended warning.
The full mod set did not produce a clean log: RecipeManager 0.6.1 threw in
`PieceUpdater.DisablePiece` during server setup and client configuration receipt.
Other modules reported missing assets or prefabs, fallback requirements, a raid-config
migration, and a location-loading timeout. The dedicated server also logged graphics
resource errors. These messages are outside the guide code and were not fixed here.
This run proves the guide flows above; it does not certify the health of every mod.

Mouse navigation was exercised. Controller navigation and non-English game localization
were not exercised. The parser's Unicode support was checked locally.
