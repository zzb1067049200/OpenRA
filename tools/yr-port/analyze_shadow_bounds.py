#!/usr/bin/env python
"""Find sequences that will trip the shadow-frame bounds check.

Mimics MiniYaml semantics used by OpenRA:

  * `die4: nukedie` followed by an indented block parses as the sequence node
    `die4` whose scalar value is the image name and whose children are the
    sequence fields. This is the multiline-value form; a naive dict parser
    reads it as a leaf and silently loses every field.
  * `Inherits: ^X` merges the template's children under the inheriting node,
    local keys winning per key (recursive merge for nested nodes).
  * Missing fields fall back to DefaultSpriteSequence's field defaults:
    Start 0, ShadowStart -1.

Then evaluates the same condition ResolveSprites uses:

    index  = CalculateFrameIndices(start, length, ...)          # checked, line 539
    shadow = index.Select(f => allSprites[f - start + shadowStart])   # line 564

Sheet length is not knowable without loading MIX, so this reports sequences
that are *structurally at risk*: Length:* (frame window runs to the end of
whatever sheet is bound) plus a non-default ShadowStart.
"""

import os
import re
import sys
from collections import defaultdict

SEQ_DIR = sys.argv[1] if len(sys.argv) > 1 else "mods/yr/sequences"

# Scalars OpenRA YAML uses for these fields. Anything else means we mis-parsed.
NUMERIC = re.compile(r"^-?\d+$")


class Node(dict):
    """A mapping that can also carry a scalar value (MiniYaml multiline value)."""

    __slots__ = ("value",)

    def __init__(self, value=None):
        super().__init__()
        self.value = value


def parse(path):
    """Parse the OpenRA YAML dialect into nested Node objects."""
    root = Node()
    # stack entries: (indent, node)
    stack = [(-1, root)]

    with open(path, encoding="utf-8") as fh:
        for raw in fh:
            line = raw.rstrip("\n")
            if not line.strip() or line.lstrip().startswith("#"):
                continue

            stripped = line.lstrip(" \t")
            indent = len(line) - len(stripped)
            if stripped.startswith("- "):
                continue  # lists are not used by sequence definitions

            if ":" not in stripped:
                continue

            key, _, val = stripped.partition(":")
            key = key.strip()
            val = val.split("#")[0].strip() if not val.strip().startswith('"') else val.strip()

            while stack and stack[-1][0] >= indent:
                stack.pop()
            parent = stack[-1][1]

            child = Node(val if val else None)
            parent[key] = child
            stack.append((indent, child))

    return root


def merge(base, override):
    """Recursive merge; override wins on collisions. Scalars stay scalars."""
    out = Node(base.value)
    for k, v in base.items():
        out[k] = v
    for k, v in override.items():
        if isinstance(v, Node) and isinstance(out.get(k), Node):
            out[k] = merge(out[k], v)
        else:
            out[k] = v
    return out


def resolve(node, templates, seen=None):
    """Return node with its Inherits chain fully merged in and 'Inherits' dropped."""
    seen = set() if seen is None else seen
    own = Node(node.value)
    for k, v in node.items():
        if k != "Inherits":
            own[k] = v

    inh = node.get("Inherits")
    if inh is None or not isinstance(inh, Node):
        return own
    name = inh.value
    if name is None or name not in templates or name in seen:
        return own
    seen.add(name)
    return merge(resolve(templates[name], templates, seen), own)


def field(node, key, default):
    v = node.get(key)
    if v is None or v.value is None:
        return default
    return v.value


def analyse():
    files = sorted(
        os.path.join(SEQ_DIR, f)
        for f in os.listdir(SEQ_DIR)
        if f.endswith((".yaml", ".yml"))
    )

    templates = {}
    for f in files:
        for key, node in parse(f).items():
            if key.startswith("^"):
                templates[key] = node

    star_total = 0
    at_risk = []
    suspect = []
    per_file = defaultdict(lambda: [0, 0])

    def risk(node):
        """Classify one resolved sequence node.

        Effective length is '*' when Length says so *and* when Length is absent:
        ResolveSprites does `length ??= allSprites.Length - start` either way.
        Shadow window is [shadowStart, shadowStart + length - 1], so any sequence
        whose window runs to the end of the sheet can overrun it once shadowStart
        exceeds Start.
        """
        length = field(node, "Length", None)
        shadow = field(node, "ShadowStart", "-1")
        if shadow == "-1":
            return None
        return "star" if length in (None, "*") else "fixed"

    for f in files:
        for actor, anode in parse(f).items():
            if actor.startswith("^") or not isinstance(anode, Node):
                continue
            # Merge the Inherits chain in first: sequences may arrive purely from a
            # template (e.g. civ1 overrides die4/die6 but inherits die5/die7/die8
            # from ^Civilian). Walking the raw node would miss those entirely.
            merged = resolve(anode, templates)
            for seqname, snode in merged.items():
                if seqname == "Inherits" or not isinstance(snode, Node):
                    continue
                # A merged template child keeps its resolved values; a locally
                # declared one still needs its own chain resolved (nested Inherits).
                resolved = resolve(snode, templates)
                length = field(resolved, "Length", None)
                if length == "*" or length is None:
                    star_total += 1

                kind = risk(resolved)
                if kind is None:
                    continue

                name = os.path.basename(f)
                row = (
                    name,
                    actor,
                    seqname,
                    field(resolved, "Start", "0"),
                    field(resolved, "ShadowStart", "-1"),
                    field(resolved, "Length", "<absent>"),
                    snode.value or "",
                )
                if kind == "star":
                    at_risk.append(row)
                    per_file[name][0] += 1
                else:
                    suspect.append(row)
                    per_file[name][1] += 1

    total = len(at_risk) + len(suspect)
    print(f"sequences with end-of-sheet length (Length '*' or absent) = {star_total}")
    print(f"sequences with a ShadowStart                              = {total}")
    print(f"  AT RISK (shadow window runs to sheet end) = {len(at_risk)}")
    print(f"  fixed-length, needs sheet size to confirm  = {len(suspect)}")
    print()
    if per_file:
        print("--- per file: at_risk / fixed ---")
        for name in sorted(per_file):
            r, c = per_file[name]
            if r or c:
                print(f"  {r:4d} / {c:4d}   {name}")
        print()
    hdr = f"  {'file':26} {'actor':24} {'sequence':18} {'Start':>5} {'Shadow':>7} {'Length':>9}  image"
    if at_risk:
        print("--- AT RISK ---")
        print(hdr)
        for row in at_risk:
            print(f"  {row[0]:26} {row[1]:24} {row[2]:18} {row[3]:>5} {row[4]:>7} {row[5]:>9}  {row[6]}")
        print()
    if suspect:
        print("--- fixed-length with ShadowStart (first 60) ---")
        print(hdr)
        for row in suspect[:60]:
            print(f"  {row[0]:26} {row[1]:24} {row[2]:18} {row[3]:>5} {row[4]:>7} {row[5]:>9}  {row[6]}")
        if len(suspect) > 60:
            print(f"  ... and {len(suspect) - 60} more")
    return 0


if __name__ == "__main__":
    sys.exit(analyse())