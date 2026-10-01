#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Phase 11: rename traits in mods/yr/**/*.yaml that the 2026 engine renamed or removed.

The engine's own update rules (OpenRA.Mods.Common/UpdateRules/Rules/*) document the renames:
  * 20231010/RenameOnDeath          Explodes       -> FireWarheadsOnDeath
                                    ThrowsShrapnel -> FireProjectilesOnDeath
  * 20230225/RenameMcvCrateAction   GiveMcvCrateAction -> GiveBaseBuilderCrateAction
  * SelfHealing                     -> ChangesHealth (same Step/Delay/StartIfBelow/DamageCooldown fields)
  * ConditionManager                removed entirely: conditions are now owned by ConditionalTrait
                                    / ExternalCondition, so the bare trait entry is dropped.
"""
import io, os, re, sys, glob

ROOT = r"D:/Workbuddy custom workplace/2026-09-27-11-13-30/openra-study/mods/yr"

RENAMES = [
    ("Explodes", "FireWarheadsOnDeath"),
    ("ThrowsShrapnel", "FireProjectilesOnDeath"),
    ("SelfHealing", "ChangesHealth"),
    ("GiveMcvCrateAction", "GiveBaseBuilderCrateAction"),
]

# trait key line: optional leading '-', the name, optional '@suffix', then ':'
def key_re(name):
    return re.compile(r"^(\t+)(-?)" + name + r"(@[A-Za-z0-9_.\-]+)?(\s*:.*)$")

REMOVED = [re.compile(r"^\t+-ConditionManager\s*:\s*$"),
           re.compile(r"^\t+ConditionManager\s*:\s*$")]

changed = {}
for path in glob.glob(os.path.join(ROOT, "**", "*.yaml"), recursive=True):
    # newline="" 很关键：否则 Windows 上写入时会把 \n 翻译成 \r\n，
    # 后续按 "\n" 做锚点匹配的脚本就会全部失配。
    with io.open(path, encoding="utf-8-sig", newline="") as f:
        lines = f.read().split("\n")

    out = []
    hits = 0
    for ln in lines:
        if any(r.match(ln) for r in REMOVED):
            hits += 1
            continue

        new = ln
        for old, newname in RENAMES:
            m = key_re(old).match(new)
            if m:
                new = m.group(1) + m.group(2) + newname + (m.group(3) or "") + m.group(4)
                hits += 1
                break
        out.append(new)

    if hits:
        with io.open(path, "w", encoding="utf-8", newline="") as f:
            f.write("\n".join(out))
        changed[os.path.relpath(path, ROOT).replace("\\", "/")] = hits

for k in sorted(changed):
    print("%4d  %s" % (changed[k], k))
print("total files changed: %d, edits: %d" % (len(changed), sum(changed.values())))
