#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
port_yr_yaml_fixes.py

YR 从 2020 时代的 OpenRA 分叉出来，它的 yaml 里有一批"引擎早已改名/删除、
但 UpdateRules 表最早只到 release-20230225"的内容，以及一批 2026 引擎新增的
必填字段。这些都在这里一次性处理掉，保证「从 git 干净状态 → 迁移」是可重放的。

各条对应的问题
--------------
1. rules/defaults.yaml  : ^Aircraft 带 ChronoshiftableWithSpriteEffect，但引擎要求
                          Chronoshiftable 必须有 Mobile/Husk（飞机没有）→ 删掉该节点。
2. rules/defaults.yaml  : 4 个 Tooltip 只有 GenericName，而 TooltipInfoBase.Name 是
                          [FieldLoader.Require] → 由 port_yr_yaml_2026.py 处理。
3. rules/allied-structures.yaml : ChronoshiftPowerInfo 新增 [Require] Dimensions/Footprint
                          （传送目标区脚印），YR 没写 → 按"3x3 传送阵列"补上。
4. rules/soviet-structures.yaml : 同上，GrantExternalConditionPower（铁幕）3x3；
                          另外该文件尾部有个孤立的 WithNukeLaunchAnimation: 节点，
                          引擎已删除该 trait → 去掉。
5. rules/allied-infantry.yaml / yuri-infantry.yaml : jumpjet / lunr 主动 -Mobile，
                          引擎要求有 Chronoshiftable 就必须有 Mobile/Husk → 一并去掉。
6. rules/allied-vehicles.yaml : PortOffsets 的值尾部跟了一个 /*...*/ 行内块注释，
                          mini yaml 不认，值被整段当成字符串 → 改写成 # 行注释。
7. rules/world.yaml : EditorSelectionLayer 已被引擎删除（现代是 MarkerLayerOverlay）
                          → 删掉节点。
"""
import io
import os
import sys

ROOT = os.path.dirname(os.path.abspath(__file__))
MODS = os.path.join(ROOT, "openra-study", "mods", "yr")


def read(rel):
    with io.open(os.path.join(MODS, rel.replace("/", os.sep)), "r",
                 encoding="utf-8-sig", newline="") as f:
        return f.read()


def write(rel, text):
    with io.open(os.path.join(MODS, rel.replace("/", os.sep)), "w",
                 encoding="utf-8", newline="") as f:
        f.write(text)


def replace_once(rel, old, new, note):
    text = read(rel)
    if text.count(old) != 1:
        print("  !! %-32s 锚点匹配 %d 次: %s" % (rel, text.count(old), note))
        return False
    write(rel, text.replace(old, new, 1))
    print("  ok %-32s %s" % (rel, note))
    return True


def main():
    print("== YR yaml 收尾修正 ==")

    # 1. ^Aircraft 去掉 ChronoshiftableWithSpriteEffect
    replace_once(
        "rules/defaults.yaml",
        "\tWithTextControlGroupDecoration:\n"
        "\tChronoshiftableWithSpriteEffect:\n"
        "\t\tImage: chrono\n"
        "\t\tWarpInSequence: warpin\n"
        "\t\tWarpOutSequence: warpout\n"
        "\t\tChronoshiftSound: schrmov.wav\n"
        "\t\tReturnToOrigin: false\n"
        "\t\tRequiresCondition: !airborne\n",
        "\tWithTextControlGroupDecoration:\n",
        "^Aircraft 移除 ChronoshiftableWithSpriteEffect",
    )

    # 3. ChronoshiftPower 补 Dimensions/Footprint
    replace_once(
        "rules/allied-structures.yaml",
        "\tChronoshiftPower@chronoshift:\n\t\tOrderName: Chronoshift\n",
        "\tChronoshiftPower@chronoshift:\n"
        "\t\tDimensions: 3,3\n"
        "\t\tFootprint: xxx xxx xxx\n"
        "\t\tOrderName: Chronoshift\n",
        "ChronoshiftPower 补 3x3 脚印",
    )

    # 4a. 铁幕补 Dimensions/Footprint
    replace_once(
        "rules/soviet-structures.yaml",
        "\tGrantExternalConditionPower@IRONCURTAIN:\n\t\tIcon: invuln\n",
        "\tGrantExternalConditionPower@IRONCURTAIN:\n"
        "\t\tDimensions: 3,3\n"
        "\t\tFootprint: xxx xxx xxx\n"
        "\t\tIcon: invuln\n",
        "IronCurtain 补 3x3 脚印",
    )

    # 4b. 去掉孤立的 WithNukeLaunchAnimation 节点
    replace_once(
        "rules/soviet-structures.yaml",
        "\t\tSelectTargetSpeechNotification: SelectTarget\n\tWithNukeLaunchAnimation:\n",
        "\t\tSelectTargetSpeechNotification: SelectTarget\n",
        "移除孤立 WithNukeLaunchAnimation 节点",
    )

    # 5. jumpjet / lunr 一并移除 Chronoshiftable
    replace_once(
        "rules/allied-infantry.yaml",
        "\t-TimedConditionBar@ChronoDisable:\n"
        "\t-ExternalCondition@CHRONODISABLE:\n"
        "\t-Mobile:\n",
        "\t-TimedConditionBar@ChronoDisable:\n"
        "\t-ExternalCondition@CHRONODISABLE:\n"
        "\t-Mobile:\n"
        "\t# 2026 引擎要求 Chronoshiftable 必须有 Mobile/Husk；jumpjet 是飞行单位。\n"
        "\t-ChronoshiftableWithSpriteEffect:\n",
        "jumpjet 移除 ChronoshiftableWithSpriteEffect",
    )
    replace_once(
        "rules/yuri-infantry.yaml",
        "\t-TimedConditionBar@ChronoDisable:\n"
        "\t-ExternalCondition@CHRONODISABLE:\n"
        "\t-Mobile:\n",
        "\t-TimedConditionBar@ChronoDisable:\n"
        "\t-ExternalCondition@CHRONODISABLE:\n"
        "\t-Mobile:\n"
        "\t# 2026 引擎要求 Chronoshiftable 必须有 Mobile/Husk；lunr 是漂浮单位。\n"
        "\t-ChronoshiftableWithSpriteEffect:\n",
        "lunr 移除 ChronoshiftableWithSpriteEffect",
    )

    # 6. PortOffsets 行内 /* */ 注释
    replace_once(
        "rules/allied-vehicles.yaml",
        "\t\tPortOffsets: 384,0,128, 224,-341,128, -224,-341,128, -384,0,128, -224,341,128, 224,341,128"
        "/*TODO: Need to confirm all five attack port*/\n",
        "\t\t# TODO: Need to confirm all five attack port\n"
        "\t\tPortOffsets: 384,0,128, 224,-341,128, -224,-341,128, -384,0,128, -224,341,128, 224,341,128\n",
        "PortOffsets 行内块注释改为 # 注释",
    )

    # 7. EditorSelectionLayer 已删除（锚点不带前置行，避免受前一步插入 ResourceTypes 影响）
    replace_once(
        "rules/world.yaml",
        "\tEditorSelectionLayer:\n\t\tPalette: pips\n",
        "",
        "移除 EditorSelectionLayer",
    )

    print("\n完成。")
    return 0


if __name__ == "__main__":
    sys.exit(main())
