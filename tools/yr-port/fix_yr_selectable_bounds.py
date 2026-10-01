#!/usr/bin/env python3
"""Migrate legacy pixel-era `Bounds:` / `DecorationBounds:` values in mods/yr to the
engine's current WDist convention.

Background
----------
OpenRA's `InteractableInfo.Bounds` used to be `int2[]` expressed in *screen pixels*.
It is now `ImmutableArray<WDist>`, converted to pixels at runtime as:

    px = value * tileSize.Axis / tileScale

so the intent-preserving migration is:

    WDist = oldPixels * tileScale / tileSize.Axis

OpenRA's own mods were migrated this way (e.g. RA soldier `18,20` -> `768,853`
with tileSize 24 and tileScale 1024). The YR port never ran the migrator, so its
values are still raw pixels and end up ~1-3 px wide -> units are effectively
unclickable.

For the YR mod: tileSize = 60,30 (mod.yaml `Terrain: TileSize: 60,30`) and
tileScale = 1448 (MapGrid, RectangularIsometric).
"""

import io
import os
import re
import sys

TILE_W, TILE_H = 60, 30
TILE_SCALE = 1448

FX = TILE_SCALE / TILE_W   # 24.1333...
FY = TILE_SCALE / TILE_H   # 48.2666...

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
YR_RULES = os.path.join(ROOT, "mods", "yr", "rules")

# Only `Selectable:` / `Interactable:` children are hitboxes. In particular
# `map.yaml` has a TOP-LEVEL `Bounds:` (map size in cells) that must NOT be touched.
ALLOWED_PARENTS = {"Selectable", "Interactable"}

LINE_RE = re.compile(r"^(\s+)(Decoration)?Bounds:(\s*)(.*?)(\s*)$")


def scale(v, factor):
    # round half away from zero
    n = v * factor
    return int(n + 0.5) if n >= 0 else -int(-n + 0.5)


def convert(text):
    parts = [p.strip() for p in text.split(",")]
    if len(parts) not in (2, 4):
        return None, "unexpected-arity"
    try:
        nums = [int(p) for p in parts]
    except ValueError:
        return None, "not-int"
    # Idempotency guard: the largest legacy pixel width in this mod is 220, while
    # every migrated value is >= 242 after scaling. Skip already-migrated lines.
    if nums[0] >= 242:
        return None, "already-migrated"
    factors = [FX, FY, FX, FY]
    out = [scale(n, f) for n, f in zip(nums, factors)]
    return out, "ok"


def parent_key(lines, i, indent):
    for j in range(i - 1, -1, -1):
        pm = re.match(r"^(\s*)([A-Za-z0-9_@^\-\.]+):", lines[j])
        if pm and len(pm.group(1)) < indent:
            return pm.group(2)
    return None


def main():
    changed_files = 0
    changed_lines = 0
    skipped = []
    for root, _dirs, files in os.walk(YR_RULES):
        for fn in sorted(files):
            if not fn.endswith(".yaml"):
                continue
            path = os.path.join(root, fn)
            with io.open(path, "r", encoding="utf-8", newline="") as f:
                lines = f.read().split("\n")
            touched = False
            for i, line in enumerate(lines):
                m = LINE_RE.match(line)
                if not m:
                    continue
                indent, deco, sp1, value, sp2 = m.groups()
                if parent_key(lines, i, len(indent)) not in ALLOWED_PARENTS:
                    continue
                value = value.rstrip()
                if not value:
                    continue
                out, status = convert(value)
                if out is None:
                    skipped.append((path, i + 1, value, status))
                    continue
                new_value = ", ".join(str(x) for x in out)
                lines[i] = f"{indent}{deco or ''}Bounds:{sp1}{new_value}"
                touched = True
                changed_lines += 1
            if touched:
                with io.open(path, "w", encoding="utf-8", newline="") as f:
                    f.write("\n".join(lines))
                changed_files += 1
                print(f"patched {os.path.relpath(path, ROOT)}")
    print(f"\nfiles={changed_files} lines={changed_lines}")
    for s in skipped:
        print("SKIPPED", s)
    return 0


if __name__ == "__main__":
    sys.exit(main())
