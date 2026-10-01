#!/usr/bin/env python3
"""YR 序列数据修补（必须在 9 步流水线 + --update-mod 之后运行）。

背景
----
`--update-mod` 的 ExplicitSequenceFilenames 规则负责把「序列节点自身值 = 精灵文件名」
的旧式简写转成显式 `Filename:`。但该规则在本引擎里**永远自我禁用**：
`ExplicitSequenceFilenames.BeforeUpdate` 用反射去取 `Manifest` 的私有字段 `yaml`，
而 2026 引擎的 `Manifest` 已没有这个字段（`yaml` 只是构造函数里的局部变量）→
`GetField` 返回 null → 读不到 mod.yaml 的 TilesetExtensions/TilesetCodes →
`BeforeUpdateSequences` 判定 `tilesetExtensions.Count == 0` → `disabled = true`。

结果：mods/yr/sequences/*.yaml 里的 5888 个简写节点一个都没迁移。
运行时由 `ExtendedTilesetSpecificSpriteSequence.ParseFilenames` 复现旧语义兜底
（见 OpenRA.Mods.YR/RA2/Graphic/ExtendedTilesetSpecificSpriteSequenceLoader.cs），
因此本脚本只负责清理**其它**在 2026 引擎下非法的数据。

本脚本做两件事
--------------
1. 小写 `length:` -> `Length:`。MiniYaml 的键匹配是 ordinal 大小写敏感的
   （`MiniYaml.NodeWithKeyOrDefault`: `node.Key != key`），所以 `length: 1` 会被
   静默忽略，等价于 `Length: *`（拉取从 Start 到精灵末尾的全部帧）。共 5 处。
2. 修掉 `Facings: 6`。2026 引擎的 DefaultSpriteSequence 构造函数硬性要求
   Facings 为 2 的幂（`Exts.IsPowerOf2`），否则抛 YamlException，整个 tileset
   的序列集解析失败。boris 的 `prone` 与 inherits 来的 `prone-stand`
   （Frames: 86, 92, 98, 104, 110, 116, 122, 128, Facings: 8）形态一致，
   故写成 Start: 86 / Length: 1 / Stride: 6 / Facings: 8，逐帧与之一一对应。
"""
import io
import os
import re
import sys

SEQ_DIR = os.path.join("mods", "yr", "sequences")

# boris.prone：原始块（soviet-infantry.yaml）
BORIS_PRONE_OLD = "\t\tStart: 86\n\t\tlength: 1\n\t\tFacings: 6\n"
BORIS_PRONE_NEW = "\t\tStart: 86\n\t\tLength: 1\n\t\tStride: 6\n\t\tFacings: 8\n"


def main():
    if not os.path.isdir(SEQ_DIR):
        sys.exit(f"找不到 {SEQ_DIR}（请在仓库根目录运行）")

    changed_lower = 0
    changed_lower_files = []
    boris_fixed = False

    for name in sorted(os.listdir(SEQ_DIR)):
        if not name.endswith(".yaml"):
            continue
        path = os.path.join(SEQ_DIR, name)
        with io.open(path, encoding="utf-8", newline="") as f:
            text = f.read()

        original = text

        # 2) boris.prone 的 Facings: 6（先做，避免被 1) 的小写替换打乱匹配）
        if BORIS_PRONE_OLD in text:
            text = text.replace(BORIS_PRONE_OLD, BORIS_PRONE_NEW, 1)
            boris_fixed = True

        # 1) 其余小写 length:
        text, n = re.subn(r"^(\t+)length:", r"\1Length:", text, flags=re.MULTILINE)
        changed_lower += n
        if n:
            changed_lower_files.append(f"{name}({n})")

        if text != original:
            with io.open(path, "w", encoding="utf-8", newline="") as f:
                f.write(text)
            print(f"  patched {name}")

    print(f"\n小写 length: -> Length:  {changed_lower} 处  {changed_lower_files}")
    print(f"boris.prone Facings 修复:  {'OK' if boris_fixed else '未匹配（可能已修过）'}")

    # 复核：不应再有非 2 的幂的 Facings
    bad = []
    for name in sorted(os.listdir(SEQ_DIR)):
        if not name.endswith(".yaml"):
            continue
        for i, line in enumerate(io.open(os.path.join(SEQ_DIR, name), encoding="utf-8").read().split("\n")):
            m = re.match(r"^\t+Facings:\s*(-?\d+)\s*$", line)
            if m:
                v = abs(int(m.group(1)))
                if v == 0 or v > 1024 or (v & (v - 1)) != 0:
                    bad.append(f"{name}:{i + 1} Facings: {m.group(1)}")
    print("剩余非法 Facings:", bad if bad else "(无)")

    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
