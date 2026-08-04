#!/usr/bin/env python3
"""Dump the real serialized field names of `LiveTimelineWorkSheet` from a
live (cutt) asset bundle, together with how much data each track carries.

Why this exists
---------------
The C# classes in ``Assets/Scripts/umamusume/Gallop/Live/Cutt/`` must declare
fields whose names match the game's serialization *exactly* (case sensitive),
otherwise Unity silently deserializes nothing.  The game's cutt bundles ship
with full TypeTrees, so the authoritative names can simply be read back out.

Where the worksheets live
-------------------------
Each song has a bundle group ``cutt/cutt_son<id>/``:

    cutt_son<id>          prefab bundle          (no worksheets)
    data                  LiveTimelineData       (no worksheets)
    son<id>_camera        WorkSheet "son<id>_Camera"
    type_01 .. type_10    WorkSheet "Type_01" ..  (only on newer songs)

A MonoBehaviour is treated as a worksheet when its TypeTree contains the field
``cameraPosKeys``.

Usage
-----
    PY=~/.venvs/umatools/bin/python

    # every worksheet of a song: field table with keyframe counts
    $PY tools/dump_cutt_typetree.py 1157

    # just the schema (field type + name), no bundle data needed
    $PY tools/dump_cutt_typetree.py 1157 --fields

    # only tracks that actually contain keys, one worksheet
    $PY tools/dump_cutt_typetree.py 1157 --sheet Type_01 --nonzero

    # nested schema of one track -- this is how you learn the key class fields
    $PY tools/dump_cutt_typetree.py 1157 --tree laserList

    # first keyframe of a track, as JSON (real values)
    $PY tools/dump_cutt_typetree.py 1157 --sample laserList

    # whole worksheet as JSON
    $PY tools/dump_cutt_typetree.py 1157 --sheet Type_01 --json out.json

    # any bundle path also works instead of a song id
    $PY tools/dump_cutt_typetree.py cutt/cutt_son1001/son1001_camera
"""

from __future__ import annotations

import argparse
import json
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import uma_common as uc  # noqa: E402


def bundles_for(db, target: str) -> list:
    """Resolve a song id or an explicit asset path into meta entries."""
    if re.fullmatch(r"\d+", target):
        entries = db.like("cutt/cutt_son%s/%%" % target)
        if not entries:
            raise SystemExit("no cutt bundles for song id %s" % target)
        # the prefab/data bundles never hold worksheets - skip them cheaply
        return [e for e in entries if not e.name.endswith(("/data", "/cutt_son%s" % target))]
    e = db.get(target)
    if e is None:
        raise SystemExit("no meta entry named %s" % target)
    return [e]


def node_lines(node, depth: int = 0, max_depth: int = 99):
    yield "%s%-46s %s" % ("  " * depth, node.m_Type, node.m_Name)
    if depth >= max_depth:
        return
    for ch in getattr(node, "m_Children", []) or []:
        yield from node_lines(ch, depth + 1, max_depth)


def field_nodes(root):
    """The WorkSheet's own fields, minus the four MonoBehaviour base fields."""
    skip = {"m_GameObject", "m_Enabled", "m_Script", "m_Name"}
    return [ch for ch in root.m_Children if ch.m_Name not in skip]


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("target", help="song id (e.g. 1157) or full asset path")
    ap.add_argument("--sheet", default=None, help="only this worksheet (e.g. Type_01, son1157_Camera)")
    ap.add_argument("--fields", action="store_true", help="print the schema only, skip deserialization")
    ap.add_argument("--nonzero", action="store_true", help="hide tracks with no keyframes")
    ap.add_argument("--tree", metavar="FIELD", help="print the nested TypeTree of one field")
    ap.add_argument("--tree-depth", type=int, default=99, help="depth limit for --tree")
    ap.add_argument("--sample", metavar="FIELD", help="print the first keyframe of one field as JSON")
    ap.add_argument("--json", metavar="PATH", help="write the whole worksheet as JSON")
    ap.add_argument("--groups", action="store_true", help="also list the group names of each track")
    uc.add_common_args(ap)
    args = ap.parse_args(argv)

    uc.require("apsw-sqlite3mc", "UnityPy")

    with uc.MetaDb(args.game_path, args.region) as db:
        entries = bundles_for(db, args.target)
        found_any = False
        for entry in entries:
            try:
                env = db.load(entry)
            except FileNotFoundError as exc:
                print("skip %s: %s" % (entry.name, exc), file=sys.stderr)
                continue
            for name, obj, root in uc.worksheets(env):
                if args.sheet and name.lower() != args.sheet.lower():
                    continue
                found_any = True
                fields = field_nodes(root)
                print("=" * 100)
                print("%s   (%s)   %d fields" % (name, entry.name, len(fields)))
                print("=" * 100)

                if args.tree:
                    node = next((f for f in fields if f.m_Name.lower() == args.tree.lower()), None)
                    if node is None:
                        print("  no such field: %s" % args.tree)
                        continue
                    for line in node_lines(node, 0, args.tree_depth):
                        print(line)
                    continue

                if args.fields:
                    for i, f in enumerate(fields):
                        print("%3d  %-50s %s" % (i, f.m_Type, f.m_Name))
                    continue

                tree = obj.read_typetree()

                if args.sample:
                    key = next((f.m_Name for f in fields if f.m_Name.lower() == args.sample.lower()), None)
                    if key is None:
                        print("  no such field: %s" % args.sample)
                        continue
                    val = tree.get(key)
                    if isinstance(val, list) and val:
                        val = val[0]
                    if isinstance(val, dict) and isinstance(val.get("thisList"), list):
                        val = dict(val, thisList=val["thisList"][:1])
                    if isinstance(val, dict) and isinstance(val.get("keys"), dict):
                        kl = val["keys"]
                        if isinstance(kl.get("thisList"), list):
                            val = dict(val, keys=dict(kl, thisList=kl["thisList"][:1]))
                    print(json.dumps(val, indent=2, ensure_ascii=False, default=str))
                    continue

                if args.json:
                    with open(args.json, "w", encoding="utf-8") as fh:
                        json.dump(tree, fh, indent=1, ensure_ascii=False, default=str)
                    print("wrote %s" % args.json)
                    continue

                print("%3s  %-50s %-46s %7s %7s" % ("#", "serialized type", "field name", "groups", "keys"))
                print("-" * 100)
                total = 0
                for i, f in enumerate(fields):
                    val = tree.get(f.m_Name)
                    keys = uc.count_keyframes(val)
                    groups = uc.count_groups(val)
                    total += keys
                    if args.nonzero and keys == 0 and groups == 0:
                        continue
                    print("%3d  %-50s %-46s %7d %7d" % (i, f.m_Type, f.m_Name, groups, keys))
                    if args.groups:
                        for gn in uc.group_names(val):
                            print("%sname: %s" % (" " * 8, gn))
                print("-" * 100)
                print("total keyframes: %d" % total)
        if not found_any:
            print("no LiveTimelineWorkSheet found", file=sys.stderr)
            return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
