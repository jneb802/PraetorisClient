# Server guide

Install this PraetorisClient build on the server and clients. Players open their
inventory and select **Compendium**. Pages appear first with a **Server Guide:**
prefix. The normal compendium entries remain available. Players can also run
`praetoris_guide` in the game console.

![Server guide displayed in the compendium](images/server-guide.png)

## Write pages

The server creates `BepInEx/config/PraetorisClient.ServerGuide.txt` on first start.
Edit the file in the active server profile. No client file is needed.

```text
# Welcome
Welcome to Praetoris.

Open the Rules page before you start building.

# Rules
Respect other players and their buildings.

# Getting started
Write your server's starting instructions here.
```

- Start each page with `# ` followed by its title.
- Pages appear in file order. Titles must be unique.
- Use blank lines between paragraphs. This is a text format, not a Markdown renderer.
- The game supports its normal rich-text tags in page bodies, such as `<b>bold</b>`.
- Titles can contain up to 80 characters. They cannot contain `<`, `>` or `$`.
- Each body can contain up to 16,000 characters.
- A guide can contain up to 64 pages and 128 KiB of UTF-8 text.
- Save an empty file to remove all guide pages. Deleting the file creates the sample again.

The server checks for edits every five seconds. Connected players check for a new
version every ten seconds. Close and reopen the compendium to display updated
pages. Players receive pages automatically when they join. Pages are held for the
current connection and are not written to the player's saved compendium.

If an edit is invalid, the server logs the reason and keeps the last valid guide.
Correct and save the file to try again. Only the connected server can supply
pages; clients cannot edit or upload the guide.

## Check operation

- `praetoris_guide_status`: show the received page count and content revision.
- `praetoris_guide_reload`: force a reload from the server console.
- Client log: `Received server guide: ...`.
- Server log: `Loaded server guide: ...`.

Client and server revisions must match after synchronization.

Run parser checks with `dotnet run --project tests/ServerGuide/ServerGuide.Tests.csproj`.

See [live validation](server-guide-validation.md) for the tested environment and results.
