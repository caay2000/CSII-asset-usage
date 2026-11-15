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


STATUSES = [
    ("candidate", "Candidates", "Nothing in these packs is used in the counted cities, directly or through another prefab. Removing a growables pack reduces zone variety."),
    ("manual", "Review by hand", "Unused, but they bring things a city scan cannot see: zones, themes, maps, asset packs, UI or styles."),
    ("protected", "Protected", "Listed in protected.txt, so never candidates."),
    ("used", "Used", "At least one prefab is in a counted city, directly or through another prefab."),
    ("unattributed", "Unattributed", "Prefabs created at runtime (Extra Assets Importer, code mods) or of unknown origin. Never candidates."),
]

STYLE = """
:root{--bg:#f6f7f8;--panel:#ffffff;--fg:#1d2329;--muted:#5d6873;--line:#dde2e7;--accent:#1f6f8b;
--cand:#a34a12;--cand-bg:#fbeee4;--manual:#7a5c00;--manual-bg:#fbf3d9;--used:#2d6a3e;--used-bg:#e5f3e8;
--prot:#4b4f9c;--prot-bg:#ebecfa;--un:#5d6873;--un-bg:#eceff2;
--sans:"Inter",system-ui,sans-serif;--head:"Space Grotesk","Inter",system-ui,sans-serif;--mono:"JetBrains Mono",ui-monospace,monospace}
@media (prefers-color-scheme:dark){:root:not([data-theme="light"]){--bg:#14181c;--panel:#1b2026;--fg:#e4e8ec;--muted:#98a3ae;--line:#2c333b;--accent:#5fb3cf;
--cand:#f0a070;--cand-bg:#3a2518;--manual:#e2c25a;--manual-bg:#342c12;--used:#7fcf94;--used-bg:#18301f;--prot:#a9adf2;--prot-bg:#24264a;--un:#98a3ae;--un-bg:#232a31;color-scheme:dark}}
:root[data-theme="dark"]{--bg:#14181c;--panel:#1b2026;--fg:#e4e8ec;--muted:#98a3ae;--line:#2c333b;--accent:#5fb3cf;
--cand:#f0a070;--cand-bg:#3a2518;--manual:#e2c25a;--manual-bg:#342c12;--used:#7fcf94;--used-bg:#18301f;--prot:#a9adf2;--prot-bg:#24264a;--un:#98a3ae;--un-bg:#232a31;color-scheme:dark}
body{background:var(--bg);color:var(--fg);font-family:var(--sans);font-size:14px;line-height:1.5}
.wrap{max-width:1120px;margin:0 auto;padding-block:32px 64px;padding-left:max(16px,env(safe-area-inset-left,0px));padding-right:max(16px,env(safe-area-inset-right,0px));display:grid;gap:28px}
h1,h2{font-family:var(--head);text-wrap:balance;margin:0}
h1{font-size:28px}h2{font-size:19px;display:flex;gap:10px;align-items:baseline}
h2 .n{font-family:var(--mono);font-size:13px;color:var(--muted)}
p{margin:0;max-width:70ch;color:var(--muted)}
.lead{display:grid;gap:6px}
.saving{display:flex;flex-wrap:wrap;gap:12px 32px;align-items:baseline;background:var(--panel);border:1px solid var(--line);border-radius:10px;padding:18px 20px}
.saving .big{font-family:var(--head);font-size:34px;font-weight:600;color:var(--cand);font-variant-numeric:tabular-nums}
.tiles{display:grid;grid-template-columns:repeat(auto-fit,minmax(150px,1fr));gap:10px}
.tile{display:grid;gap:2px;padding:12px 14px;border-radius:8px;background:var(--panel);border:1px solid var(--line);text-decoration:none;color:inherit}
.tile b{font-family:var(--mono);font-size:20px;font-variant-numeric:tabular-nums}
.tile span{font-size:12px;color:var(--muted)}
.pill{display:inline-block;padding:1px 8px;border-radius:999px;font-size:12px;font-weight:600;letter-spacing:.02em}
.candidate{color:var(--cand);background:var(--cand-bg)}.manual{color:var(--manual);background:var(--manual-bg)}
.used{color:var(--used);background:var(--used-bg)}.protected{color:var(--prot);background:var(--prot-bg)}.unattributed{color:var(--un);background:var(--un-bg)}
section{display:grid;gap:10px;min-width:0}
.scroll{overflow-x:auto;background:var(--panel);border:1px solid var(--line);border-radius:10px}
table{border-collapse:collapse;width:100%;font-size:13px}
th,td{padding:7px 12px;text-align:left;vertical-align:top;border-bottom:1px solid var(--line)}
tr:last-child td{border-bottom:0}
th{font-size:11px;text-transform:uppercase;letter-spacing:.06em;color:var(--muted);font-weight:600;white-space:nowrap}
td.num{font-family:var(--mono);font-variant-numeric:tabular-nums;text-align:right;white-space:nowrap}
td.id,.ex{font-family:var(--mono);font-size:12px;color:var(--muted)}
a{color:var(--accent)}a:focus-visible,input:focus-visible{outline:2px solid var(--accent);outline-offset:2px}
input{font:inherit;padding:8px 12px;border-radius:8px;border:1px solid var(--line);background:var(--panel);color:var(--fg);width:100%;max-width:360px}
.empty{color:var(--muted);font-style:italic}
"""

COUNTED = '<span class="pill used">counted</span>'
LEFT_OUT = '<span class="pill unattributed">left out</span>'

SCRIPT = """
document.getElementById('q').addEventListener('input',function(e){var q=e.target.value.toLowerCase();
document.querySelectorAll('tr[data-s]').forEach(function(r){r.hidden=q&&r.dataset.s.indexOf(q)<0;});});
"""


def types_text(types):
    top = sorted(types.items(), key=lambda t: -t[1])[:3]
    return html.escape(", ".join(f"{t.replace('Prefab', '')} {n}" for t, n in top))


def link(r):
    name = html.escape(r["name"])
    if r["pdxId"]:
        return f'<a href="https://mods.paradoxplaza.com/mods/{html.escape(str(r["pdxId"]))}/Windows">{name}</a>'
    return name


def table(rows, used):
    head = ["Pack", "Id", "Prefabs", "Size"] + (["Direct", "Indirect", "Examples"] if used else ["Types"])
    out = ["<div class='scroll'><table><tr>" + "".join(f"<th>{h}</th>" for h in head) + "</tr>"]
    for r in rows:
        search = html.escape(f"{r['name']} {r['id']}".lower())
        cells = [link(r), f'<span class="id">{html.escape(r["id"])}</span>', f'<td class="num">{r["prefabs"]}</td>',
                 f'<td class="num">{human(r["size"]) if r["size"] else "—"}</td>']
        if used:
            cells += [f'<td class="num">{r["direct"]}</td>', f'<td class="num">{r["indirect"]}</td>',
                      "<td class='ex'>" + "<br>".join(html.escape(e) for e in r["examples"]) + "</td>"]
        else:
            cells += [f"<td>{types_text(r['types'])}</td>"]
        tds = "".join(c if c.startswith("<td") else f"<td>{c}</td>" for c in cells)
        out.append(f'<tr data-s="{search}">{tds}</tr>')
    out.append("</table></div>")
    return "".join(out)


def write_html(args, rows, usage, cities, gone):
    by = defaultdict(list)
    for r in rows:
        by[r["status"]].append(r)
    for key in by:
        by[key].sort(key=lambda r: (-r["size"], -r["prefabs"]))

    candidates = by["candidate"]
    saving = sum(r["size"] for r in candidates)
    saving_prefabs = sum(r["prefabs"] for r in candidates)
    tiles = "".join(
        f'<a class="tile" href="#{key}"><b>{len(by[key])}</b><span class="pill {key}">{title}</span></a>'
        for key, title, _ in STATUSES
    )
    city_rows = "".join(
        f"<tr><td>{html.escape(c.get('name') or '')}</td><td class='id'>{html.escape(cid)}</td>"
        f"<td class='num'>{html.escape(str(c.get('firstSeen', '')))}</td><td class='num'>{html.escape(str(c.get('lastSeen', '')))}</td>"
        f"<td>{COUNTED if not cities or cid in cities else LEFT_OUT}</td></tr>"
        for cid, c in usage.get("cities", {}).items()
    )
    sections = []
    for key, title, note in STATUSES:
        body = table(by[key], key == "used") if by[key] else '<p class="empty">None.</p>'
        sections.append(f'<section id="{key}"><h2>{title} <span class="n">{len(by[key])}</span></h2><p>{note}</p>{body}</section>')
    gone_rows = "".join(f"<li><span class='id'>{html.escape(p)}</span>: {n} used prefabs</li>" for p, n in sorted(gone.items()))
    since = "all time" if args.since == "0000-00-00" else "since " + html.escape(args.since)

    counted = len(cities) if cities else len(usage.get("cities", {}))
    city_word = "city" if counted == 1 else "cities"
    saving_block = (f'<div class="saving"><p>Pack sizes were not measured (--no-sizes): {len(candidates)} candidate packs ({saving_prefabs} prefabs) that no counted city uses.</p></div>'
                    if args.no_sizes else
                    f'<div class="saving"><span class="big">{human(saving)}</span><p>on disk across {len(candidates)} candidate packs ({saving_prefabs} prefabs) that no counted city uses.</p></div>')
    page = f"""<title>Asset Usage Report</title>
<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Inter:wght@400;600&family=JetBrains+Mono:wght@400;600&family=Space+Grotesk:wght@600&display=swap">
<style>{STYLE}</style>
<div class="wrap">
<div class="lead"><h1>Asset usage report</h1>
<p>Usage {since} in {counted} counted {city_word}. Generated from inventory.json and usage.json. This report only lists packs; nothing is removed.</p></div>
{saving_block}
<div class="tiles">{tiles}</div>
<section><h2>Cities <span class="n">{len(usage.get("cities", {}))}</span></h2>
<p>Only cities loaded with the mod count. A pack used only in a city you have not loaded since installing the mod shows up as a candidate.</p>
<div class="scroll"><table><tr><th>Name</th><th>Id</th><th>First seen</th><th>Last seen</th><th></th></tr>{city_rows}</table></div></section>
<input id="q" type="search" aria-label="Filter packs by name or id" placeholder="Filter packs by name or id">
{"".join(sections)}
<section><h2>Used packs not loaded now <span class="n">{len(gone)}</span></h2><ul>{gone_rows or '<li class="empty">None.</li>'}</ul></section>
</div>
<script>{SCRIPT}</script>"""
    out = os.path.join(args.out, "report.html")
    with open(out, "w", encoding="utf-8") as f:
        f.write("<!doctype html><html lang='en'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'></head><body>" + page + "</body></html>")
    print(f"Report: {out}")


def safe_cell(text):
    return "'" + text if text and text[0] in "=+-@" else text


def write_candidates(args, rows):
    candidates = sorted((r for r in rows if r["status"] == "candidate"), key=lambda r: (-r["size"], -r["prefabs"]))
    path = os.path.join(args.out, "candidates.csv")
    with open(path, "w", newline="", encoding="utf-8") as f:
        writer = csv.writer(f)
        writer.writerow(["id", "pdxId", "name", "sizeBytes", "prefabs"])
        for r in candidates:
            writer.writerow([r["id"], r["pdxId"], safe_cell(r["name"]), r["size"], r["prefabs"]])
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
