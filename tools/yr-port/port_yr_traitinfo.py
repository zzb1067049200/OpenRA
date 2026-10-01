#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
port_yr_traitinfo.py

2026 引擎的 ActorInfo.cs:84 使用:
    creator.CreateObject<TraitInfo>(traitName + "Info")
即所有 yaml trait 的 Info 类必须是 OpenRA.Traits.TraitInfo 的子类。
2020 时代的 OpenRA 允许 Info 只实现标记接口 ITraitInfoInterface（并自带 Create 方法），
在本引擎中会抛 InvalidCastException。

本脚本把 YR 中残留的 `: ITraitInfoInterface` 统一改为 `: TraitInfo`，
并把 Create 方法标记为 override。
"""
import io
import os
import re
import sys

ROOT = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(ROOT, "openra-study", "OpenRA.Mods.YR")

TARGETS = [
    "Common/Immobile.cs",
    "Traits/BaseSpawnerSlave.cs",
    "Traits/BunkerCargo.cs",
    "Traits/EnvironmentPaletteEffect.cs",
    "Traits/GrantConditionOnAttackType.cs",
    "Traits/GrantExternalConditionWeapon.cs",
    "Traits/GrantTimedConditionOnDeploy.cs",
    "Traits/ShootableBallisticMissile.cs",
]

# 变体 A: " : ITraitInfoInterface," -> " : TraitInfo,"
RE_IFACE_LIST = re.compile(r":\s*ITraitInfoInterface\s*,")
# 变体 B: " : ITraitInfoInterface" (行尾)
RE_IFACE_ONLY = re.compile(r":\s*ITraitInfoInterface(?=\s*$)", re.M)
# Create 方法签名
RE_CREATE_PLAIN = re.compile(r"\bpublic\s+object\s+Create\(\s*ActorInitializer\b")
RE_CREATE_VIRTUAL = re.compile(r"\bpublic\s+virtual\s+object\s+Create\(\s*ActorInitializer\b")


def convert(path):
    with io.open(path, "r", encoding="utf-8-sig") as f:
        text = f.read()

    orig = text
    counts = {}

    text, n = RE_IFACE_LIST.subn(": TraitInfo,", text)
    counts["iface_list"] = n

    text, n = RE_IFACE_ONLY.subn(": TraitInfo", text)
    counts["iface_only"] = n

    text, n = RE_CREATE_VIRTUAL.subn("public override object Create(ActorInitializer", text)
    counts["create_virtual"] = n

    text, n = RE_CREATE_PLAIN.subn("public override object Create(ActorInitializer", text)
    counts["create_plain"] = n

    if text != orig:
        with io.open(path, "w", encoding="utf-8", newline="") as f:
            f.write(text)

    return counts


def main():
    total = {}
    for rel in TARGETS:
        p = os.path.join(SRC, rel)
        if not os.path.exists(p):
            print("!! 缺失: %s" % rel)
            continue
        c = convert(p)
        total[rel] = c
        print("%-52s iface_list=%d iface_only=%d create_virtual=%d create_plain=%d"
              % (rel, c["iface_list"], c["iface_only"], c["create_virtual"], c["create_plain"]))

    agg = sum(sum(c.values()) for c in total.values())
    print("\n合计修改 %d 处，涉及 %d 个文件" % (agg, len(total)))


if __name__ == "__main__":
    main()
