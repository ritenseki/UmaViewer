#!/usr/bin/env python3
"""Decrypt any game asset bundle and inspect it with UnityPy.

Given an asset path from the meta DB (e.g. ``3d/effect/live/pfb_eff_live1157``)
this locates the hashed file under ``dat/``, undoes the XOR cipher and hands
the result to UnityPy.

Usage
-----
    PY=~/.venvs/umatools/bin/python

    # list every object in the bundle
    $PY tools/dump_bundle.py list 3d/env/live/live1014/pfb_env_live1014_controller000

    # object names of one type only
    $PY tools/dump_bundle.py list cutt/cutt_son1157/type_01 --type MonoBehaviour

    # TypeTree schema of an object (by path_id, or the first of a type)
    $PY tools/dump_bundle.py tree cutt/cutt_son1157/type_01 --type MonoBehaviour --depth 2

    # deserialized object as JSON
    $PY tools/dump_bundle.py json cutt/cutt_son1157/type_01 -o /tmp/ws.json

    # write the decrypted bundle itself (loadable by UnityPy/AssetStudio)
    $PY tools/dump_bundle.py save cutt/cutt_son1157/type_01 -o /tmp/type_01.unity3d

    # game object hierarchy of a prefab bundle
    $PY tools/dump_bundle.py gameobjects 3d/env/live/live1014/pfb_env_live1014_controller000

``--deps`` also loads the bundle's prerequisites, which is needed when an
object references assets living in another bundle.
"""

from __future__ import annotations

import argparse
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import uma_common as uc  # noqa: E402


def pick(env, args):
    """Objects selected by --type / --path-id, in file order."""
    objs = list(env.objects)
    if args.path_id is not None:
        objs = [o for o in objs if o.path_id == args.path_id]
    if args.type:
        objs = [o for o in objs if o.type.name.lower() == args.type.lower()]
    return objs


def obj_name(obj) -> str:
    try:
        d = obj.read_typetree()
        return d.get("m_Name") or ""
    except Exception:
        return ""


def node_lines(node, depth=0, max_depth=99):
    yield "%s%-46s %s" % ("  " * depth, node.m_Type, node.m_Name)
    if depth >= max_depth:
        return
    for ch in getattr(node, "m_Children", []) or []:
        yield from node_lines(ch, depth + 1, max_depth)


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("cmd", choices=("list", "tree", "json", "save", "gameobjects"))
    ap.add_argument("name", help="asset path as stored in the meta DB")
    ap.add_argument("--type", default=None, help="filter by Unity class name (MonoBehaviour, Material, ...)")
    ap.add_argument("--path-id", type=int, default=None)
    ap.add_argument("--depth", type=int, default=99, help="depth limit for `tree`")
    ap.add_argument("--deps", action="store_true", help="also load the bundle's dependencies")
    ap.add_argument("-o", "--output", default=None)
    uc.add_common_args(ap)
    args = ap.parse_args(argv)

    uc.require("apsw-sqlite3mc", "UnityPy")

    with uc.MetaDb(args.game_path, args.region) as db:
        entry = db.get(args.name)
        if entry is None:
            print("no meta entry named %s" % args.name, file=sys.stderr)
            return 1

        if args.cmd == "save":
            out = args.output or (os.path.basename(entry.name) + ".unity3d")
            with open(out, "wb") as fh:
                fh.write(db.bundle_bytes(entry))
            print("wrote %s (%d bytes, key=%d)" % (out, os.path.getsize(out), entry.key))
            return 0

        env = db.load(entry, with_deps=args.deps)

        if args.cmd == "list":
            counts: dict[str, int] = {}
            for o in pick(env, args):
                counts[o.type.name] = counts.get(o.type.name, 0) + 1
                print("%-22s %-22d %s" % (o.type.name, o.path_id, obj_name(o)))
            print("-- " + ", ".join("%s=%d" % kv for kv in sorted(counts.items())), file=sys.stderr)
            return 0

        if args.cmd == "gameobjects":
            for o in env.objects:
                if o.type.name != "GameObject":
                    continue
                try:
                    d = o.read_typetree()
                except Exception:
                    continue
                print("%-22d %-40s active=%s components=%d" % (
                    o.path_id, d.get("m_Name", ""), d.get("m_IsActive"), len(d.get("m_Component", []))))
            return 0

        objs = pick(env, args)
        if not objs:
            print("no matching object", file=sys.stderr)
            return 1

        if args.cmd == "tree":
            for o in objs:
                node = getattr(getattr(o, "serialized_type", None), "node", None)
                print("=" * 90)
                print("%s  path_id=%d  %s" % (o.type.name, o.path_id, obj_name(o)))
                print("=" * 90)
                if node is None:
                    print("  (bundle carries no TypeTree for this object)")
                    continue
                for line in node_lines(node, 0, args.depth):
                    print(line)
            return 0

        # json
        data = []
        for o in objs:
            try:
                data.append(o.read_typetree())
            except Exception as exc:
                print("skip %s %d: %s" % (o.type.name, o.path_id, exc), file=sys.stderr)
        payload = data[0] if len(data) == 1 else data
        text = json.dumps(payload, indent=1, ensure_ascii=False, default=str)
        if args.output:
            with open(args.output, "w", encoding="utf-8") as fh:
                fh.write(text)
            print("wrote %s (%d object(s))" % (args.output, len(data)))
        else:
            print(text)
        return 0


if __name__ == "__main__":
    raise SystemExit(main())
