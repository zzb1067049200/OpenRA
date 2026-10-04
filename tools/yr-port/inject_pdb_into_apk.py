#!/usr/bin/env python3
"""Rebuild an (unsigned) APK from an exploded directory, preserving every entry.

The .NET Android SDK resolves PDBs into its internal _ResolvedSymbols item but nothing in
the packaging pipeline consumes it, so a Debug build still ships without symbols and every
on-device stack trace arrives without line numbers. Rather than keep fighting SDK internals,
we explode the APK, drop the PDBs into assemblies/ (where the .NET Android runtime looks for
them), and repack.

Stored uncompressed, matching what aapt2 produces for an unsigned APK. Must run BEFORE
signing: repacking invalidates any signature already applied.

Usage: inject_pdb_into_apk.py <exploded_dir> <output_apk>
"""
import os
import sys
import zipfile


def main():
    if len(sys.argv) != 3:
        sys.exit(f"usage: {sys.argv[0]} <exploded_dir> <output_apk>")

    work, apk = sys.argv[1], sys.argv[2]

    if not os.path.isdir(work):
        sys.exit(f"not a directory: {work}")

    entries = []
    for root, _, files in os.walk(work):
        for name in files:
            full = os.path.join(root, name)
            arc = os.path.relpath(full, work).replace(os.sep, "/")
            entries.append((full, arc))

    if not entries:
        sys.exit(f"no files under {work}; refusing to write an empty APK")

    # AndroidManifest.xml must be present, otherwise the APK is not installable and the
    # failure will look like a signing problem much later.
    if not any(arc == "AndroidManifest.xml" for _, arc in entries):
        sys.exit("AndroidManifest.xml missing from the exploded directory")

    entries.sort(key=lambda x: x[1])

    with zipfile.ZipFile(apk, "w", zipfile.ZIP_STORED) as z:
        for full, arc in entries:
            z.write(full, arc)

    print(f"rebuilt {apk}: {len(entries)} entries")

    with zipfile.ZipFile(apk) as z:
        pdbs = [n for n in z.namelist() if n.lower().endswith(".pdb")]
    print(f"pdb entries in the rebuilt APK: {len(pdbs)}")
    for name in pdbs[:10]:
        print(f"  {name}")


if __name__ == "__main__":
    main()
