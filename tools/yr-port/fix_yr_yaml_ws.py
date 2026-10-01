#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
fix_yr_yaml_ws.py

引擎的 --update-mod 走的是 MiniYaml.FromLines(discardCommentsAndWhitespace: false)，
这会保留行内容并按缩进严格解析。此时"只含空白字符的行"会被当成一个
缩进不一致的节点，直接抛 "Bad indent in miniyaml"。

check-yaml 走的是丢弃空白的解析路径，所以这类问题在 check-yaml 里看不出来。

本脚本把 mods/yr 下所有 yaml 中"仅由空白字符组成的行"清空为真正的空行。
"""
import io
import os

ROOT = os.path.dirname(os.path.abspath(__file__))
MODS = os.path.join(ROOT, "openra-study", "mods", "yr")
SKIP_DIRS = {".preupdate", "obj", "bin"}


def main():
    total_files = 0
    total_lines = 0
    for dirpath, dirnames, filenames in os.walk(MODS):
        dirnames[:] = [d for d in dirnames if d not in SKIP_DIRS]
        for fn in filenames:
            if not fn.lower().endswith((".yaml", ".yml")):
                continue
            p = os.path.join(dirpath, fn)
            with io.open(p, "r", encoding="utf-8-sig", newline="") as f:
                lines = f.readlines()

            changed = 0
            out = []
            for ln in lines:
                body = ln.rstrip("\r\n")
                eol = ln[len(body):]
                if body != "" and body.strip() == "":
                    out.append(eol)  # 变成真正的空行，保留原来的换行风格
                    changed += 1
                else:
                    out.append(ln)

            if changed:
                with io.open(p, "w", encoding="utf-8", newline="") as f:
                    f.writelines(out)
                rel = os.path.relpath(p, ROOT)
                print("  %-60s 清理 %d 行" % (rel, changed))
                total_files += 1
                total_lines += changed

    print("\n共 %d 个文件, %d 行空白行被清空" % (total_files, total_lines))


if __name__ == "__main__":
    main()
