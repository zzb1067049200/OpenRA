#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""probe_yr_anchors.py — 打印 YR yaml 待修改区域的原始文本，用于校正迁移脚本的锚点。"""
import io
import os
import sys

ROOT = os.path.dirname(os.path.abspath(__file__))
MODS = os.path.join(ROOT, "openra-study", "mods", "yr")


def show(rel, patterns, ctx=3):
    p = os.path.join(MODS, rel.replace("/", os.sep))
    text = io.open(p, "r", encoding="utf-8-sig", newline="").read()
    lines = text.split("\n")
    print("=" * 70)
    print("###", rel)
    for pat in patterns:
        hits = [i for i, l in enumerate(lines) if pat in l]
        print("--- pattern %r -> %d hit(s)" % (pat, len(hits)))
        for i in hits[:4]:
            a, b = max(0, i - 1), min(len(lines), i + ctx + 1)
            for j in range(a, b):
                print("  %4d %r" % (j + 1, lines[j]))
            print("  ......")


def main():
    show("rules/world.yaml",
         ["ResourceType@Ore", "RenderTypes", "EditorResourceLayer", "BuildingInfluence", "^BaseWorld:",
          "DomainIndex", "CreateMPPlayers"])
    show("rules/player.yaml",
         ["^BasePlayer:", "VeteranProductionIconOverlay", "PlayerResources:", "ConditionManager"])
    show("rules/defaults.yaml",
         ["Tooltip:", "^Aircraft:", "ChronoshiftableWithSpriteEffect", "GenericName: Civilian"])
    show("rules/allied-structures.yaml", ["ChronoshiftPower@"])
    show("rules/soviet-structures.yaml", ["GrantExternalConditionPower@", "WithNukeLaunchAnimation"])
    show("rules/allied-infantry.yaml", ["-TimedConditionBar@ChronoDisable"])
    show("rules/allied-vehicles.yaml", ["PortOffsets"])
    show("rules/palettes.yaml", ["PlayerHighlightPalette"])
    return 0


if __name__ == "__main__":
    sys.exit(main())
