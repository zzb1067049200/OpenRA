#!/usr/bin/env python3
import io, os, re

ROOT = r"D:\Workbuddy custom workplace\2026-09-27-11-13-30\openra-study"
LOG = r"D:\Workbuddy custom workplace\2026-09-27-11-13-30\yr_checkyaml11.log"
RULES = os.path.join(ROOT, "mods", "yr", "rules")

flagged = set()
for line in io.open(LOG, encoding="utf-8", errors="replace"):
    m = re.search(r"Error: ([A-Za-z0-9_\.]+)\.SelectableInfo\.Bounds", line)
    if m:
        flagged.add(m.group(1))

rows = []
for fn in sorted(os.listdir(RULES)):
    if not fn.endswith(".yaml"):
        continue
    lines = io.open(os.path.join(RULES, fn), encoding="utf-8").read().split("\n")
    actor = None
    for i, l in enumerate(lines):
        am = re.match(r"^([A-Za-z0-9_]+):", l)
        if am:
            actor = am.group(1)
        bm = re.match(r"^\s+(Decoration)?Bounds:\s*(.+?)\s*$", l)
        if bm and actor:
            nums = [int(p.strip()) for p in bm.group(2).split(",") if p.strip()]
            if len(nums) >= 2:
                rows.append((nums[0], nums[1], nums[2] if len(nums) > 2 else 0,
                             nums[3] if len(nums) > 3 else 0, actor, fn, i + 1))

print("rows:", len(rows), "flagged:", len(flagged))

print("\n--- rows sorted by (X,Y) with flag status ---")
for X, Y, OX, OY, a, fn, ln in sorted(rows, key=lambda r: (r[0], r[1])):
    print(f"X={X:4} Y={Y:4} off={OX:4},{OY:4}  {'FLAG' if a in flagged else 'ok  '}  {a:16} {fn}:{ln}")


def test(fx, fy):
    bad = []
    for X, Y, OX, OY, a, fn, ln in rows:
        pred = int(X * fx) <= 0 or int(Y * fy) <= 0
        act = a in flagged
        if pred != act:
            bad.append((X, Y, a, pred, act))
    return bad


print("\n--- candidate factors ---")
for name, fx, fy in [
    ("tileSize(60,30)/tileScale(1448)", 60 / 1448, 30 / 1448),
    ("tileSize(60,30)/tileScale(1024)", 60 / 1024, 30 / 1024),
    ("tileSize(24,24)/tileScale(1024)", 24 / 1024, 24 / 1024),
    ("tileSize(60,30)/tileScale(2048)", 60 / 2048, 30 / 2048),
    ("tileSize(1,1)/tileScale(1024)", 1 / 1024, 1 / 1024),
]:
    b = test(fx, fy)
    print(f"{name}: mismatches={len(b)}")
    for x in b[:8]:
        print("     ", x)
