#!/usr/bin/env python3
"""Decrypt and query the game's `meta` database.

The meta DB maps an asset path (``cutt/cutt_son1157/type_01``) to the hashed
file name under ``dat/`` plus the per-bundle XOR key.  It is a SQLite database
encrypted with SQLite3 Multiple Ciphers (ChaCha20); see ``uma_common.py`` for
the key derivation.

Usage
-----
    PY=~/.venvs/umatools/bin/python

    # write a plaintext copy you can open with any sqlite client
    $PY tools/dump_meta.py plain                      # -> tools/out/meta_plain.db
    $PY tools/dump_meta.py plain -o /tmp/meta.db

    # search asset paths (SQL LIKE pattern, % is the wildcard)
    $PY tools/dump_meta.py find 'cutt/cutt_son1157/%'
    $PY tools/dump_meta.py find '%live1014%' --limit 20

    # everything known about one asset, including where it lives on disk
    $PY tools/dump_meta.py info cutt/cutt_son1157/type_01

    # list the live song ids that have cutt bundles
    $PY tools/dump_meta.py songs

Table ``a`` columns (see Assets/Scripts/UmaDatabase/UmaDatabaseEntry.cs):
    i  row id            n  asset path          d  ';'-joined dependencies
    g  ?                 l  length              c  checksum
    h  hashed filename   m  asset type          e  per-bundle cipher key
"""

from __future__ import annotations

import argparse
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import uma_common as uc  # noqa: E402

DEFAULT_OUT_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "out")


def cmd_plain(args) -> int:
    """Copy the decrypted meta DB into a plaintext SQLite file.

    ``sqlite3_backup`` and ``VACUUM INTO`` both refuse / re-encrypt here (the
    ChaCha20 codec reserves bytes per page, so source and target page layouts
    are incompatible), so the schema and rows are copied explicitly.
    """
    uc.require("apsw-sqlite3mc")
    import apsw

    out = args.output or os.path.join(DEFAULT_OUT_DIR, "meta_plain.db")
    os.makedirs(os.path.dirname(os.path.abspath(out)), exist_ok=True)
    for suffix in ("", "-journal", "-wal", "-shm"):
        if os.path.exists(out + suffix):
            os.remove(out + suffix)

    with uc.MetaDb(args.game_path, args.region) as db:
        dest = apsw.Connection(out)
        try:
            schema = list(
                db.con.execute(
                    "SELECT type,name,sql FROM sqlite_master "
                    "WHERE sql IS NOT NULL AND name NOT LIKE 'sqlite\\_%' ESCAPE '\\'"
                )
            )
            tables = [(n, sql) for t, n, sql in schema if t == "table"]
            indexes = [(n, sql) for t, n, sql in schema if t == "index"]
            total = 0
            for name, sql in tables:
                dest.execute(sql)
                rows = list(db.con.execute('SELECT * FROM "%s"' % name))
                if rows:
                    ph = ",".join("?" * len(rows[0]))
                    with dest:
                        dest.executemany('INSERT INTO "%s" VALUES (%s)' % (name, ph), rows)
                total += len(rows)
                print("  %-16s %8d rows" % (name, len(rows)))
            for name, sql in indexes:
                try:
                    dest.execute(sql)
                except Exception as exc:  # a stale index definition must not kill the dump
                    print("  index %s skipped (%s)" % (name, exc), file=sys.stderr)
        finally:
            dest.close()
    print("wrote %s (%d rows total, %.1f MB)" % (out, total, os.path.getsize(out) / 1048576.0))
    return 0


def cmd_find(args) -> int:
    pattern = args.pattern if "%" in args.pattern else "%" + args.pattern + "%"
    with uc.MetaDb(args.game_path, args.region) as db:
        rows = db.like(pattern)
        if args.limit:
            rows = rows[: args.limit]
        for e in rows:
            on_disk = "" if os.path.isfile(e.path(db.game_path)) else "  [NOT DOWNLOADED]"
            print("%-60s %-34s key=%-22d%s" % (e.name, e.url, e.key, on_disk))
        print("-- %d entr%s" % (len(rows), "y" if len(rows) == 1 else "ies"), file=sys.stderr)
    return 0


def cmd_info(args) -> int:
    with uc.MetaDb(args.game_path, args.region) as db:
        e = db.get(args.name)
        if e is None:
            print("no such asset: %s" % args.name, file=sys.stderr)
            return 1
        p = e.path(db.game_path)
        print("name         : %s" % e.name)
        print("type         : %s" % e.type)
        print("hash (url)   : %s" % e.url)
        print("path         : %s%s" % (p, "" if os.path.isfile(p) else "   [NOT DOWNLOADED]"))
        if os.path.isfile(p):
            print("size         : %d bytes" % os.path.getsize(p))
        print("checksum     : %s" % e.checksum)
        print("key          : %d (%s)" % (e.key, "encrypted" if e.is_encrypted else "plaintext"))
        if e.is_encrypted:
            print("fkey         : %s" % uc.build_fkey(e.key).hex())
        deps = e.deps()
        print("dependencies : %d" % len(deps))
        for d in deps:
            print("    %s" % d)
    return 0


def cmd_songs(args) -> int:
    with uc.MetaDb(args.game_path, args.region) as db:
        ids = db.song_ids()
        for sid in ids:
            entries = db.like("cutt/cutt_son%s/%%" % sid)
            missing = sum(0 if os.path.isfile(e.path(db.game_path)) else 1 for e in entries)
            print("son%-8s %2d bundles%s" % (sid, len(entries), "  (%d missing)" % missing if missing else ""))
        print("-- %d songs" % len(ids), file=sys.stderr)
    return 0


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)

    p = sub.add_parser("plain", help="write a decrypted copy of the meta DB")
    p.add_argument("-o", "--output", default=None, help="output path (default: tools/out/meta_plain.db)")
    uc.add_common_args(p)
    p.set_defaults(func=cmd_plain)

    p = sub.add_parser("find", help="search asset paths with a LIKE pattern")
    p.add_argument("pattern")
    p.add_argument("--limit", type=int, default=0)
    uc.add_common_args(p)
    p.set_defaults(func=cmd_find)

    p = sub.add_parser("info", help="show one asset's meta row and disk location")
    p.add_argument("name")
    uc.add_common_args(p)
    p.set_defaults(func=cmd_info)

    p = sub.add_parser("songs", help="list live song ids that have cutt bundles")
    uc.add_common_args(p)
    p.set_defaults(func=cmd_songs)

    args = ap.parse_args(argv)
    return args.func(args)


if __name__ == "__main__":
    raise SystemExit(main())
