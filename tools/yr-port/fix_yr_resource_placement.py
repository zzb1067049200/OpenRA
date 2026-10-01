#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
fix_yr_resource_placement.py

引擎 lint 要求 `ResourceLayer` 只能挂在 `World` 上（"It is a system trait meant
for World."）。而我把资源体系重构时，把它和 `ResourceRenderer` 一起留在了
`^BaseWorld`，于是 `EditorWorld`（Inherits: ^BaseWorld）也被塞了一个 ResourceLayer。

引擎自带 mods/ra 的正确布局是：
    ^BaseWorld : ResourceRenderer                     <- 渲染器放基类
    World      : ResourceLayer (ResourceTypes ...)     <- 层只放 World
    EditorWorld: EditorResourceLayer (ResourceTypes)   <- 编辑器用 Editor 版

             EditorResourceLayerInfo : TraitInfo, IResourceLayerInfo
本身自带 IResourceLayerInfo 实现，所以 EditorWorld 不需要 ResourceLayer。

本脚本把 YR 的 world.yaml 调成同样的布局。
"""
import io
import os
import sys

ROOT = os.path.dirname(os.path.abspath(__file__))
P = os.path.join(ROOT, "openra-study", "mods", "yr", "rules", "world.yaml")

LAYER_BODY = (
    "\t\tRecalculateResourceDensity: true\n"
    "\t\tResourceTypes:\n"
    "\t\t\tOre:\n"
    "\t\t\t\tResourceIndex: 1\n"
    "\t\t\t\tTerrainType: Ore\n"
    "\t\t\t\tAllowedTerrainTypes: Clear, Rough, Road\n"
    "\t\t\t\tMaxDensity: 12\n"
    "\t\t\tGems:\n"
    "\t\t\t\tResourceIndex: 2\n"
    "\t\t\t\tTerrainType: Gems\n"
    "\t\t\t\tAllowedTerrainTypes: Clear, Rough, Road\n"
    "\t\t\t\tMaxDensity: 12\n"
)

BASE_WORLD_LAYER = "\tResourceLayer:\n" + LAYER_BODY


def main():
    with io.open(P, "r", encoding="utf-8-sig", newline="") as f:
        text = f.read()

    if text.count(BASE_WORLD_LAYER) != 1:
        print("!! ^BaseWorld 中的 ResourceLayer 块匹配 %d 次, 预期 1 次" % text.count(BASE_WORLD_LAYER))
        return 1

    # 1) 从 ^BaseWorld 摘掉 ResourceLayer
    text = text.replace(BASE_WORLD_LAYER, "", 1)

    # 2) 挂到 World: 上（紧跟 BuildingInfluence）
    anchor = "\tBuildingInfluence:\n"
    if text.count(anchor) != 1:
        print("!! BuildingInfluence 锚点匹配 %d 次" % text.count(anchor))
        return 1
    text = text.replace(anchor, anchor + BASE_WORLD_LAYER, 1)

    # 3) EditorWorld 的 EditorResourceLayer 补上资源类型定义
    ed_anchor = "\tEditorResourceLayer:\n"
    if text.count(ed_anchor) != 1:
        print("!! EditorResourceLayer 锚点匹配 %d 次" % text.count(ed_anchor))
        return 1
    text = text.replace(ed_anchor, "\tEditorResourceLayer:\n" + LAYER_BODY, 1)

    with io.open(P, "w", encoding="utf-8", newline="") as f:
        f.write(text)

    print("已调整 world.yaml:")
    print("  ^BaseWorld      : 移除 ResourceLayer（保留 ResourceRenderer）")
    print("  World           : 新增 ResourceLayer + ResourceTypes")
    print("  EditorWorld     : EditorResourceLayer 补上 ResourceTypes")
    return 0


if __name__ == "__main__":
    sys.exit(main())
