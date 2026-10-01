#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
prep_yr_preupdate.py — 在跑引擎 --update-mod 之前必须完成的两项清理。

为什么必须「提前」做：
  1. AlwaysVisible 在 release-20250330 被删除（"now is the default behavior"）。
     但它会导致 --update-mod 里的 RemoveBuildingInfoAllowPlacementOnResources 规则
     先于 RemoveAlwaysVisible 加载 DefaultRules 而抛 "Cannot locate type:
     AlwaysVisibleInfo"。DefaultRules 是 Lazy，异常会被缓存，后面所有依赖它的规则
     一起失败。所以提前删干净，等价于 RemoveAlwaysVisible 规则的效果。
  2. sequences/tech-structures.yaml 里的 `-DepthSprite:` 是 2020 时代希望
     "本序列不要继承父级的 DepthSprite"。2026 引擎的 DepthSprite 来自
     `Defaults:` 节点、由序列加载器合并，不参与 mini yaml 的节点解析，
     所以 `-DepthSprite:` 会抛
     "There are no elements with key `DepthSprite` to remove"。
     现代等价写法是给该序列显式写一个空的 `DepthSprite:`：
     DefaultSpriteSequence 用 string.IsNullOrEmpty(depthSprite) 判断是否启用，
     空值即等于关闭，语义完全一致。
"""
import io
import os
import re
import sys

ROOT = os.path.dirname(os.path.abspath(__file__))
MODS = os.path.join(ROOT, "openra-study", "mods", "yr")
SKIP_DIRS = {".preupdate", "obj", "bin"}

RX_ALWAYS = re.compile(r"^(\t+)-?AlwaysVisible(@[A-Za-z0-9_]+)?:[ \t]*$")
RX_DEPTH = re.compile(r"^(\t+)-DepthSprite:[ \t]*$")


def level_of(line):
    """缩进层级：tab = 1 级，4 个空格 = 1 级。"""
    lv = 0
    spaces = 0
    for ch in line:
        if ch == "\t":
            lv += 1
        elif ch == " ":
            spaces += 1
            if spaces >= 4:
                spaces = 0
                lv += 1
        else:
            break
    return lv


def walk_yaml():
    for dirpath, dirnames, filenames in os.walk(MODS):
        dirnames[:] = [d for d in dirnames if d not in SKIP_DIRS]
        for fn in filenames:
            if fn.lower().endswith((".yaml", ".yml")):
                yield os.path.join(dirpath, fn)


def main():
    n_always = 0
    n_depth = 0
    files = 0

    for p in walk_yaml():
        with io.open(p, "r", encoding="utf-8-sig", newline="") as f:
            lines = f.read().split("\n")

        out = []
        i = 0
        hit_a = 0
        hit_d = 0
        while i < len(lines):
            m = RX_ALWAYS.match(lines[i])
            if m:
                indent = level_of(lines[i])
                i += 1
                while i < len(lines) and lines[i].strip() and level_of(lines[i]) > indent:
                    i += 1
                hit_a += 1
                continue

            d = RX_DEPTH.match(lines[i])
            if d:
                out.append(d.group(1) + "DepthSprite:")
                hit_d += 1
                i += 1
                continue

            out.append(lines[i])
            i += 1

        if hit_a or hit_d:
            with io.open(p, "w", encoding="utf-8", newline="") as f:
                f.write("\n".join(out))
            files += 1
            n_always += hit_a
            n_depth += hit_d
            print("  %-56s 删除 AlwaysVisible %d，DepthSprite 改写 %d"
                  % (os.path.relpath(p, ROOT), hit_a, hit_d))

    print("\n共 %d 个文件：删除 AlwaysVisible %d 处，-DepthSprite -> 空 DepthSprite %d 处"
          % (files, n_always, n_depth))
    return 0


if __name__ == "__main__":
    sys.exit(main())
