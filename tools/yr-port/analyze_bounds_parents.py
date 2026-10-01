#!/usr/bin/env python3
import io, os, re
from collections import Counter

YR = r"D:\Workbuddy custom workplace\2026-09-27-11-13-30\openra-study\mods\yr"
parents = Counter()
samples = {}
for root, _d, files in os.walk(YR):
    for fn in sorted(files):
        if not fn.endswith(".yaml"):
            continue
        p = os.path.join(root, fn)
        lines = io.open(p, encoding="utf-8").read().split("\n")
        for i, l in enumerate(lines):
            m = re.match(r"^(\s+)((?:Decoration)?Bounds):\s*(.+?)\s*$", l)
            if not m:
                continue
            ind = len(m.group(1))
            par = None
            for j in range(i - 1, -1, -1):
                pm = re.match(r"^(\s*)([A-Za-z0-9_@^\-\.]+):", lines[j])
                if pm and len(pm.group(1)) < ind:
                    par = pm.group(2)
                    break
            parents[par] += 1
            samples.setdefault(par, f"{os.path.relpath(p, YR)}:{i+1}  {l.strip()}")
print("total:", sum(parents.values()))
for k, v in parents.most_common():
    print(f"{v:5}  {k}      e.g. {samples[k]}")
