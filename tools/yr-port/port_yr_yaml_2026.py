#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
port_yr_yaml_2026.py — 把 YR yaml 里 2020 时代、且引擎 UpdateRules 已不再覆盖的
构造，手工映射到 2026 引擎的写法。

背景：引擎的 --update-mod 最早只支持 release-20230225 起跳，YR yaml 早于它，
所以 2020→2023 之间的改名没人管，必须手工处理。

处理清单
--------
A. 纯改名（引擎里是同一个 trait，只是换了名字）
   CreateMPPlayers  -> CreateMapPlayers        (Traits/World/CreateMapPlayers.cs)
   MPStartUnits     -> StartingUnits           (Traits/World/MapStartingUnits.cs)
   MPStartLocations -> MapStartingLocations    (Traits/World/MapStartingLocations.cs)
   SpawnMPUnits     -> SpawnStartingUnits      (Traits/World/SpawnStartingUnits.cs)

B. 资源体系重构（旧：顶层 ResourceType@X；新：拆到 ResourceLayer + ResourceRenderer）
   旧的 ValuePerUnit 现在挂在 Player 的 PlayerResources.ResourceValues 上。
   ResourceLayer.ResourceTypeInfo   : ResourceIndex / TerrainType / AllowedTerrainTypes / MaxDensity
   ResourceRenderer.ResourceTypeInfo: Sequences / Palette / Name / Image

C. 引擎已彻底删除、且无等价物的 trait，直接删节点
   DomainIndex            (引擎中无 IDomainIndex / DomainIndex 任何痕迹)
   PlayerHighlightPalette (仅编辑器高亮，装饰性)
   VeteranProductionIconOverlay (现代做法是 WithProductionIconOverlay + ProductionIconOverlayManager，另行跟进)

D. TooltipInfoBase.Name 是 [FieldLoader.Require]，YR 里有 4 个只写了 GenericName 的
   Tooltip，补上 Name。
"""
import io
import os
import re
import sys

ROOT = os.path.dirname(os.path.abspath(__file__))
MODS = os.path.join(ROOT, "openra-study", "mods", "yr")

RENAMES = [
    ("CreateMPPlayers", "CreateMapPlayers"),
    ("MPStartUnits", "StartingUnits"),
    ("MPStartLocations", "MapStartingLocations"),
    ("SpawnMPUnits", "SpawnStartingUnits"),
]

# 只匹配"节点名"，即 缩进 + 可选的 - 前缀 + 名字 + 可选的 @后缀 + 冒号 + 行尾
RENAME_RES = [
    (re.compile(r"^([ \t]*-?)" + old + r"(@[A-Za-z0-9_]+)?:(?=\s*$)", re.M), r"\1" + new + r"\2:")
    for old, new in RENAMES
]

ORE_SEQ = "tib01, tib02, tib03, tib04, tib05, tib06, tib07, tib08, tib09, tib10, " \
          "tib11, tib12, tib13, tib14, tib15, tib16, tib17, tib18, tib19, tib20"
GEM_SEQ = "gem01, gem02, gem03, gem04, gem05, gem06, gem07, gem08, gem09, gem10, gem11, gem12"


def read(p):
    with io.open(p, "r", encoding="utf-8-sig", newline="") as f:
        return f.read()


def write(p, text):
    with io.open(p, "w", encoding="utf-8", newline="") as f:
        f.write(text)


def report(rel, note):
    print("  %-46s %s" % (rel, note))


# ---------------------------------------------------------------- A. 纯改名
def do_renames():
    print("== A. trait 改名 ==")
    for dirpath, dirnames, filenames in os.walk(MODS):
        dirnames[:] = [d for d in dirnames if d not in {".preupdate", "obj", "bin"}]
        for fn in filenames:
            if not fn.lower().endswith(".yaml"):
                continue
            p = os.path.join(dirpath, fn)
            text = orig = read(p)
            n = 0
            for rx, rep in RENAME_RES:
                text, c = rx.subn(rep, text)
                n += c
            if text != orig:
                write(p, text)
                report(os.path.relpath(p, ROOT), "改名 %d 处" % n)


# ------------------------------------------------- B. 资源体系重构
def do_resources():
    print("== B. 资源体系重构 (world.yaml) ==")
    p = os.path.join(MODS, "rules", "world.yaml")
    text = read(p)

    start = text.find("\tResourceType@Ore:\n")
    # 注意：原始文件里这一行的缩进是 8 个空格而不是两个 tab，所以只按内容匹配。
    end_marker = "RenderTypes: Ore, Gems\n"
    end = text.find(end_marker)
    if start < 0 or end < 0:
        print("  !! 未找到资源块, 跳过")
        return
    end += len(end_marker)

    new_block = (
        "\tResourceRenderer:\n"
        "\t\tResourceTypes:\n"
        "\t\t\tOre:\n"
        "\t\t\t\tSequences: " + ORE_SEQ + "\n"
        "\t\t\t\tPalette: resource\n"
        "\t\t\t\tName: Valuable Minerals\n"
        "\t\t\tGems:\n"
        "\t\t\t\tSequences: " + GEM_SEQ + "\n"
        "\t\t\t\tPalette: resource\n"
        "\t\t\t\tName: Valuable Minerals\n"
        "\tResourceLayer:\n"
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

    dropped = text[start:end].count("\n")
    text = text[:start] + new_block + text[end:]
    write(p, text)
    report("mods/yr/rules/world.yaml",
           "ResourceType@Ore/Gems + ResourceRenderer -> ResourceRenderer/ResourceLayer (%d 行重写)" % dropped)


# ------------------------------------------------------- C. 删除无等价物节点
def drop_node(text, first_line, extra_children=0, path=""):
    """删除以 first_line 开头(含行首缩进)的节点; extra_children 为需要一并删除的子行数。"""
    idx = text.find(first_line)
    if idx < 0:
        return text, False
    # 回退到行首
    line_start = text.rfind("\n", 0, idx) + 1
    end = line_start
    for _ in range(1 + extra_children):
        nl = text.find("\n", end)
        if nl < 0:
            end = len(text)
            break
        end = nl + 1
    return text[:line_start] + text[end:], True


def do_drops():
    print("== C. 删除引擎已移除的 trait 节点 ==")
    targets = [
        ("rules/world.yaml", "\tDomainIndex:\n", 0),
        ("rules/palettes.yaml", "\tPlayerHighlightPalette:\n", 0),
        ("rules/player.yaml", "\tVeteranProductionIconOverlay:\n", 3),
    ]
    for rel, first, children in targets:
        p = os.path.join(MODS, rel.replace("/", os.sep))
        text = read(p)
        text, ok = drop_node(text, first, children)
        if ok:
            write(p, text)
            report("mods/yr/" + rel, "删除 %s" % first.strip().rstrip(":"))
        else:
            print("  !! %s 中未找到 %s" % (rel, first.strip()))


# ------------------------------------------- D. 补 Tooltip 必填 Name + 资源价值
def do_tooltip_and_values():
    print("== D. 补 Tooltip.Name 与 PlayerResources.ResourceValues ==")

    p = os.path.join(MODS, "rules", "defaults.yaml")
    text = orig = read(p)
    rx = re.compile(r"^(\t+Tooltip:)\n(\t+)GenericName: ([^\n]+)\n", re.M)

    def repl(m):
        indent = m.group(2)
        value = m.group(3).strip()
        return "%s\n%sName: %s\n%sGenericName: %s\n" % (m.group(1), indent, value, indent, value)

    text, n = rx.subn(repl, text)
    if text != orig:
        write(p, text)
        report("mods/yr/rules/defaults.yaml", "补 Name 的 Tooltip %d 处" % n)
    else:
        print("  !! defaults.yaml 未发现缺 Name 的 Tooltip")

    # PlayerResources.ResourceValues 挂到 ^BasePlayer
    #   ^BasePlayer:
    #       AlwaysVisible:
    #       Shroud:
    p = os.path.join(MODS, "rules", "player.yaml")
    text = orig = read(p)
    old = "^BasePlayer:\n\tAlwaysVisible:\n\tShroud:\n"
    new = ("^BasePlayer:\n"
           "\tAlwaysVisible:\n"
           "\tShroud:\n"
           "\tPlayerResources:\n"
           "\t\tResourceValues:\n"
           "\t\t\tOre: 25\n"
           "\t\t\tGems: 50\n")
    if old in text and "\tResourceValues:" not in text:
        text = text.replace(old, new, 1)
        write(p, text)
        report("mods/yr/rules/player.yaml", "在 ^BasePlayer 增加 PlayerResources.ResourceValues")
    else:
        print("  !! ^BasePlayer 结构不符或已有 ResourceValues, 跳过")


def main():
    if not os.path.isdir(MODS):
        print("找不到 mods/yr: %s" % MODS)
        return 1
    do_renames()
    do_resources()
    do_drops()
    do_tooltip_and_values()
    print("\n完成。")
    return 0


if __name__ == "__main__":
    sys.exit(main())
