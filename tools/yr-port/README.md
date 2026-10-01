# YR port: deterministic mod-migration pipeline

This directory contains the one-shot scripts used to port the bundled
`mods/yr` (Yuri's Revenge) data from its 2020-era upstream layout to the
2026 OpenRA engine used by this fork.

The migrated result is already committed under `mods/yr/`. These scripts exist
so the migration is **reproducible** and **reviewable**: if `mods/yr` is ever
reset (`git checkout -- mods/yr`), re-running the pipeline below regenerates the
exact same tree.

## Why a pipeline is needed

The engine ships a migrator (`--update-mod`) that covers `release-20230225` →
`bleed`. It is **not idempotent** (several rules use `AddNode`), so it must be
run **exactly once** on a clean input. Everything *before* 2023 (the YR data is
2020-era) and the new 2026 **required** fields are not covered by it, hence the
manual passes.

## Order (run from the repo root)

```
# 1. mod.yaml manifest: drop removed entries, keep LF line endings
python tools/yr-port/port_yr_mod_yaml.py

# 2. rules yaml trait renames (Explodes -> FireWarheadsOnDeath, ...)
python tools/yr-port/port_yr_yaml_traits.py

# 3. strip whitespace-only lines (comment-preserving parser chokes on them)
python tools/yr-port/fix_yr_yaml_ws.py

# 4. pre-2023 constructs the engine migrator does not know about
#    (CreateMPPlayers->CreateMapPlayers, resource-system restructure, ...)
python tools/yr-port/port_yr_yaml_2026.py

# 5. ResourceLayer belongs on World, not EditorWorld
python tools/yr-port/fix_yr_resource_placement.py

# 6. targeted required-field fixes (Chronoshift Dimensions/Footprint, ...)
python tools/yr-port/port_yr_yaml_fixes.py

# 7. MUST run before --update-mod:
#    remove AlwaysVisible, rewrite "-DepthSprite:" to empty "DepthSprite:"
python tools/yr-port/prep_yr_preupdate.py

# 8. delete blank lines that precede a level>=2 line ("Bad indent in miniyaml")
python tools/yr-port/fix_yr_yaml_blanklines.py

# 9. the engine migrator, once
dotnet bin/OpenRA.Utility.dll yr --update-mod release-20230225 --apply --yes --skip-maps
```

`port_yr_traitinfo.py` is the C#-side counterpart: it converts legacy
`ITraitInfoInterface` `Info` classes to `OpenRA.Traits.TraitInfo` (the 2026
engine's `ActorInfo` requires the `TraitInfo` base). Run it from the repo root
against the C# sources.

`probe_yr_anchors.py` is a diagnostic used while writing the passes above — it
dumps pristine anchor regions so the exact indentation of the source data could
be confirmed. It is not part of the pipeline.

## Verification

```
dotnet build OpenRA.Mods.YR/OpenRA.Mods.YR.csproj -c Debug   # C# must be 0 errors
cp -f mods/yr/OpenRA.Mods.YR.dll bin/OpenRA.Mods.YR.dll
dotnet bin/OpenRA.Utility.dll yr --check-yaml
```

`--check-yaml` exits non-zero on lint findings, but lint passes are **only** run
by that command (see `OpenRA.Mods.Cnc/UtilityCommands/CheckYaml.cs`); they never
run during normal game startup, so remaining lint output does not prevent the mod
from loading. What matters is that the command gets past ruleset construction —
a `YamlException` (unknown type / bad indent) means a real load failure.
