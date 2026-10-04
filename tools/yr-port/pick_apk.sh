#!/usr/bin/env bash
# Print the path of the COMPLETE APK produced by `dotnet publish`.
#
# Why not `find . -name '*.apk' -print -quit`
# ------------------------------------------------
# A publish leaves several APKs behind, and they are NOT equivalent:
#
#   OpenRA.Android/obj/Debug/android/bin/com.openra.android.apk   ~113 MB, PARTIAL
#   bin/publish/com.openra.android-Signed.apk                     ~249 MB, COMPLETE
#   bin/publish/com.openra.android.apk                            (unsigned copy)
#   bin/com.openra.android-Signed.apk                             (another copy)
#
# The partial one sorts first alphabetically -- "O" (OpenRA.Android/...) precedes "b"
# (bin/...) in the C locale, and again for ./bin vs ./OpenRA.Android -- so
# `-print -quit` reliably selects it. It installs fine and it even accepts the injected
# PDBs, but it has no libassembly-store.so, so the .NET Android runtime aborts in
# create_domain before MainActivity runs:
#
#   F monodroid: No assemblies (or assembly blobs) were found in the application APK
#                file(s) or on the filesystem
#
# Why this checks content, not size
# ---------------------------------
# Size looks like the obvious discriminator (the assembly store is 26 MB per ABI, so the
# complete package is about twice the partial one) and it works for the real 113 MB vs
# 249 MB case. It is still the wrong test: a local experiment that injected PDBs into the
# partial package made it the largest file, and this script then picked the broken one.
# Content is the actual property that matters, so test that.
#
# Prints nothing and exits 1 when no usable APK is found, so callers can use `|| APK=""`.
set -euo pipefail

command -v unzip >/dev/null 2>&1 || { echo "pick_apk.sh: unzip is required" >&2; exit 1; }

# libassembly-store.so is the embedded managed assembly blob. Without it the runtime
# cannot resolve any assembly and aborts before managed code runs.
REQUIRED="lib/arm64-v8a/libassembly-store.so"

pick() {
    # Newest first, so that when several candidates qualify (e.g. an unsigned and a signed
    # copy of the same build) the most recent publish wins rather than an arbitrary one.
    find . -type f -name '*.apk' -printf '%T@ %p\n' 2>/dev/null | sort -rn | cut -d' ' -f2-
}

COMPLETE=""
FALLBACK=""

while IFS= read -r apk; do
    [ -n "$apk" ] || continue
    # `grep -c`, not `grep -q`. Under `set -o pipefail`, `grep -q` exits the moment it
    # matches, `unzip -l` takes SIGPIPE, and the pipeline reports 141 -- so a successful
    # match looks like a failure. Measured on this exact input:
    #
    #   unzip -l app.apk | grep -q libassembly-store.so   -> 0
    #   ( set -eo pipefail; same pipeline )               -> 141
    #
    # `grep -c` consumes all input, so no SIGPIPE, and the count is what we test anyway.
    if [ "$(unzip -l "$apk" 2>/dev/null | grep -c "$REQUIRED" || true)" -gt 0 ]; then
        COMPLETE="$apk"
        break
    fi
    # Not `[ -z "$FALLBACK" ] && FALLBACK="$apk"`: under `set -e` that form returns 1 on the
    # last iteration once FALLBACK is set, which would abort the script.
    if [ -z "$FALLBACK" ]; then
        FALLBACK="$apk"
    fi
done < <(pick)

if [ -n "$COMPLETE" ]; then
    echo "note: selected on content ($REQUIRED present)" >&2
    echo "$COMPLETE"
    exit 0
fi

# Nothing complete. Echo the fallback anyway so the caller can report a precise diagnosis
# instead of "no APK found", and exit non-zero so `|| APK=""` still works.
if [ -n "$FALLBACK" ]; then
    echo "pick_apk.sh: no APK contains $REQUIRED; the runtime would abort at startup." >&2
    echo "pick_apk.sh: candidates were:" >&2
    find . -type f -name '*.apk' -printf '  %10s %p\n' 2>/dev/null | sort -rn >&2
    echo "$FALLBACK"
fi
exit 1
