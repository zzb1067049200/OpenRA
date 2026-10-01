#!/usr/bin/env python3
"""Verify the Selectable/Interactable bounds migration in mods/yr."""
import io, os, re

ROOT = r"D:\Workbuddy custom workplace\2026-09-27-11-13-30\openra-study"
RULES = os.path.join(ROOT, "mods", "yr", "rules")
TILE_W, TILE_H, SCALE = 60, 30, 1448

bad = []
px = []
for fn in sorted(os.listdir(RULES)):
    if not fn.endswith(".yaml"):
        continue
    lines = io.open(os.path.join(RULES, fn), encoding="utf-8").read().split("\n")
    for i, l in enumerate(lines):
        m = re.match(r"^\s+(Decoration)?Bounds:\s*(.+?)\s*$", l)
        if not m:
            continue
        nums = [int(p.strip()) for p in m.group(2).split(",") if p.strip()]
        if len(nums) < 2:
            continue
        w = nums[0] * TILE_W / SCALE
        h = nums[1] * TILE_H / SCALE
        px.append((w, h, nums[0], nums[1], fn, i + 1))
        if w < 4 or h < 4:
            bad.append((w, h, fn, i + 1, m.group(2)))

print(f"hitbox entries: {len(px)}")
print(f"smaller than 4px on an axis: {len(bad)}")
for b in bad[:15]:
    print("   ", b)
px.sort()
print("\nsmallest intended pixel hitboxes:")
for w, h, X, Y, fn, ln in px[:6]:
    print(f"   {w:6.1f} x {h:6.1f} px   (WDist {X},{Y})  {fn}:{ln}")
print("\nlargest intended pixel hitboxes:")
for w, h, X, Y, fn, ln in px[-6:]:
    print(f"   {w:6.1f} x {h:6.1f} px   (WDist {X},{Y})  {fn}:{ln}")
