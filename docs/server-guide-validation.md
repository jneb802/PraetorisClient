# Server guide validation — 2026-09-17 Pacific

## Environment

- Valdev dedicated server and Valnet Client 01, with a development character.
- Isolated `server-guide-ui-s8-8.0.20` profiles on both hosts.
- Season 8 8.0.20 mod files copied from the running production server. Client-only FastLink and BepInEx came from the existing client installation.
- Test configuration came from prior local profiles. This was not a byte-for-byte copy of production configuration.
- Candidate PraetorisClient 0.1.73 on both sides. Test-only enforcement accepted its hash and allowed the valheimCLI test helper.
- Separate-window candidate DLL SHA-256: `6ad8d00a34000bab117463957032556613c983d370354d324822cfff7a88c4a9`.
- Original profiles: Valdev `season8-1-0-migration`; client `praetoris-season-8-8.0.15`.

## Separate Server Guide window

The guide now has its own book icon and window. It no longer adds pages to the
Valheim compendium. The following checks used the separate-window candidate above.

| Check | Result |
| --- | --- |
| Build and local checks | Release build passed with 0 errors and 85 existing warnings. Document, image, markup, and history checks passed. |
| Open from inventory | Opened inventory and clicked the gold book icon. Its tooltip says Server Guide. The window title says Server Guide. |
| Page isolation | The guide showed only Welcome, Getting started, and Building guide. The original compendium still showed its normal entries and no guide pages. Inspected both screenshots. |
| Links and images | Followed links through all three pages. The PNG and its caption appeared correctly. |
| History and scroll | Scrolled Building guide to the end. Back returned to Getting started; Forward restored Building guide at scroll 0.00. |
| Close and reopen | Escape and the Close button closed the guide to inventory. The book icon reopened the previous page with its saved scroll position. |
| Empty guide | Saving an empty server file cleared the page list and history. The open window showed a message that the server has not published pages. |
| Full page list | Loaded 64 pages, scrolled the list to the end, and selected Page 64. Its body appeared correctly. Closing and reopening retained Page 64. |
| Recovery and removed page | Restored the three-page file while Page 64 was open. The window automatically returned to Welcome and cleared obsolete history. |
| Logout | Returned to the main menu. Status reported `pages=0, images=0/0, received=False, revision=`. |

Evidence is retained at
`/Users/benjmarston/Develop/artifacts/server-guide-standalone-20260917/`.
The final run had no guide exception. The existing RecipeManager exception remained.

## Earlier transfer and reader checks

These checks preceded the separate-window change and used candidate
`9363ddca37f7f44e0730355c11db3b8d9ef6f2ebcd6fded93cb8ed40e98e39b8`.
The server text/image transfer code did not change when the window was separated.

| Check | Result |
| --- | --- |
| Release build | Passed with 0 errors and 85 existing warnings. |
| Local checks | Passed: document boundaries, Unicode, page links, image block order, image deduplication, PNG integrity, bounded history, removed-page cleanup, and scroll restoration. |
| Dedicated join | Server logged successful client mod validation. Both hosts used the same candidate DLL hash. |
| Initial synchronization | Three pages and one image received. Server and client revision: `C0imZd5cI4cknCA+y2fmYHJARaUqfDl7dmURVGmJAJA=`. |
| Images | The 100,381-byte PNG transferred in multiple chunks, passed its hash check, and appeared with its caption. The same image appeared on two pages with one download. |
| Page links | Clicked Welcome → Getting started → Building guide. Custom link labels and normal title links both opened the correct page. |
| Back and Forward | Scrolled Building guide to the end, selected Back, then Forward. The page returned to its saved scroll position of 0.00. Inspected the end-of-page screenshot. |
| New history branch | Used Back, then followed a different link. Forward became unavailable. |
| Normal compendium | Selected Active effects. The native reader returned and guide controls disappeared. Selecting Welcome restored the guide reader. |
| Live image replacement | Replaced the server PNG while Welcome remained open. The client received and displayed the replacement without reopening the guide. History remained available. New revision: `jJpcsBQnfa4wz1+riSIamOzJ05MDcxa0hxXuDSiWhIg=`. |
| Invalid PNG | Changed one PNG byte. Server logged a checksum failure once. Client retained the prior revision, pages, and image. |
| Invalid page link | Added a link to a missing page. Server logged the missing target once. Client retained the prior revision, pages, and image. |
| Live text update | Changed the Welcome body while it remained open. The new text appeared automatically, with history retained. |
| Empty file | The open guide returned to the native Deathlink compendium entry. Pages, image metadata, and history cleared. Inspected the screenshot. |
| Recovery | Restored valid text and the original PNG. Client received three pages and one image again, with the original revision. Reopened Welcome successfully. |
| Logout | Returned to the main menu. Status reported `pages=0, images=0/0, received=False, revision=`. |

The [checked-in screenshot](images/server-guide.png) shows the final Welcome page.
Additional screenshots and command evidence are retained locally at
`/Users/benjmarston/Develop/artifacts/server-guide-ui-20260917/`.

## Log limits

The final run had no guide exception. Earlier development candidates exposed UI
compatibility errors; those were corrected before this final build and proof run.
The invalid-image and invalid-link tests produced their intended warnings.

The full mod set did not produce a clean log. RecipeManager threw in
`PieceUpdater.DisablePiece` during configuration setup. Other modules reported
missing assets, prefabs, and shader data. These messages are outside the guide code
and were not fixed here. This run proves the listed guide flows; it does not certify
the health of every mod.

Mouse navigation was exercised. Controller navigation and non-English game
localization were not exercised. Unicode parsing was checked locally.
