#!/usr/bin/env python3
import argparse
import csv
import glob
import html
import json
import os
import re
from collections import defaultdict

GAME_DATA = "/mnt/c/Users/caay2/AppData/LocalLow/Colossal Order/Cities Skylines II"
NON_INSTANCED = re.compile(
    r"^(Zone|Theme|AssetPack|Content|UI\w*|\w*Style\w*|Terrain\w*|Map\w*|Climate\w*|Weather\w*|\w*Settings|"
    r"\w*Configuration\w*|Notification\w*|Infoview\w*|Tutorial\w*|Achievement\w*|Unlock\w*|Policy|Brand|Company|"
    r"Economy\w*|Service\w*|Feature|Milestone|Signature\w*)Prefab$"
)


def load_json(path):
    with open(path, encoding="utf-8-sig") as f:
        return json.load(f)


def pdx_names(game_data):
    path = os.path.join(game_data, ".cache", "Mods", "pdx_mods_cache.json")
    if not os.path.exists(path):
        return {}
    return {str(item.get("id")): item.get("displayName") for item in load_json(path).get("items", [])}


def folder_size(path):
    total = 0
    for root, _, files in os.walk(path):
        for name in files:
            try:
                total += os.path.getsize(os.path.join(root, name))
            except OSError:
                pass
    return total


def pack_size(game_data, pack):
    if pack.get("source") == "pdx" and pack.get("pdxId"):
        folders = glob.glob(os.path.join(game_data, ".cache", "Mods", "pdx_mods", f"{pack['pdxId']}_*"))
        return sum(folder_size(f) for f in folders)
    path = pack.get("path")
    if not path:
        return 0
    path = path.replace("\\", "/")
    match = re.match(r"^([A-Za-z]):/(.*)$", path)
    if match:
        path = f"/mnt/{match.group(1).lower()}/{match.group(2)}"
    return folder_size(path) if os.path.isdir(path) else 0


def used_prefabs(usage, cities, since):
    used = {}
    for key, record in usage.get("prefabs", {}).items():
        for city_id, city in record.get("cities", {}).items():
            if cities and city_id not in cities:
                continue
            if city.get("lastDirect") and city["lastDirect"] >= since:
                used[key] = ("direct", record, city)
                break
            if city.get("lastIndirect") and city["lastIndirect"] >= since and key not in used:
                used[key] = ("indirect", record, city)
    return used


def classify(pack_id, pack, stats, protected):
    if pack["source"] == "game":
        return "game"
    if pack["source"] in ("runtime", "unknown") or pack_id.endswith(":?"):
        return "unattributed"
    if stats["direct"] + stats["indirect"] > 0:
        return "used"
    if pack_id in protected or pack.get("pdxId") in protected:
        return "protected"
    if any(NON_INSTANCED.match(t) for t in pack.get("types", {})):
        return "manual"
    return "candidate"


def human(size):
    for unit in ("B", "KB", "MB", "GB"):
        if size < 1024 or unit == "GB":
            return f"{size:.0f} {unit}" if unit == "B" else f"{size:.1f} {unit}"
        size /= 1024


def build(args):
    data = args.data
    inventory = load_json(os.path.join(data, "inventory.json"))
    usage_path = os.path.join(data, "usage.json")
    usage = load_json(usage_path) if os.path.exists(usage_path) else {"cities": {}, "prefabs": {}}
    protected_path = os.path.join(data, "protected.txt")
    protected = set()
    if os.path.exists(protected_path):
        with open(protected_path, encoding="utf-8") as f:
            protected = {line.strip() for line in f if line.strip() and not line.startswith("#")}
    cities = set(args.cities.split(",")) if args.cities else set()
    names = pdx_names(args.game_data)
    used = used_prefabs(usage, cities, args.since)

    stats = defaultdict(lambda: {"direct": 0, "indirect": 0, "examples": []})
    for key, prefab in inventory["prefabs"].items():
        if key not in used:
            continue
        kind, record, city = used[key]
        for pack_id in prefab["packs"]:
            s = stats[pack_id]
            s[kind] += 1
            if len(s["examples"]) < 4 and (kind == "direct" or len(s["examples"]) < 2):
                via = "" if kind == "direct" else " via " + ", ".join((city.get("via") or [])[:2])
                s["examples"].append(f"{key}{via}")

    loaded = set(inventory["packs"].keys())
    gone = defaultdict(int)
    for key, (kind, record, city) in used.items():
        if key not in inventory["prefabs"]:
            for pack_id in record.get("packs", []):
                if pack_id not in loaded:
                    gone[pack_id] += 1

    rows = []
    for pack_id, pack in inventory["packs"].items():
        s = stats[pack_id]
        name = pack.get("name") or names.get(pack.get("pdxId") or "") or pack_id
        if pack.get("pdxId") and names.get(pack["pdxId"]):
            name = names[pack["pdxId"]]
        status = classify(pack_id, pack, s, protected)
        size = pack_size(args.game_data, pack) if status in ("candidate", "manual", "used", "protected") and not args.no_sizes else 0
        rows.append({
            "id": pack_id, "name": name, "source": pack["source"], "pdxId": pack.get("pdxId") or "",
            "prefabs": pack["prefabCount"], "direct": s["direct"], "indirect": s["indirect"], "status": status,
            "size": size, "types": pack.get("types", {}), "examples": s["examples"],
        })

    write_html(args, rows, usage, cities, gone)
    write_candidates(args, rows)


def section(title, note, rows, columns):
    if not rows:
        return f"<h2>{html.escape(title)} (0)</h2>"
    head = "".join(f"<th>{html.escape(c)}</th>" for c, _ in columns)
    body = "\n".join("<tr>" + "".join(f"<td>{cell(r)}</td>" for _, cell in columns) + "</tr>" for r in rows)
    return f"<h2>{html.escape(title)} ({len(rows)})</h2><p>{note}</p><table><tr>{head}</tr>{body}</table>"


def types_text(types):
    top = sorted(types.items(), key=lambda t: -t[1])[:4]
    return html.escape(", ".join(f"{t} {n}" for t, n in top))


def link(r):
    name = html.escape(r["name"])
    if r["pdxId"]:
        return f'<a href="https://mods.paradoxplaza.com/mods/{r["pdxId"]}/Windows">{name}</a>'
    return name


def write_html(args, rows, usage, cities, gone):
    by = defaultdict(list)
    for r in rows:
        by[r["status"]].append(r)
    for key in by:
        by[key].sort(key=lambda r: (-r["size"], -r["prefabs"]))

    city_rows = "".join(
        f"<tr><td>{html.escape(c.get('name') or '')}</td><td>{html.escape(cid)}</td><td>{c.get('firstSeen','')}</td>"
        f"<td>{c.get('lastSeen','')}</td><td>{'yes' if not cities or cid in cities else 'no'}</td></tr>"
        for cid, c in usage.get("cities", {}).items()
    )
    base = [("Pack", link), ("Id", lambda r: html.escape(r["id"])), ("Prefabs", lambda r: r["prefabs"]),
            ("Size", lambda r: human(r["size"]) if r["size"] else ""), ("Types", lambda r: types_text(r["types"]))]
    used_cols = base[:3] + [("Direct", lambda r: r["direct"]), ("Indirect", lambda r: r["indirect"]),
                            ("Examples", lambda r: "<br>".join(html.escape(e) for e in r["examples"]))]
    total_candidates = sum(r["size"] for r in by["candidate"])
    summary = ", ".join(f"{k}: {len(v)}" for k, v in sorted(by.items()))
    gone_html = "".join(f"<li>{html.escape(p)}: {n} used prefabs</li>" for p, n in sorted(gone.items()))

    page = f"""<!doctype html><html><head><meta charset="utf-8"><title>Asset usage report</title>
<style>
body{{font-family:system-ui,sans-serif;margin:24px;color:#222;background:#fff}}
table{{border-collapse:collapse;margin-bottom:24px;font-size:13px}}
td,th{{border:1px solid #ccc;padding:4px 8px;text-align:left;vertical-align:top}}
th{{background:#f0f0f0}} h2{{margin-top:32px}} p{{max-width:900px}}
</style></head><body>
<h1>Asset usage report</h1>
<p>Usage since {html.escape(args.since)}. Packs: {summary}. Candidates add up to {human(total_candidates)}.</p>
<h2>Cities</h2><table><tr><th>Name</th><th>Id</th><th>First seen</th><th>Last seen</th><th>Counted</th></tr>{city_rows}</table>
<p>Only cities loaded with the mod count. A pack used only in a city you have not loaded since installing the mod will show as a candidate.</p>
{section("Candidates", "No prefab of these packs is used, directly or through another prefab, in the counted cities. Removing a pack of growables reduces zone variety.", by["candidate"], base)}
{section("Review by hand", "Unused, but they bring types the scan cannot see in a city (zones, themes, asset packs, UI, styles...).", by["manual"], base)}
{section("Protected", "Listed in protected.txt.", by["protected"], base)}
{section("Used", "", by["used"], used_cols)}
{section("Unattributed", "Prefabs created at runtime (Extra Assets Importer, code mods) or with an unknown origin. Never candidates.", by["unattributed"], base)}
<h2>Used packs not loaded now</h2><ul>{gone_html or "<li>none</li>"}</ul>
</body></html>"""
    out = os.path.join(args.out, "report.html")
    with open(out, "w", encoding="utf-8") as f:
        f.write(page)
    print(f"Report: {out}")


def write_candidates(args, rows):
    candidates = sorted((r for r in rows if r["status"] == "candidate"), key=lambda r: (-r["size"], -r["prefabs"]))
    path = os.path.join(args.out, "candidates.csv")
    with open(path, "w", newline="", encoding="utf-8") as f:
        writer = csv.writer(f)
        writer.writerow(["id", "pdxId", "name", "sizeBytes", "prefabs"])
        for r in candidates:
            writer.writerow([r["id"], r["pdxId"], r["name"], r["size"], r["prefabs"]])
    print(f"Candidates: {path} ({len(candidates)})")


def main():
    default_data = os.path.join(GAME_DATA, "ModsData", "asset-usage")
    parser = argparse.ArgumentParser(description="Report which asset packs your cities use.")
    parser.add_argument("--data", default=default_data, help="folder with inventory.json and usage.json")
    parser.add_argument("--game-data", default=GAME_DATA, help="Cities Skylines II user data folder")
    parser.add_argument("--out", default=None, help="output folder (default: --data)")
    parser.add_argument("--cities", default="", help="comma separated city ids to count (default: all)")
    parser.add_argument("--since", default="0000-00-00", help="count usage on or after this day (YYYY-MM-DD)")
    parser.add_argument("--no-sizes", action="store_true", help="skip measuring pack folders")
    args = parser.parse_args()
    args.out = args.out or args.data
    build(args)


if __name__ == "__main__":
    main()
