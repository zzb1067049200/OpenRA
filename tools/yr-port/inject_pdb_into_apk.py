#!/usr/bin/env python3
"""Inject PDBs into an unsigned Android APK so device stack traces carry line numbers.

Why this exists
---------------
The .NET Android SDK resolves PDBs into its internal ``_ResolvedSymbols`` item, but nothing
in the packaging pipeline consumes it -- that item feeds NativeAOT and the debugger, not the
APK. So a ``-c Debug`` build with ``AndroidIncludeDebugSymbols=true`` still ships with zero
symbols, and every on-device crash reports a bare method name with no line. Rather than keep
fighting SDK internals, this explodes the APK, drops the PDBs into ``assemblies/`` (where the
.NET Android runtime looks for them) and repacks.

Must run BEFORE signing: repacking invalidates any signature already applied.

Why the work happens here and not in the workflow
--------------------------------------------------
Doing the find/copy/filter in shell needs a ``find | while read`` pipeline, and under
``set -o pipefail`` a non-matching glob or an early ``exit`` inside the loop kills the step
even when every PDB was copied correctly. Python has no such trap.

Usage:
    inject_pdb_into_apk.py --apk app.apk --search bin obj
    inject_pdb_into_apk.py --apk app.apk --search bin --keep     # keep the temp dir
"""
import argparse
import os
import shutil
import sys
import tempfile
import zipfile

# Only managed assemblies are useful for a managed stack trace. Native .so symbols come
# from the NDK build and would not resolve against OpenRA.Game.dll.
INCLUDE_PREFIXES = ("OpenRA.",)
INCLUDE_EXACT = ("libnet-android.debug.so",)


def collect_pdbs(search_dirs):
    """Return {basename: path} for every PDB under the given roots, first match wins.

    bin/ and obj/ hold overlapping copies; the same assembly can appear in several publish
    intermediates, so dedupe by filename and keep the first one seen.
    """
    found = {}
    for root in search_dirs:
        if not os.path.isdir(root):
            continue
        for dirpath, _, filenames in os.walk(root):
            for name in filenames:
                if not name.lower().endswith(".pdb"):
                    continue
                if not (name.startswith(INCLUDE_PREFIXES) or name in INCLUDE_EXACT):
                    continue
                found.setdefault(name, os.path.join(dirpath, name))
    return found


def repack(exploded, apk):
    entries = []
    for dirpath, _, filenames in os.walk(exploded):
        for name in filenames:
            full = os.path.join(dirpath, name)
            arc = os.path.relpath(full, exploded).replace(os.sep, "/")
            entries.append((full, arc))

    if not entries:
        sys.exit("nothing to repack")

    if not any(arc == "AndroidManifest.xml" for _, arc in entries):
        sys.exit("AndroidManifest.xml missing; refusing to write an unbuildable APK")

    entries.sort(key=lambda x: x[1])
    with zipfile.ZipFile(apk, "w", zipfile.ZIP_STORED) as z:
        for full, arc in entries:
            z.write(full, arc)
    return len(entries)


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--apk", required=True, help="path to the unsigned APK, modified in place")
    ap.add_argument("--search", nargs="+", default=["bin", "obj"],
                    help="directories to scan for PDBs (default: bin obj)")
    ap.add_argument("--keep", action="store_true", help="keep the exploded directory")
    args = ap.parse_args()

    if not os.path.isfile(args.apk):
        sys.exit(f"APK not found: {args.apk}")

    pdbs = collect_pdbs(args.search)
    print(f"found {len(pdbs)} managed PDB(s)")
    for name in sorted(pdbs):
        print(f"  {name}")
    if not pdbs:
        sys.exit("no matching PDBs; a -c Debug build should have produced some")

    exploded = tempfile.mkdtemp(prefix="apk-explode-")
    try:
        with zipfile.ZipFile(args.apk) as z:
            z.extractall(exploded)

        assemblies = os.path.join(exploded, "assemblies")
        os.makedirs(assemblies, exist_ok=True)
        for name, src in pdbs.items():
            shutil.copyfile(src, os.path.join(assemblies, name))

        count = repack(exploded, args.apk)
        print(f"repacked {args.apk}: {count} entries")

        with zipfile.ZipFile(args.apk) as z:
            in_apk = [n for n in z.namelist() if n.lower().endswith(".pdb")]
        print(f"PDB entries now in the APK: {len(in_apk)}")
        if not in_apk:
            sys.exit("repack reported success but no PDB is in the archive")
    finally:
        if args.keep:
            print(f"exploded directory kept at {exploded}")
        else:
            shutil.rmtree(exploded, ignore_errors=True)

    print("OK")


if __name__ == "__main__":
    main()
