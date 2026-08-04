#!/usr/bin/env python3
"""Scan every live song and report how many keyframes each timeline track has.

This is the "which track should I implement next" tool.  It walks all
``cutt/cutt_son<id>/`` bundle groups, deserializes every LiveTimelineWorkSheet
inside them, and counts the keyframes of each of the ~124 track fields.

Outputs (default directory: tools/out/)
---------------------------------------
    scan_keys.csv     song x track matrix, cell = number of keyframes
    scan_groups.csv   song x track matrix, cell = number of track groups
                      (list length for grouped tracks, keyframe count for
                       plain key lists)
    scan_summary.txt  per-track totals, sorted by how many songs use the track
    scan.json         everything above in machine readable form, plus the
                      per-song worksheet inventory and the serialized type of
                      each field

Aggregation
-----------
A song usually ships several worksheets (``son<id>_Camera`` plus ``Type_01`` ..
``Type_10``); the type sheets are variations of one another.  By default a
song's value for a track is the MAX over its worksheets, so ten near-identical
sheets do not inflate the number.  ``--agg sum`` and ``--per-sheet`` are
available if you want the raw numbers.

Usage
-----
    PY=~/.venvs/umatools/bin/python

    $PY tools/scan_live_tracks.py                    # everything, to tools/out/
    $PY tools/scan_live_tracks.py --songs 1157 1001  # only these songs
    $PY tools/scan_live_tracks.py --per-sheet        # one row per worksheet
    $PY tools/scan_live_tracks.py --out-dir /tmp/x --quiet

    # what the summary looks like:
    #   track field                songs  total keys   max   example songs
    #   blinkLightList                56       12345   410   1157, 1156, ...
"""

from __future__ import annotations

import argparse
import csv
import json
import os
import sys
import time
from collections import OrderedDict

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import uma_common as uc  # noqa: E402

DEFAULT_OUT_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "out")

BASE_FIELDS = {"m_GameObject", "m_Enabled", "m_Script", "m_Name"}
# scalar header fields that are not tracks
SCALAR_FIELDS = {
    "version", "targetCameraIndex", "enableAtRuntime", "enableAtEdit",
    "TotalTimeLength", "Lyrics", "SheetType", "SheetVariationId", "IsVariationSheet",
}


def scan_song(db, song_id: str, verbose: bool = True):
    """Return ``{sheet_name: {field: (groups, keys)}}`` plus field types."""
    sheets = OrderedDict()
    types: dict[str, str] = {}
    entries = db.like("cutt/cutt_son%s/%%" % song_id)
    for entry in entries:
        if entry.name.endswith(("/data", "/cutt_son%s" % song_id)):
            continue  # never carry worksheets, skip the decrypt cost
        try:
            env = db.load(entry)
        except FileNotFoundError as exc:
            if verbose:
                print("    skip %s (%s)" % (entry.name, exc), file=sys.stderr)
            continue
        except Exception as exc:
            print("    FAILED %s: %s" % (entry.name, exc), file=sys.stderr)
            continue
        for name, obj, root in uc.worksheets(env):
            try:
                tree = obj.read_typetree()
            except Exception as exc:
                print("    FAILED typetree %s/%s: %s" % (entry.name, name, exc), file=sys.stderr)
                continue
            row: dict[str, tuple[int, int]] = {}
            for ch in root.m_Children:
                f = ch.m_Name
                if f in BASE_FIELDS or f in SCALAR_FIELDS:
                    continue
                types.setdefault(f, ch.m_Type)
                val = tree.get(f)
                row[f] = (uc.count_groups(val), uc.count_keyframes(val))
            sheets["%s|%s" % (song_id, name)] = row
    return sheets, types


def write_matrix(path: str, rows: "OrderedDict[str, dict]", fields: list, idx: int) -> None:
    with open(path, "w", newline="", encoding="utf-8") as fh:
        w = csv.writer(fh)
        w.writerow(["song"] + fields)
        for row_name, values in rows.items():
            w.writerow([row_name] + [values.get(f, (0, 0))[idx] for f in fields])


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--songs", nargs="*", default=None, help="song ids to scan (default: all)")
    ap.add_argument("--out-dir", default=DEFAULT_OUT_DIR)
    ap.add_argument("--agg", choices=("max", "sum"), default="max", help="how to fold a song's worksheets")
    ap.add_argument("--per-sheet", action="store_true", help="keep one row per worksheet instead of per song")
    ap.add_argument("--quiet", action="store_true")
    uc.add_common_args(ap)
    args = ap.parse_args(argv)

    uc.require("apsw-sqlite3mc", "UnityPy")
    os.makedirs(args.out_dir, exist_ok=True)

    t0 = time.time()
    rows: "OrderedDict[str, dict]" = OrderedDict()
    types: dict[str, str] = {}
    inventory: dict[str, list] = {}

    with uc.MetaDb(args.game_path, args.region) as db:
        song_ids = args.songs or db.song_ids()
        for i, sid in enumerate(song_ids, 1):
            if not args.quiet:
                print("[%2d/%d] son%s" % (i, len(song_ids), sid), file=sys.stderr)
            sheets, t = scan_song(db, sid, verbose=not args.quiet)
            types.update(t)
            inventory[sid] = [k.split("|", 1)[1] for k in sheets]
            if not sheets:
                continue
            if args.per_sheet:
                rows.update(sheets)
            else:
                merged: dict[str, tuple[int, int]] = {}
                for values in sheets.values():
                    for f, (g, k) in values.items():
                        pg, pk = merged.get(f, (0, 0))
                        merged[f] = (max(pg, g), max(pk, k)) if args.agg == "max" else (pg + g, pk + k)
                rows["son%s" % sid] = merged

    # keep the game's own field order (types dict is insertion ordered)
    fields = list(types.keys())

    keys_csv = os.path.join(args.out_dir, "scan_keys.csv")
    groups_csv = os.path.join(args.out_dir, "scan_groups.csv")
    write_matrix(groups_csv, rows, fields, 0)
    write_matrix(keys_csv, rows, fields, 1)

    stats = []
    for f in fields:
        vals = [(name, v.get(f, (0, 0))[1]) for name, v in rows.items()]
        used = [n for n, k in vals if k > 0]
        stats.append(
            {
                "field": f,
                "type": types[f],
                "songs": len(used),
                "total": sum(k for _, k in vals),
                "max": max([k for _, k in vals] or [0]),
                "examples": used[:6],
            }
        )
    stats.sort(key=lambda s: (-s["songs"], -s["total"]))

    summary = os.path.join(args.out_dir, "scan_summary.txt")
    with open(summary, "w", encoding="utf-8") as fh:
        fh.write("LiveTimelineWorkSheet track usage over %d rows (%s)\n" % (len(rows), args.agg))
        fh.write("%-46s %-50s %6s %9s %7s\n" % ("field", "serialized type", "songs", "total", "max"))
        fh.write("-" * 125 + "\n")
        for s in stats:
            fh.write("%-46s %-50s %6d %9d %7d\n" % (s["field"], s["type"], s["songs"], s["total"], s["max"]))

    js = os.path.join(args.out_dir, "scan.json")
    with open(js, "w", encoding="utf-8") as fh:
        json.dump(
            {
                "rows": {n: {f: list(v) for f, v in vals.items()} for n, vals in rows.items()},
                "field_types": types,
                "stats": stats,
                "worksheets": inventory,
                "aggregation": "per-sheet" if args.per_sheet else args.agg,
            },
            fh,
            indent=1,
        )

    print("scanned %d rows / %d fields in %.1fs" % (len(rows), len(fields), time.time() - t0))
    for p in (keys_csv, groups_csv, summary, js):
        print("  %s" % p)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
