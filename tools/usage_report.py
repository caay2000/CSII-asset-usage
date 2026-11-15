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


def pdx_items(game_data):
    path = os.path.join(game_data, ".cache", "Mods", "pdx_mods_cache.json")
    if not os.path.exists(path):
        return {}
    return {str(item.get("id")): item for item in load_json(path).get("items", [])}


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


def manual_reasons(types):
    found = [t for t in types if NON_INSTANCED.match(t)]
    if len(types) > 1:
        found = [t for t in found if t != "AssetPackPrefab"]
    return sorted(found)


def base_status(pack_id, pack, stats):
    if pack["source"] == "game":
        return "game"
    if pack["source"] in ("runtime", "unknown") or pack_id.endswith(":?"):
        return "unattributed"
    if stats["direct"] + stats["indirect"] > 0:
        return "used"
    if manual_reasons(pack.get("types", {})):
        return "manual"
    return "candidate"


def decision_for(pack_id, pack, decisions):
    for key in (pack_id, pack.get("pdxId")):
        if key and key in decisions:
            return decisions[key]
    return ""


def classify(pack_id, pack, stats, decisions):
    base = base_status(pack_id, pack, stats)
    decision = decision_for(pack_id, pack, decisions)
    if base in ("candidate", "manual") and decision:
        return base, decision
    return base, base


LIST_HEADER = "written by the asset usage report"


def list_state(path, rows):
    rows_by_key = {}
    for r in rows:
        if r["base"] in ("candidate", "manual"):
            rows_by_key[r["id"]] = r["id"]
            if r["pdxId"]:
                rows_by_key[r["pdxId"]] = r["id"]
    ids, keep = [], []
    if os.path.exists(path):
        with open(path, encoding="utf-8-sig") as f:
            for line in f:
                line = line.rstrip("\r\n")
                key = line.split("#")[0].strip()
                if key in rows_by_key:
                    ids.append(rows_by_key[key])
                elif key or (line.strip() and LIST_HEADER not in line):
                    keep.append(line)
    return {"ids": ids, "keep": keep}


PACK_PREFIX = "pdx:"


def link_dependencies(rows, decisions, packs):
    by_id = {r["id"]: r for r in rows}
    for r in rows:
        for dep in r["deps"]:
            by_id[dep]["needed_by"].append(r["id"])
    for r in rows:
        if r["base"] == "candidate" and any(by_id[d]["base"] == "used" for d in r["needed_by"]):
            r["base"] = "manual"
            r["status"] = decision_for(r["id"], packs[r["id"]], decisions) or "manual"


def read_ids(path):
    if not os.path.exists(path):
        return set()
    with open(path, encoding="utf-8-sig") as f:
        return {line.split("#")[0].strip() for line in f} - {""}


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
    decisions = {i: "protected" for i in read_ids(os.path.join(data, "protected.txt"))}
    decisions.update({i: "excluded" for i in read_ids(os.path.join(data, "excluded.txt"))})
    cities = set(args.cities.split(",")) if args.cities else set()
    items = pdx_items(args.game_data)
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
        item = items.get(pack.get("pdxId") or "", {})
        name = item.get("displayName") or pack.get("name") or pack_id
        thumb = item.get("thumbnailPath") or ""
        base, status = classify(pack_id, pack, s, decisions)
        deps = [f"{PACK_PREFIX}{d['id']}" for d in item.get("dependencies") or [] if d.get("type") == "MOD" and d.get("id")]
        size = pack_size(args.game_data, pack) if base in ("candidate", "manual", "used") and not args.no_sizes else 0
        rows.append({
            "id": pack_id, "name": name, "source": pack["source"], "pdxId": pack.get("pdxId") or "", "thumb": thumb if thumb.startswith("https://") else "",
            "prefabs": pack["prefabCount"], "direct": s["direct"], "indirect": s["indirect"], "status": status,
            "base": base, "listed": decision_for(pack_id, pack, decisions), "size": size, "types": pack.get("types", {}), "examples": s["examples"],
            "deps": [d for d in deps if d in inventory["packs"] and d != pack_id], "needed_by": [],
        })
    link_dependencies(rows, decisions, inventory["packs"])

    write_html(args, rows, usage, cities, gone)
    write_candidates(args, rows)


STATUSES = [
    ("candidate", "Candidates", "Nothing in these packs is used in the counted cities, directly or through another prefab. Removing a growables pack reduces zone variety."),
    ("manual", "Review by hand", "Unused, but they bring things a city scan cannot see (zones, themes, maps, UI or styles), or a used pack depends on them."),
    ("excluded", "Excluded", "Marked to remove. Save excluded.txt and use it to turn these packs off in Skyve."),
    ("protected", "Protected", "Marked to keep, so never candidates. Saved in protected.txt."),
    ("used", "Used", "At least one prefab is in a counted city, directly or through another prefab."),
    ("unattributed", "Unattributed", "Prefabs created at runtime (Extra Assets Importer, code mods) or of unknown origin. Never candidates."),
]
DECIDABLE = ("candidate", "manual", "excluded", "protected")

STYLE = """
:root{--bg:#f6f7f8;--panel:#ffffff;--fg:#1d2329;--muted:#5d6873;--line:#dde2e7;--accent:#1f6f8b;
--cand:#a34a12;--cand-bg:#fbeee4;--manual:#7a5c00;--manual-bg:#fbf3d9;--used:#2d6a3e;--used-bg:#e5f3e8;
--prot:#4b4f9c;--prot-bg:#ebecfa;--excl:#a3263a;--excl-bg:#fbe6e9;--un:#5d6873;--un-bg:#eceff2;
--sans:"Inter",system-ui,sans-serif;--head:"Space Grotesk","Inter",system-ui,sans-serif;--mono:"JetBrains Mono",ui-monospace,monospace}
@media (prefers-color-scheme:dark){:root:not([data-theme="light"]){--bg:#14181c;--panel:#1b2026;--fg:#e4e8ec;--muted:#98a3ae;--line:#2c333b;--accent:#5fb3cf;
--cand:#f0a070;--cand-bg:#3a2518;--manual:#e2c25a;--manual-bg:#342c12;--used:#7fcf94;--used-bg:#18301f;--prot:#a9adf2;--prot-bg:#24264a;--excl:#f28b9b;--excl-bg:#3d1a21;--un:#98a3ae;--un-bg:#232a31;color-scheme:dark}}
:root[data-theme="dark"]{--bg:#14181c;--panel:#1b2026;--fg:#e4e8ec;--muted:#98a3ae;--line:#2c333b;--accent:#5fb3cf;
--cand:#f0a070;--cand-bg:#3a2518;--manual:#e2c25a;--manual-bg:#342c12;--used:#7fcf94;--used-bg:#18301f;--prot:#a9adf2;--prot-bg:#24264a;--excl:#f28b9b;--excl-bg:#3d1a21;--un:#98a3ae;--un-bg:#232a31;color-scheme:dark}
body{background:var(--bg);color:var(--fg);font-family:var(--sans);font-size:14px;line-height:1.5;margin:0}
.wrap{max-width:1120px;margin:0 auto;padding-block:24px 64px;padding-left:max(16px,env(safe-area-inset-left,0px));padding-right:max(16px,env(safe-area-inset-right,0px));display:grid;gap:28px}
h1,h2{font-family:var(--head);text-wrap:balance;margin:0}
h1{font-size:28px}h2{font-size:19px;display:flex;gap:10px;align-items:baseline}
h2 .n{font-family:var(--mono);font-size:13px;color:var(--muted)}
p{margin:0;max-width:70ch;color:var(--muted)}
.lead{display:grid;gap:6px}
.bar{position:sticky;top:0;z-index:5;background:var(--bg);border-bottom:1px solid var(--line)}
.bar .in{max-width:1120px;margin:0 auto;padding:10px max(16px,env(safe-area-inset-right,0px)) 10px max(16px,env(safe-area-inset-left,0px));display:grid;gap:8px}
.row{display:flex;flex-wrap:wrap;gap:8px;align-items:center}
.saving{display:flex;flex-wrap:wrap;gap:12px 32px;align-items:baseline;background:var(--panel);border:1px solid var(--line);border-radius:10px;padding:18px 20px}
.saving .big{font-family:var(--head);font-size:34px;font-weight:600;color:var(--cand);font-variant-numeric:tabular-nums}
.tiles{display:flex;flex-wrap:wrap;gap:6px}
.tile{display:flex;gap:8px;align-items:center;padding:5px 10px;border-radius:8px;background:var(--panel);border:1px solid var(--line);text-decoration:none;color:inherit}
.tile b{font-family:var(--mono);font-size:14px;font-variant-numeric:tabular-nums}
.pill{display:inline-block;padding:1px 8px;border-radius:999px;font-size:12px;font-weight:600;letter-spacing:.02em}
.candidate{color:var(--cand);background:var(--cand-bg)}.manual{color:var(--manual);background:var(--manual-bg)}
.used{color:var(--used);background:var(--used-bg)}.protected{color:var(--prot);background:var(--prot-bg)}
.excluded{color:var(--excl);background:var(--excl-bg)}.unattributed{color:var(--un);background:var(--un-bg)}
section{display:grid;gap:10px;min-width:0}
.scroll{overflow-x:auto;background:var(--panel);border:1px solid var(--line);border-radius:10px}
table{border-collapse:collapse;width:100%;font-size:13px}
th,td{padding:7px 12px;text-align:left;vertical-align:top;border-bottom:1px solid var(--line)}
tr:last-child td{border-bottom:0}
th{font-size:11px;text-transform:uppercase;letter-spacing:.06em;color:var(--muted);font-weight:600;white-space:nowrap}
td.num{font-family:var(--mono);font-variant-numeric:tabular-nums;text-align:right;white-space:nowrap}
.needed{font-size:12px;color:var(--muted)}
.needed.conflict{color:var(--excl);font-weight:600}
td.id,.id,.ex,.why{font-family:var(--mono);font-size:12px;color:var(--muted)}
td.act{white-space:nowrap}
.pack{display:flex;gap:10px;align-items:center;min-width:220px}
.thumb{flex:none;width:96px;height:54px;border-radius:6px;object-fit:cover;background:var(--line)}
img.thumb{cursor:zoom-in}
#preview{position:fixed;z-index:10;width:384px;height:216px;object-fit:cover;border-radius:10px;pointer-events:none;box-shadow:0 8px 30px rgba(0,0,0,.4);background:var(--line)}
tbody:not(:empty)+tbody.none{display:none}
a{color:var(--accent)}a:focus-visible,input:focus-visible,button:focus-visible{outline:2px solid var(--accent);outline-offset:2px}
input{font:inherit;padding:6px 12px;border-radius:8px;border:1px solid var(--line);background:var(--panel);color:var(--fg);flex:1 1 220px;max-width:360px}
button{font:inherit;font-size:12px;font-weight:600;padding:3px 10px;border-radius:6px;border:1px solid var(--line);background:var(--panel);color:var(--fg);cursor:pointer}
button.keep[aria-pressed="true"]{color:var(--prot);background:var(--prot-bg);border-color:var(--prot)}
button.drop[aria-pressed="true"]{color:var(--excl);background:var(--excl-bg);border-color:var(--excl)}
button.save{padding:6px 12px}
.dirty{font-size:12px;color:var(--excl)}
.empty{color:var(--muted);font-style:italic}
"""

COUNTED = '<span class="pill used">counted</span>'
LEFT_OUT = '<span class="pill unattributed">left out</span>'

SCRIPT = """
(function(){
var lists=JSON.parse(document.getElementById('lists').textContent);
var KEY='asset-usage-decisions:'+lists.folder;
var inFile={protected:new Set(lists.protected.ids),excluded:new Set(lists.excluded.ids)};
function fileDecision(id){return inFile.excluded.has(id)?'excluded':inFile.protected.has(id)?'protected':'';}
function rows(){return Array.prototype.slice.call(document.querySelectorAll('tr[data-id]'));}
var known=new Set(rows().map(function(tr){return tr.dataset.id;}));
var overrides={};
try{overrides=JSON.parse(localStorage.getItem(KEY))||{};}catch(e){}
function prune(){Object.keys(overrides).forEach(function(id){if(!known.has(id)||overrides[id]===fileDecision(id))delete overrides[id];});}
prune();
function store(){try{localStorage.setItem(KEY,JSON.stringify(overrides));}catch(e){}}
function decision(id){return id in overrides?overrides[id]:fileDecision(id);}
function human(n){var u=['B','KB','MB','GB'],i=0;while(n>=1024&&i<3){n/=1024;i++;}return i?n.toFixed(1)+' '+u[i]:n+' B';}
function pad(){var bar=document.querySelector('nav.bar');document.documentElement.style.scrollPaddingTop=(bar.offsetHeight+12)+'px';}
pad();window.addEventListener('resize',pad);
function place(tr){
var d=decision(tr.dataset.id);
tr.querySelector('.keep').setAttribute('aria-pressed',d==='protected');
tr.querySelector('.drop').setAttribute('aria-pressed',d==='excluded');
var body=document.querySelector('#'+(d||tr.dataset.base)+' tbody.list');
if(tr.parentNode!==body)body.appendChild(tr);}
function refresh(){
var counts={},saving=0,prefabs=0;
document.querySelectorAll('tbody.list').forEach(function(b){
var sorted=Array.prototype.slice.call(b.children).sort(function(x,y){return (+y.dataset.size-+x.dataset.size)||(+y.dataset.prefabs-+x.dataset.prefabs);});
sorted.forEach(function(r){b.appendChild(r);});
var key=b.closest('section').id;counts[key]=b.children.length;
if(key==='candidate'){sorted.forEach(function(r){saving+=+r.dataset.size;prefabs+=+r.dataset.prefabs;});}});
Object.keys(counts).forEach(function(k){document.querySelectorAll('[data-count="'+k+'"]').forEach(function(e){e.textContent=counts[k];});});
var big=document.getElementById('saving');if(big)big.textContent=human(saving);
var cand=document.getElementById('cand-n');if(cand)cand.textContent=counts.candidate;
var cp=document.getElementById('cand-p');if(cp)cp.textContent=prefabs;
var conflicts=0;
rows().forEach(function(tr){var n=tr.querySelector('.needed');if(!n)return;
var bad=decision(tr.dataset.id)==='excluded'&&n.dataset.needed.split(' ').some(function(id){return !known.has(id)||decision(id)!=='excluded';});
n.classList.toggle('conflict',bad);if(bad)conflicts++;});
var cw=document.getElementById('conflicts');cw.hidden=!conflicts;
cw.textContent=conflicts+(conflicts===1?' excluded pack is':' excluded packs are')+' needed by packs you keep';
var pending=Object.keys(overrides).length;
document.getElementById('dirty').hidden=!pending;
document.getElementById('dirty').textContent=pending+(pending===1?' change':' changes')+' not saved to the files yet';}
function toggle(tr,kind){
var id=tr.dataset.id,next=decision(id)===kind?'':kind;
if(next===fileDecision(id))delete overrides[id];else overrides[id]=next;
store();place(tr);refresh();}
rows().forEach(function(tr){
tr.querySelector('.keep').addEventListener('click',function(){toggle(tr,'protected');});
tr.querySelector('.drop').addEventListener('click',function(){toggle(tr,'excluded');});
place(tr);});
refresh();
function text(kind){
var lines=['# '+kind+'.txt written by the asset usage report. One pack id per line; text after # is ignored.'].concat(lists[kind].keep);
rows().forEach(function(tr){if(decision(tr.dataset.id)===kind)lines.push(tr.dataset.id+'  # '+tr.dataset.name.replace(/[\\r\\n]+/g,' '));});
return lines.join('\\n')+'\\n';}
function save(kind){
var name=kind+'.txt',body=text(kind);
function done(){var now=new Set(rows().filter(function(tr){return decision(tr.dataset.id)===kind;}).map(function(tr){return tr.dataset.id;}));
inFile[kind]=now;prune();store();refresh();}
if(window.showSaveFilePicker){
window.showSaveFilePicker({suggestedName:name,types:[{description:'Text',accept:{'text/plain':['.txt']}}]})
.then(function(h){return h.createWritable().then(function(w){return w.write(body).then(function(){return w.close();});});})
.then(done).catch(function(e){if(e&&e.name!=='AbortError')alert('Could not save '+name+': '+e.message);});
return;}
var a=document.createElement('a');a.href=URL.createObjectURL(new Blob([body],{type:'text/plain'}));a.download=name;
document.body.appendChild(a);a.click();a.remove();done();}
document.getElementById('save-protected').addEventListener('click',function(){save('protected');});
document.getElementById('save-excluded').addEventListener('click',function(){save('excluded');});
var preview=document.getElementById('preview');
document.addEventListener('error',function(e){var t=e.target;if(t.classList&&t.classList.contains('thumb'))t.style.visibility='hidden';},true);
document.addEventListener('mouseover',function(e){var t=e.target;if(!(t.classList&&t.classList.contains('thumb')&&t.tagName==='IMG'))return;
var r=t.getBoundingClientRect(),w=384,h=216,left=r.right+12,top=r.top+r.height/2-h/2;
if(left+w>window.innerWidth-8)left=Math.max(8,r.left-w-12);
top=Math.min(Math.max(8,top),window.innerHeight-h-8);
preview.src=t.currentSrc||t.src;preview.style.left=left+'px';preview.style.top=top+'px';preview.hidden=false;});
document.addEventListener('mouseout',function(e){if(e.target.classList&&e.target.classList.contains('thumb'))preview.hidden=true;});
window.addEventListener('scroll',function(){preview.hidden=true;},{passive:true});
document.getElementById('q').addEventListener('input',function(e){var q=e.target.value.toLowerCase();
document.querySelectorAll('tr[data-s]').forEach(function(r){r.hidden=q&&r.dataset.s.indexOf(q)<0;});});
})();
"""


def types_text(types):
    top = sorted(types.items(), key=lambda t: -t[1])[:3]
    return html.escape(", ".join(f"{t.replace('Prefab', '')} {n}" for t, n in top))


def thumb(r):
    if not r["thumb"]:
        return '<span class="thumb"></span>'
    return f'<img class="thumb" src="{html.escape(r["thumb"])}" alt="" loading="lazy" decoding="async" referrerpolicy="no-referrer">'


def link(r):
    name = html.escape(r["name"])
    if r["pdxId"]:
        return f'<a href="https://mods.paradoxplaza.com/mods/{html.escape(str(r["pdxId"]))}/Windows" target="_blank" rel="noopener">{name}</a>'
    return name


def head_row(used):
    head = ["Pack", "Id", "Prefabs", "Size"] + (["Direct", "Indirect", "Examples"] if used else ["Types", "Keep or remove"])
    return "<thead><tr>" + "".join(f"<th>{h}</th>" for h in head) + "</tr></thead>"


def needed_html(r, by_id):
    if not r["needed_by"]:
        return ""
    names = ", ".join(sorted(by_id[i]["name"] for i in r["needed_by"]))
    ids = " ".join(r["needed_by"])
    return f"<div class='needed' data-needed='{html.escape(ids)}'>needed by {html.escape(names)}</div>"


def row_html(r, used, by_id):
    search = html.escape(f"{r['name']} {r['id']}".lower())
    cells = [f'<td><div class="pack">{thumb(r)}<span>{link(r)}</span></div></td>', f'<span class="id">{html.escape(r["id"])}</span>', f'<td class="num">{r["prefabs"]}</td>',
             f'<td class="num">{human(r["size"]) if r["size"] else "—"}</td>']
    if used:
        cells += [f'<td class="num">{r["direct"]}</td>', f'<td class="num">{r["indirect"]}</td>',
                  "<td class='ex'>" + "<br>".join(html.escape(e) for e in r["examples"]) + "</td>"]
    else:
        reasons = manual_reasons(r["types"]) if r["base"] == "manual" else []
        why = f"<div class='why'>brings {html.escape(', '.join(t.replace('Prefab', '') for t in reasons))}</div>" if reasons else ""
        cells += [f"<td>{types_text(r['types'])}{why}{needed_html(r, by_id)}</td>",
                  '<td class="act"><button type="button" class="keep" aria-pressed="false">Protect</button> '
                  '<button type="button" class="drop" aria-pressed="false">Exclude</button></td>']
    tds = "".join(c if c.startswith("<td") else f"<td>{c}</td>" for c in cells)
    if used:
        return f'<tr data-s="{search}">{tds}</tr>'
    return (f'<tr data-s="{search}" data-id="{html.escape(r["id"])}" data-base="{r["base"]}" data-name="{html.escape(r["name"])}" '
            f'data-size="{r["size"]}" data-prefabs="{r["prefabs"]}">{tds}</tr>')


def table(key, rows, by_id):
    used = key not in DECIDABLE
    if used and not rows:
        return '<p class="empty">None.</p>'
    cols = 7 if used else 6
    body = "".join(row_html(r, used, by_id) for r in rows)
    return (f"<div class='scroll'><table>{head_row(used)}<tbody class='list'>{body}</tbody>"
            f"<tbody class='none'><tr><td colspan='{cols}' class='empty'>None.</td></tr></tbody></table></div>")


def lists_json(args, rows):
    lists = {kind: list_state(os.path.join(args.data, f"{kind}.txt"), rows) for kind in ("protected", "excluded")}
    lists["folder"] = args.data
    return json.dumps(lists).replace("</", "<\\/")


def write_html(args, rows, usage, cities, gone):
    by_id = {r["id"]: r for r in rows}
    by = defaultdict(list)
    for r in rows:
        by[r["status"]].append(r)
    for key in by:
        by[key].sort(key=lambda r: (-r["size"], -r["prefabs"]))

    candidates = by["candidate"]
    saving = sum(r["size"] for r in candidates)
    saving_prefabs = sum(r["prefabs"] for r in candidates)
    tiles = "".join(
        f'<a class="tile" href="#{key}"><b data-count="{key}">{len(by[key])}</b><span class="pill {key}">{title}</span></a>'
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
        sections.append(f'<section id="{key}"><h2>{title} <span class="n" data-count="{key}">{len(by[key])}</span></h2><p>{note}</p>{table(key, by[key], by_id)}</section>')
    listed_used = sorted(r["name"] for r in rows if r["listed"] == "excluded" and r["base"] == "used")
    warning = (f"<section><h2>Excluded but used <span class='n'>{len(listed_used)}</span></h2>"
               f"<p>Listed in excluded.txt, but a counted city uses them, so they stay under Used: "
               f"{html.escape(', '.join(listed_used))}.</p></section>") if listed_used else ""
    gone_rows = "".join(f"<li><span class='id'>{html.escape(p)}</span>: {n} used prefabs</li>" for p, n in sorted(gone.items()))
    since = "all time" if args.since == "0000-00-00" else "since " + html.escape(args.since)

    counted = len(cities) if cities else len(usage.get("cities", {}))
    city_word = "city" if counted == 1 else "cities"
    saving_block = (f'<div class="saving"><p>Pack sizes were not measured (--no-sizes): <span id="cand-n">{len(candidates)}</span> candidate packs (<span id="cand-p">{saving_prefabs}</span> prefabs) that no counted city uses.</p></div>'
                    if args.no_sizes else
                    f'<div class="saving"><span class="big" id="saving">{human(saving)}</span><p>on disk across <span id="cand-n">{len(candidates)}</span> candidate packs (<span id="cand-p">{saving_prefabs}</span> prefabs) that no counted city uses.</p></div>')
    folder = html.escape(args.data)
    page = f"""<title>Asset Usage Report</title>
<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Inter:wght@400;600&family=JetBrains+Mono:wght@400;600&family=Space+Grotesk:wght@600&display=swap">
<style>{STYLE}</style>
<script type="application/json" id="lists">{lists_json(args, rows)}</script>
<nav class="bar" aria-label="Sections"><div class="in">
<div class="tiles">{tiles}</div>
<div class="row"><input id="q" type="search" aria-label="Filter packs by name or id" placeholder="Filter packs by name or id">
<button type="button" class="save" id="save-protected">Save protected.txt</button>
<button type="button" class="save" id="save-excluded">Save excluded.txt</button>
<span class="dirty" id="dirty" hidden></span>
<a class="dirty" id="conflicts" href="#excluded" hidden></a></div>
</div></nav>
<img id="preview" alt="" hidden referrerpolicy="no-referrer">
<div class="wrap">
<div class="lead"><h1>Asset usage report</h1>
<p>Usage {since} in {counted} counted {city_word}. Generated from inventory.json and usage.json. This report only lists packs; nothing is removed.</p>
<p>Protect or exclude packs with the buttons, then save both files into <span class="id">{folder}</span> so the next report keeps them. The browser remembers unsaved choices until then.</p></div>
{saving_block}
<section><h2>Cities <span class="n">{len(usage.get("cities", {}))}</span></h2>
<p>Only cities loaded with the mod count. A pack used only in a city you have not loaded since installing the mod shows up as a candidate.</p>
<div class="scroll"><table><tr><th>Name</th><th>Id</th><th>First seen</th><th>Last seen</th><th></th></tr>{city_rows}</table></div></section>
{"".join(sections)}
{warning}
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
