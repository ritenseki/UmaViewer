"""Shared library for the UmaViewer Python asset tools.

This is a Python re-implementation of the decryption logic that lives in the
C# side of this repository:

  * ``Assets/Scripts/Config.cs``                      -- the key constants
  * ``Assets/Scripts/UmaAssetBundleStream.cs``        -- asset bundle XOR cipher
  * ``Assets/Scripts/UmaDatabase/UmaDatabaseEntry.cs``-- per-bundle key expansion
  * ``Assets/Scripts/UmaDatabase/UmaDatabaseController.cs`` -- meta DB cipher

Nothing here writes to the game folder; every file is opened read-only.

----------------------------------------------------------------------------
Setup
----------------------------------------------------------------------------
    python3 -m venv ~/.venvs/umatools
    ~/.venvs/umatools/bin/python -m pip install -r tools/requirements.txt

Then run any tool with that interpreter, e.g.

    ~/.venvs/umatools/bin/python tools/dump_cutt_typetree.py 1157

----------------------------------------------------------------------------
Game folder
----------------------------------------------------------------------------
The tools look for the game's ``Persistent`` folder (the one containing
``meta`` and ``dat/``) in this order:

  1. ``--game-path`` command line option
  2. ``$UMA_GAME_PATH`` environment variable
  3. the paths in :data:`DEFAULT_GAME_PATHS`

----------------------------------------------------------------------------
How the encryption works
----------------------------------------------------------------------------
meta database
    A SQLite database encrypted with SQLite3 Multiple Ciphers, cipher index 3
    (= ChaCha20 / "sqleet" scheme, non-legacy defaults).  The passphrase is
    ``DBKey[i] XOR DBBaseKey[i % 13]`` (32 raw bytes, see :func:`meta_key`).
    We hand those raw bytes to sqlite3mc through ``PRAGMA hexkey`` which is
    the exact equivalent of the C# ``sqlite3_key(db, bytes, 32)`` call.

asset bundles
    ``dat/<first 2 chars of hash>/<hash>``.  The first 256 bytes are stored in
    the clear; every byte at file offset ``p >= 256`` is XORed with
    ``FKey[p % 88]`` where ``FKey`` is built from the 11-byte ``ABKey`` and the
    per-bundle 64-bit key stored in the meta table (column ``e``):

        FKey[i * 8 + j] = ABKey[i] XOR little_endian_int64(key)[j]

    A key of 0 means the bundle is not encrypted.
"""

from __future__ import annotations

import os
import shutil
import struct
import sys
import tempfile
from dataclasses import dataclass
from typing import Iterable, Iterator, Optional

# --------------------------------------------------------------------------
# dependency check (friendly message instead of a bare ImportError)
# --------------------------------------------------------------------------

_MISSING: list[str] = []

try:
    import apsw  # provided by the `apsw-sqlite3mc` wheel
except ImportError:  # pragma: no cover
    apsw = None
    _MISSING.append("apsw-sqlite3mc")

try:
    import UnityPy
except ImportError:  # pragma: no cover
    UnityPy = None
    _MISSING.append("UnityPy")


def require(*modules: str) -> None:
    """Abort with an actionable message if a needed dependency is absent."""
    needed = [m for m in modules if m in _MISSING]
    if not needed:
        return
    sys.stderr.write(
        "\nMissing Python package(s): %s\n\n"
        "Install them into a virtualenv, e.g.\n"
        "    python3 -m venv ~/.venvs/umatools\n"
        "    ~/.venvs/umatools/bin/python -m pip install -r %s\n"
        "and re-run this script with ~/.venvs/umatools/bin/python\n\n"
        % (", ".join(needed), os.path.join(os.path.dirname(__file__), "requirements.txt"))
    )
    raise SystemExit(2)


def has_sqlite3mc() -> bool:
    """True when the installed apsw build actually carries the MC ciphers."""
    if apsw is None:
        return False
    try:
        con = apsw.Connection(":memory:")
        try:
            list(con.execute("PRAGMA cipher"))
            return True
        finally:
            con.close()
    except Exception:
        return False


# --------------------------------------------------------------------------
# keys -- verbatim from Assets/Scripts/Config.cs
# --------------------------------------------------------------------------

DB_BASE_KEY = bytes(
    [0xF1, 0x70, 0xCE, 0xA4, 0xDF, 0xCE, 0xA3, 0xE1,
     0xA5, 0xD8, 0xC7, 0x0B, 0xD1, 0x00, 0x00, 0x00]
)

DB_KEY = bytes(
    [0x6D, 0x5B, 0x65, 0x33, 0x63, 0x36,
     0x63, 0x25, 0x54, 0x71, 0x2D, 0x73,
     0x50, 0x53, 0x63, 0x38, 0x6D, 0x34,
     0x37, 0x7B, 0x35, 0x63, 0x70, 0x23,
     0x37, 0x34, 0x53, 0x29, 0x73, 0x43,
     0x36, 0x33]
)

GLOBAL_DB_KEY = bytes(
    [0x56, 0x63, 0x6B, 0x63, 0x42, 0x72, 0x37, 0x76, 0x65, 0x70, 0x41, 0x62]
)

AB_KEY = bytes([0x53, 0x2B, 0x46, 0x31, 0xE4, 0xA7, 0xB9, 0x47, 0x3E, 0x7C, 0xFB])

HEADER_SIZE = 256
FKEY_LEN = len(AB_KEY) * 8  # 88

#: sqlite3mc cipher index used by UmaDatabaseController.ReadMetaFromEncryptedDb
META_CIPHER = "chacha20"  # == sqlite3mc_config(db, "cipher", 3)

DEFAULT_GAME_PATHS = (
    "/mnt/d/Umamusume/umamusume_Data/Persistent",
    os.path.expandvars(r"%USERPROFILE%\AppData\LocalLow\Cygames\umamusume"),
)


def meta_key(region: str = "jp") -> bytes:
    """Final meta-DB passphrase: ``DBKey[i] XOR DBBaseKey[i % 13]``.

    Mirrors ``UmaDatabaseController.GenFinalKey``.
    """
    key = GLOBAL_DB_KEY if region.lower() in ("global", "en") else DB_KEY
    return bytes(key[i] ^ DB_BASE_KEY[i % 13] for i in range(len(key)))


# --------------------------------------------------------------------------
# asset bundle cipher
# --------------------------------------------------------------------------

def build_fkey(key: int) -> bytes:
    """Expand a per-bundle 64-bit key into the 88-byte XOR keystream.

    Mirrors ``UmaDatabaseEntry.FKey``.
    """
    key_bytes = struct.pack("<q", key)  # signed, little endian
    out = bytearray(FKEY_LEN)
    for i, b in enumerate(AB_KEY):
        base = i << 3
        for j in range(8):
            out[base + j] = b ^ key_bytes[j]
    return bytes(out)


def decrypt_bundle_bytes(data: bytes, key: int) -> bytes:
    """Decrypt a whole asset bundle image held in memory."""
    if not key or len(data) <= HEADER_SIZE:
        return bytes(data)
    fk = build_fkey(key)
    body = data[HEADER_SIZE:]
    n = len(body)
    # byte at file offset p uses fk[p % 88]; body[0] is at offset HEADER_SIZE
    off = HEADER_SIZE % FKEY_LEN
    rotated = fk[off:] + fk[:off]
    stream = (rotated * (n // FKEY_LEN + 1))[:n]
    plain = (int.from_bytes(body, "big") ^ int.from_bytes(stream, "big")).to_bytes(n, "big")
    return bytes(data[:HEADER_SIZE]) + plain


def decrypt_bundle_file(path: str, key: int) -> bytes:
    with open(path, "rb") as fh:
        return decrypt_bundle_bytes(fh.read(), key)


# --------------------------------------------------------------------------
# meta database
# --------------------------------------------------------------------------

@dataclass
class MetaEntry:
    """One row of the meta ``a`` table (see UmaDatabaseEntry.cs)."""

    type: str          # column m, e.g. "_3d_cutt"
    name: str          # column n, the asset path, e.g. cutt/cutt_son1157/type_01
    url: str           # column h, the hashed filename under dat/
    checksum: str      # column c
    prerequisites: str # column d, ';' separated dependency asset paths
    key: int           # column e, per-bundle cipher key (0 = plaintext bundle)

    @property
    def is_encrypted(self) -> bool:
        return self.key != 0

    def path(self, game_path: str) -> str:
        return os.path.join(game_path, "dat", self.url[:2], self.url)

    def deps(self) -> list[str]:
        return [d for d in (self.prerequisites or "").split(";") if d]


def find_game_path(explicit: Optional[str] = None) -> str:
    candidates = []
    if explicit:
        candidates.append(explicit)
    env = os.environ.get("UMA_GAME_PATH")
    if env:
        candidates.append(env)
    candidates.extend(DEFAULT_GAME_PATHS)
    for c in candidates:
        if c and os.path.isfile(os.path.join(c, "meta")):
            return c
    raise SystemExit(
        "Could not locate the game's Persistent folder (needs a 'meta' file).\n"
        "Tried: %s\nPass --game-path or set $UMA_GAME_PATH." % ", ".join(filter(None, candidates))
    )


class MetaDb:
    """Read-only access to the encrypted ``meta`` SQLite database.

    The game keeps ``meta`` open, so we work on a temporary copy by default.
    """

    def __init__(self, game_path: Optional[str] = None, region: str = "jp", copy: bool = True,
                 readonly: bool = True):
        require("apsw-sqlite3mc")
        self.game_path = find_game_path(game_path)
        self.meta_path = os.path.join(self.game_path, "meta")
        self._tmp = None
        src = self.meta_path
        if copy:
            fd, tmp = tempfile.mkstemp(prefix="uma_meta_", suffix=".db")
            os.close(fd)
            shutil.copyfile(self.meta_path, tmp)
            self._tmp = tmp
            src = tmp
        elif not readonly:
            raise ValueError("refusing to open the game's meta file writable; use copy=True")
        flags = apsw.SQLITE_OPEN_READONLY if readonly else (
            apsw.SQLITE_OPEN_READWRITE | apsw.SQLITE_OPEN_CREATE
        )
        self.con = apsw.Connection(src, flags=flags)
        key = meta_key(region)
        try:
            list(self.con.execute("PRAGMA cipher='%s'" % META_CIPHER))
            list(self.con.execute("PRAGMA hexkey='%s'" % key.hex()))
            list(self.con.execute("SELECT name FROM sqlite_master LIMIT 1"))
        except Exception as exc:
            self.close()
            raise SystemExit(
                "Failed to decrypt the meta database (%s).\n"
                "Either the keys in Assets/Scripts/Config.cs are out of date, or the\n"
                "installed apsw build has no SQLite3MultipleCiphers support "
                "(need the 'apsw-sqlite3mc' wheel, not plain 'apsw')." % exc
            )

    # -- lifecycle ---------------------------------------------------------
    def close(self) -> None:
        try:
            self.con.close()
        except Exception:
            pass
        if self._tmp and os.path.exists(self._tmp):
            try:
                os.remove(self._tmp)
            except OSError:
                pass
            self._tmp = None

    def __enter__(self) -> "MetaDb":
        return self

    def __exit__(self, *exc) -> None:
        self.close()

    # -- queries -----------------------------------------------------------
    _COLS = "m,n,h,c,d,e"

    def _rows(self, where: str, args: Iterable = ()) -> Iterator[MetaEntry]:
        sql = "SELECT %s FROM a %s" % (self._COLS, where)
        for m, n, h, c, d, e in self.con.execute(sql, tuple(args)):
            yield MetaEntry(m or "", n or "", h or "", c or "", d or "", e or 0)

    def get(self, name: str) -> Optional[MetaEntry]:
        for e in self._rows("WHERE n = ?", (name,)):
            return e
        return None

    def like(self, pattern: str) -> list[MetaEntry]:
        return list(self._rows("WHERE n LIKE ? ORDER BY n", (pattern,)))

    def all(self) -> list[MetaEntry]:
        return list(self._rows("ORDER BY n"))

    def song_ids(self) -> list[str]:
        """All live song ids that have a ``cutt/cutt_son<id>/`` bundle group."""
        import re

        ids = set()
        for (n,) in self.con.execute("SELECT n FROM a WHERE n LIKE 'cutt/cutt_son%'"):
            m = re.match(r"cutt/cutt_son(\d+)/", n or "")
            if m:
                ids.add(m.group(1))
        return sorted(ids, key=int)

    # -- bundle loading ----------------------------------------------------
    def bundle_bytes(self, name_or_entry) -> bytes:
        entry = name_or_entry if isinstance(name_or_entry, MetaEntry) else self.get(name_or_entry)
        if entry is None:
            raise KeyError("no meta entry named %r" % name_or_entry)
        p = entry.path(self.game_path)
        if not os.path.isfile(p):
            raise FileNotFoundError(
                "%s -> %s (not downloaded; open it once in the game or in UmaViewer)" % (entry.name, p)
            )
        return decrypt_bundle_file(p, entry.key)

    def load(self, name_or_entry, with_deps: bool = False):
        """Decrypt and open a bundle with UnityPy; returns an Environment."""
        require("UnityPy")
        import io

        entry = name_or_entry if isinstance(name_or_entry, MetaEntry) else self.get(name_or_entry)
        if entry is None:
            raise KeyError("no meta entry named %r" % name_or_entry)
        env = UnityPy.Environment()
        if with_deps:
            for dep in entry.deps():
                try:
                    env.load_file(io.BytesIO(self.bundle_bytes(dep)), name=dep, is_dependency=True)
                except Exception as exc:
                    sys.stderr.write("  warning: dependency %s not loaded (%s)\n" % (dep, exc))
        env.load_file(io.BytesIO(self.bundle_bytes(entry)), name=entry.name)
        return env


# --------------------------------------------------------------------------
# helpers shared by the cutt tools
# --------------------------------------------------------------------------

#: A MonoBehaviour is a LiveTimelineWorkSheet when its TypeTree has this field.
WORKSHEET_MARKER = "cameraPosKeys"


def is_worksheet(obj) -> bool:
    node = getattr(getattr(obj, "serialized_type", None), "node", None)
    if node is None:
        return False
    return any(ch.m_Name == WORKSHEET_MARKER for ch in node.m_Children)


def worksheets(env):
    """Yield ``(name, obj, typetree_root_node)`` for each WorkSheet in *env*."""
    for obj in env.objects:
        if obj.type.name != "MonoBehaviour" or not is_worksheet(obj):
            continue
        node = obj.serialized_type.node
        try:
            name = obj.read_typetree().get("m_Name") or "?"
        except Exception:
            name = "?"
        yield name, obj, node


def count_keyframes(value) -> int:
    """Total number of timeline keyframes reachable from *value*.

    Every timeline key list is serialized as ``{_attribute, _playMode,
    thisList}``; we sum the ``thisList`` lengths and never descend into the
    keyframes themselves.
    """
    if isinstance(value, dict):
        if "thisList" in value:
            tl = value["thisList"]
            return len(tl) if isinstance(tl, list) else 0
        return sum(count_keyframes(v) for v in value.values() if isinstance(v, (dict, list)))
    if isinstance(value, list):
        return sum(count_keyframes(v) for v in value if isinstance(v, (dict, list)))
    return 0


def count_groups(value) -> int:
    """Size of the top-level container of a track field.

    * key list (``thisList``)      -> number of keyframes
    * list of named track groups   -> number of groups
    * struct of several key lists  -> number of sub key lists
    """
    if isinstance(value, list):
        return len(value)
    if isinstance(value, dict):
        if "thisList" in value:
            tl = value["thisList"]
            return len(tl) if isinstance(tl, list) else 0
        return sum(1 for v in value.values() if isinstance(v, (dict, list)))
    return 0


def group_names(value) -> list[str]:
    """Names of the track groups inside a list-style field, if any."""
    if isinstance(value, list):
        return [v.get("name", "") for v in value if isinstance(v, dict) and "name" in v]
    return []


def add_common_args(parser) -> None:
    parser.add_argument(
        "--game-path",
        default=None,
        help="game Persistent folder (default: $UMA_GAME_PATH or %s)" % DEFAULT_GAME_PATHS[0],
    )
    parser.add_argument(
        "--region", default="jp", choices=("jp", "global"), help="which DB key to use (default: jp)"
    )
