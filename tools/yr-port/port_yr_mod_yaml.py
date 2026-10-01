#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Phase 10: migrate mods/yr/mod.yaml from the 2020 schema to the 2026 engine schema.

The 2026 engine (Manifest.cs / ModData.cs) is far stricter than the 2020 one YR was
written against:
  * `FileSystem:` is mandatory (a bare `Packages:` block is no longer read) and every
    entry must use an explicit `^SupportDir|` / `^EngineDir|` root instead of the
    legacy `^Content/` token.
  * Every non-reserved top-level key must resolve to an IGlobalModData type, so
    legacy-only sections (Translations, ModelSequenceFormat, the old ModContent layout)
    now abort the mod load with "`X` is not a valid mod manifest entry."
  * `Assemblies:` must be a plain comma-separated list of file names; the `pkg|name`
    form resolves to a literal `pkg|name` path and breaks both the desktop loader and
    Android's LoadCompiledInAssembly name lookup.
  * `GameSpeeds` gained a required `DefaultSpeed` and a nested `Speeds:` node.
  * UI text moved from Translations/*.yaml to Fluent bundles.
"""
import io, os, sys

ROOT = r"D:/Workbuddy custom workplace/2026-09-27-11-13-30/openra-study"
PATH = os.path.join(ROOT, "mods/yr/mod.yaml")

with io.open(PATH, encoding="utf-8-sig") as f:
    s = f.read()

def sub(src, old, new, count=1):
    n = src.count(old)
    if n != count:
        print("!! expected %d, found %d:\n%s" % (count, n, old[:300]))
        sys.exit(1)
    return src.replace(old, new)

# ------------------------------------------------------------------
# 1. Metadata: give it a real window title
# ------------------------------------------------------------------
s = sub(s, """	WebIcon32: https://raw.githubusercontent.com/cookgreen/yr/master/mods/yr/icon.png
    
ModCredits:""", """	WebIcon32: https://raw.githubusercontent.com/cookgreen/yr/master/mods/yr/icon.png
	WindowTitle: Red Alert 2 Yuri's Revenge

ModCredits:""")

# ------------------------------------------------------------------
# 2. ModContent: drop the whole trailing section (see header comment).
# ------------------------------------------------------------------
mc = s.find("\nModContent:")
if mc < 0:
    print("!! ModContent section not found")
    sys.exit(1)
s = s[:mc].rstrip("\n") + "\n"

# ------------------------------------------------------------------
# 3. GameSpeeds: legacy flat layout -> DefaultSpeed + Speeds:
# ------------------------------------------------------------------
gs = s.find("\nGameSpeeds:\n")
if gs < 0:
    print("!! GameSpeeds section not found")
    sys.exit(1)
body_start = gs + len("\nGameSpeeds:\n")
body = s[body_start:]
if "DefaultSpeed" in body:
    print("!! GameSpeeds already migrated")
    sys.exit(1)
new_body = "	DefaultSpeed: default\n	Speeds:\n"
for ln in body.rstrip("\n").split("\n"):
    new_body += ("\t" + ln if ln.strip() else ln) + "\n"
s = s[:body_start] + new_body

# ------------------------------------------------------------------
# 4. Packages: -> FileSystem: DefaultFileSystem / Packages:
#    Keeps the exact original mount order and `~` optional markers so asset priority
#    is unchanged (`yr|bits*` stays last so the mod overrides content assets).
# ------------------------------------------------------------------
s = sub(s, """Packages:
	~^Content/ra2
	~^Content/yr
	.
	$yr: yr
	./mods/common: common
""", """# The 2026 engine requires an explicit FileSystem section; a bare `Packages:` block is
# no longer read (see OpenRA.Game/Manifest.cs). DefaultFileSystem keeps the original mount
# semantics, including the `~` optional marker and mount order / asset priority.
FileSystem: DefaultFileSystem
	Packages:
		^EngineDir
		$yr: yr
		^EngineDir|mods/common: common
		~^SupportDir|Content/ra2
		~^SupportDir|Content/yr
""")

# Re-indent the remainder of the legacy Packages body (one extra tab) and rewrite the
# legacy `^Content/` root token.
lines = s.split("\n")
start = next(i for i, ln in enumerate(lines) if ln.startswith("\t\t~^SupportDir|Content/yr")) + 1
out, end = [], None
for i in range(start, len(lines)):
    ln = lines[i]
    if ln.startswith("\t") and ln.strip():
        out.append("\t\t" + ln[1:].replace("^Content/", "^SupportDir|Content/"))
        continue
    end = i
    break
if end is None:
    print("!! could not find Packages body end")
    sys.exit(1)
lines[start:end] = out
s = "\n".join(lines)

# ------------------------------------------------------------------
# 5. MapFolders: legacy ^maps token
# ------------------------------------------------------------------
s = sub(s, "	~^maps/yr/release-20200503: User", "	~^SupportDir|maps/yr/release-20200503: User")

# ------------------------------------------------------------------
# 6. Assemblies: `pkg|file` -> plain file list
# ------------------------------------------------------------------
s = sub(s, """Assemblies:
	common|OpenRA.Mods.Common.dll
	common|OpenRA.Mods.Cnc.dll
	yr|OpenRA.Mods.YR.dll""",
        "Assemblies: OpenRA.Mods.Common.dll, OpenRA.Mods.Cnc.dll, OpenRA.Mods.YR.dll")

# ------------------------------------------------------------------
# 7. FluentMessages (replaces the removed Translations mechanism)
# ------------------------------------------------------------------
s = sub(s, """Cursors:
	yr|cursors.yaml

Chrome:""", """Cursors:
	yr|cursors.yaml

# The 2026 engine replaced the legacy Translations/languages/*.yaml mechanism with Fluent
# bundles. YR's chrome already carries inline English text, so only the shared engine
# bundles are pulled in; YR strings fall back to being their own key.
FluentMessages:
	common|fluent/common.ftl
	common|fluent/chrome.ftl
	common|fluent/hotkeys.ftl
	common|fluent/ingame-debug.ftl
	common|fluent/rules.ftl

Chrome:""")

# ------------------------------------------------------------------
# 8. Sections the 2026 engine expects but YR never had
# ------------------------------------------------------------------
s = sub(s, "SpriteSequenceFormat: ExtendedTilesetSpecificSpriteSequence",
        """DefaultOrderGenerator: UnitOrderGenerator

VideoFormats: Vqa, Wsa

TerrainFormat: DefaultTerrain

AssetBrowser:
	SpriteExtensions: .shp, .tmp, .tem, .sno, .urb, .ubn, .des, .lun
	AudioExtensions: .aud, .wav, .bag
	VideoExtensions: .vqa, .wsa

SpriteSequenceFormat: ExtendedTilesetSpecificSpriteSequence""")

# ------------------------------------------------------------------
# 9. Stale key that would abort the load
# ------------------------------------------------------------------
s = sub(s, "ModelSequenceFormat: VoxelModelSequence\n\n", "")

# ------------------------------------------------------------------
# 10. Translations: the legacy languages/*.yaml mechanism is gone in 2026,
#     and the bare key itself now aborts the load
#     ("`Translations` is not a valid mod manifest entry").
# ------------------------------------------------------------------
_lines = s.split("\n")
_out = []
_skip = False
for _ln in _lines:
    if _ln == "Translations:":
        _skip = True
        continue
    if _skip:
        if _ln[:1] in ("\t", " "):
            continue
        _skip = False
    _out.append(_ln)

if len(_out) == len(_lines):
    print("!! Translations section not found")
    sys.exit(1)
s = "\n".join(_out)

# newline="" 保证写回 LF。用默认 newline 时 Windows 会把 \n 翻成 \r\n，
# 后续按 "\n" 做锚点匹配的脚本就会全部失配。
with io.open(PATH, "w", encoding="utf-8", newline="") as f:
    f.write(s)

print("OK  mods/yr/mod.yaml")
