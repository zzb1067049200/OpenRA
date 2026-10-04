#!/usr/bin/env python3
"""Inject PDBs into an unsigned Android APK so device stack traces carry line numbers.

Why this exists
---------------
The .NET Android SDK resolves PDBs into its internal ``_ResolvedSymbols`` item, but nothing
in the packaging pipeline consumes it -- that item feeds NativeAOT and the debugger, not the
APK. So a ``-c Debug`` build with ``AndroidIncludeDebugSymbols=true`` still ships with zero
symbols, and every on-device crash reports a bare method name with no line. Rather than keep
fighting SDK internals, this appends the PDBs to the APK under ``assemblies/`` (where the
.NET Android runtime looks for them).

Must run BEFORE signing: repacking invalidates any signature already applied.

Why this copies entries instead of unzipping the archive
--------------------------------------------------------
The first version did ``ZipFile.extractall()`` into a temp dir and rewrote every entry from
disk. That silently dropped ``lib/arm64-v8a/libassembly-store.so`` (26 MB per ABI, the
embedded managed assembly blob), and the app died at startup with::

    F/monodroid: No assemblies (or assembly blobs) were found in the application APK
                file(s) or on the filesystem
    F/monodroid: Abort at monodroid-glue.cc:757 (MonodroidRuntime::create_domain)

A 249 MB APK came out at 198 MB. Nothing in the copy loop reported an error, which is the
worst part: a green CI step produced an APK that could never launch. So entries are now
streamed straight from the source archive, preserving each entry's original compression
method, and ``verify_roundtrip`` asserts the entry set is a strict superset of the original.

Usage:
    inject_pdb_into_apk.py --apk app.apk --search bin obj
    inject_pdb_into_apk.py --apk app.apk --search bin --keep     # keep extracted PDBs
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

# .NET Android refuses to start without this. It carries the embedded managed assemblies
# for every TFM that is not a plain shared framework, so losing it means the process
# aborts in create_domain before any managed code runs. Asserted on after the copy.
REQUIRED_ENTRIES = (
    "AndroidManifest.xml",
    "classes.dex",
    "resources.arsc",
)


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


def copy_entries(src, dst):
    """Stream every entry from the src archive into dst, preserving compression method.

    PDBs are the only additions, so each one is written ZIP_STORED: the Android runtime
    memory-maps the assemblies region and refuses to load a deflated entry.
    """
    try:
        zin = zipfile.ZipFile(src)
    except zipfile.BadZipFile:
        sys.exit(f"not a valid ZIP/APK: {src}")
    with zin:
        names = zin.namelist()
        missing = [n for n in REQUIRED_ENTRIES if n not in names]
        if missing:
            sys.exit(f"source APK is missing {missing}; it is not a complete Android package")

        with zipfile.ZipFile(dst, "w", zipfile.ZIP_DEFLATED) as zout:
            for info in zin.infolist():
                if info.is_dir():
                    continue
                out = zipfile.ZipInfo(info.filename, date_time=info.date_time)
                out.compress_type = info.compress_type
                out.external_attr = info.external_attr
                out.internal_attr = info.internal_attr
                out.create_system = info.create_system
                with zin.open(info) as fin, zout.open(out, "w") as fout:
                    shutil.copyfileobj(fin, fout, length=1024 * 1024)
    return set(names)


def verify_roundtrip(apk, before, pdbs):
    """Fail loudly if the rewrite lost anything.

    This is the check that would have caught the missing libassembly-store.so on the first
    try. A silent drop here means an APK that installs fine and then aborts on launch, so a
    regression has to break the build instead.
    """
    with zipfile.ZipFile(apk) as z:
        after = set(z.namelist())
        if b"\x00" in after:
            sys.exit("APK contains a null entry name; the copy produced a malformed archive")

        lost = before - after
        if lost:
            sys.exit(
                f"repack lost {len(lost)} original entrie(s), e.g. {sorted(lost)[:5]}. "
                "The resulting APK would install and then abort at startup."
            )

        # Checked before the PDB assertion: a missing runtime library is the failure that
        # aborts create_domain, and reporting "missing injected PDBs" for it would send you
        # looking in the wrong place.
        #
        # The .so files must be present, and their compression method must be whatever the
        # SDK produced. Do not "normalise" it: the .NET Android SDK ships libmonodroid.so
        # and libassembly-store.so deflated, and rewriting them as STORED is both slow and
        # not what the runtime expects. Only the injected PDBs are forced to STORED.
        for lib in ("libassembly-store.so", "libmonodroid.so", "libmonosgen-2.0.so"):
            if not any(n.endswith("/" + lib) for n in after):
                sys.exit(
                    f"{lib} is absent from the repacked APK. The .NET Android runtime aborts "
                    "in create_domain when the assembly store or runtime libs are missing."
                )

        added = after - before
        expected = {f"assemblies/{n}" for n in pdbs}
        if not expected <= added:
            sys.exit(f"missing injected PDBs: {sorted(expected - added)}")
    return added


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--apk", required=True, help="path to the unsigned APK, modified in place")
    ap.add_argument("--search", nargs="+", default=["bin", "obj"],
                    help="directories to scan for PDBs (default: bin obj)")
    ap.add_argument("--keep", action="store_true", help="keep the staged PDB directory")
    args = ap.parse_args()

    if not os.path.isfile(args.apk):
        sys.exit(f"APK not found: {args.apk}")

    pdbs = collect_pdbs(args.search)
    print(f"found {len(pdbs)} managed PDB(s)")
    for name in sorted(pdbs):
        print(f"  {name}")
    if not pdbs:
        sys.exit("no matching PDBs; a -c Debug build should have produced some")

    staged = tempfile.mkdtemp(prefix="pdb-stage-")
    try:
        for name, src in pdbs.items():
            shutil.copyfile(src, os.path.join(staged, name))

        tmp_apk = args.apk + ".tmp"
        try:
            before = copy_entries(args.apk, tmp_apk)

            with zipfile.ZipFile(tmp_apk, "a", compression=zipfile.ZIP_STORED) as z:
                for name in sorted(pdbs):
                    z.write(os.path.join(staged, name), f"assemblies/{name}")

            added = verify_roundtrip(tmp_apk, before, pdbs)
            os.replace(tmp_apk, args.apk)
        finally:
            if os.path.exists(tmp_apk):
                os.remove(tmp_apk)

        print(f"repacked {args.apk}: kept {len(before)} original entries, added {len(added)}")
    finally:
        if args.keep:
            print(f"staged PDBs kept at {staged}")
        else:
            shutil.rmtree(staged, ignore_errors=True)

    print("OK")


if __name__ == "__main__":
    main()
