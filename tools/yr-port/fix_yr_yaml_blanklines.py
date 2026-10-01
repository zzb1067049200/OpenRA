#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
fix_yr_yaml_blanklines.py

背景（引擎源码 OpenRA.Game/MiniYaml.cs 的 FromLines）：

    if (!key.IsEmpty || !discardCommentsAndWhitespace)
    {
        if (parsedLines.Count > 0 && parsedLines[^1].Level < level - 1)
            throw new YamlException($"Bad indent in miniyaml at {location}");
        ...
    }

--update-mod 走的是 discardCommentsAndWhitespace = false 的解析路径，此时
空行也会作为 level 0 的节点进入 parsedLines。于是

    上一行是空行(level 0)  +  下一行 level >= 2
    => 0 < level-1  =>  抛 "Bad indent in miniyaml"

而 check-yaml / 运行时走的是丢弃空白的路径，空行被跳过，所以这个问题只在
--update-mod 时暴露。引擎自带 mods/ra 里此类模式数量为 0，说明这是 YR yaml
本身需要修掉的。

修法：删除"后面紧跟 level>=2 行"的空行（这类空行本身不携带任何信息，
删掉即可让相邻的非空行重新满足 mini yaml 的缩进不变式）。
"""
import io
import os

ROOT = os.path.dirname(os.path.abspath(__file__))
MODS = os.path.join(ROOT, "openra-study", "mods", "yr")
SKIP_DIRS = {".preupdate", "obj", "bin"}


def level_of(line):
    """按 mini yaml 规则计算缩进层级：tab = 1 级，4 个空格 = 1 级。"""
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


def is_blank(line):
    return line.strip() == ""


def process(text):
    lines = text.splitlines(keepends=True)
    out = []          # 逆序累积，out[0] 即"原文件中的下一行"
    dropped = 0
    for line in reversed(lines):
        if is_blank(line) and out and level_of(out[0]) >= 2:
            dropped += 1
            continue
        out.append(line)
    out.reverse()
    return "".join(out), dropped


def main():
    total_files = 0
    total_dropped = 0
    for dirpath, dirnames, filenames in os.walk(MODS):
        dirnames[:] = [d for d in dirnames if d not in SKIP_DIRS]
        for fn in filenames:
            if not fn.lower().endswith((".yaml", ".yml")):
                continue
            p = os.path.join(dirpath, fn)
            with io.open(p, "r", encoding="utf-8-sig", newline="") as f:
                text = f.read()

            new_text, dropped = process(text)
            if dropped:
                with io.open(p, "w", encoding="utf-8", newline="") as f:
                    f.write(new_text)
                print("  %-58s 删除 %d 行空行" % (os.path.relpath(p, ROOT), dropped))
                total_files += 1
                total_dropped += dropped

    print("\n共 %d 个文件, 删除 %d 行空行" % (total_files, total_dropped))


if __name__ == "__main__":
    main()
