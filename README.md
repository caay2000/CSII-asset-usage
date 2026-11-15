# CSII Asset Usage

A Cities: Skylines II mod that inventories every loaded asset, records which ones your cities use, and reports
which packs you never use, so you can trim your playset and shorten loading times. It only reports: it never
unsubscribes, disables or deletes anything.

Status: internal, not published. Design in `docs/feature/`.

## How it works

- When a city finishes loading, the mod reads every loaded prefab, works out the pack it comes from (Paradox Mods id,
  local folder under `Mods/`, the game, or "runtime" for prefabs made by Extra Assets Importer and code mods) and the
  prefabs it references. It writes this to `ModsData/asset-usage/inventory.json`. The city opens without waiting for it:
  - If the loaded prefabs are the same as in the saved inventory (and it is under 7 days old), the saved one is reused.
  - Otherwise the inventory is built a few milliseconds per frame while you play.
- It then reads every entity in the city and records the prefabs in use. While you play, it adds the prefabs of new
  entities (placed by any tool, or grown by the game).
- Everything a used prefab references (meshes, sub-objects, sub-nets, variants...) counts as used indirectly.
- History per city goes to `ModsData/asset-usage/usage.json`: first and last day each prefab was seen, directly or
  indirectly. It is written when the city loads, every 5 minutes, when you save, when you leave the city and when
  the game closes.
- The log is `Logs/asset-usage.log`.

## Report

```
python3 tools/usage_report.py [--cities id1,id2] [--since 2026-11-14] [--no-sizes]
```

It writes `report.html` and `candidates.csv` next to the data. Packs are split into candidates (nothing used),
review by hand (unused but bring zones, themes, UI or other types the scan cannot see), excluded, protected, used and
unattributed. A pack whose only unseen type is its asset pack filter still counts as a candidate. Paradox Mods packs show their
cover from the game's mods cache (`.cache/Mods/pdx_mods_cache.json`), loaded from Paradox's servers when the report opens. The same cache gives each pack's dependencies:
- each row says which packs need it;
- a candidate that a used pack depends on goes to review by hand;
- the top bar warns when an excluded pack is needed by a pack you keep.

Each candidate or review row has Protect and Exclude buttons. The browser remembers the choices until you save them
with the buttons in the top bar as `protected.txt` and `excluded.txt` in `ModsData/asset-usage`; the next report reads
both. Protected packs are never candidates, and `excluded.txt` is the list of packs to turn off in Skyve. Both files
take one pack id or Paradox Mods id per line; text after `#` is ignored. Saving keeps the lines and comments the report has no row for. If the
browser downloads the file instead of asking where to save it, move it from Downloads into that folder.

Only cities loaded with the mod count, so load each city you play at least once before trusting the candidates.

## Build

- Tests: `dotnet test AssetUsage.Tests`
- Deploy (game closed): `dotnet build AssetUsage -p:Deploy=true` copies the DLL to `Mods/asset-usage`.
