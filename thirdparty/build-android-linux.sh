#!/bin/bash
# Cross-compile the three native libraries (FreeType, Lua, OpenAL-soft) for
# Android arm64-v8a (API 24) on Linux. Adapted from the macOS-only
# build-*-android.sh scripts in this directory.
#
# Usage:  bash thirdparty/build-android-linux.sh <NDK_ROOT>
set -euo pipefail

NDK="${1:?usage: build-android-linux.sh <NDK_ROOT>}"
API=24
ABI=arm64-v8a
HOST=linux-x86_64
TOOLCHAIN="$NDK/toolchains/llvm/prebuilt/$HOST"
TARGET=aarch64-linux-android
CC="$TOOLCHAIN/bin/${TARGET}${API}-clang"
AR="$TOOLCHAIN/bin/llvm-ar"
RANLIB="$TOOLCHAIN/bin/llvm-ranlib"
export PATH="$TOOLCHAIN/bin:$PATH"

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
TP="$REPO_ROOT/thirdparty"
OUT="$REPO_ROOT/OpenRA.Android/jniLibs/$ABI"
mkdir -p "$OUT"

export CFLAGS="-O2 -fPIC"
export CXXFLAGS="-O2 -fPIC"
export LDFLAGS="-Wl,-z,max-page-size=16384"

# ---------------------------------------------------------------------------
# 1. Lua 5.1.5  →  liblua51.so
# ---------------------------------------------------------------------------
LUA_SRC="$TP/lua-5.1.5"
if [ ! -d "$LUA_SRC" ]; then
	curl -fsSL https://www.lua.org/ftp/lua-5.1.5.tar.gz -o /tmp/lua.tgz
	mkdir -p "$LUA_SRC"
	tar xzf /tmp/lua.tgz -C "$LUA_SRC" --strip-components=1
fi
rm -rf "$TP/lua-build-$ABI"
mkdir -p "$TP/lua-build-$ABI"
cd "$LUA_SRC"
make clean >/dev/null 2>&1 || true
CORE="lapi lcode ldebug ldo ldump lfunc lgc llex lmem lobject lopcodes lparser lstate lstring ltable ltm lundump lvm lzio"
LIB="lauxlib lbaselib ldblib liolib lmathlib loslib ltablib lstrlib loadlib linit"
OBJS=""
for f in $CORE $LIB; do
	"$CC" $CFLAGS -DLUA_USE_LINUX -c src/$f.c -o "$TP/lua-build-$ABI/$f.o"
	OBJS="$OBJS $TP/lua-build-$ABI/$f.o"
done
"$CC" -shared $LDFLAGS -Wl,-soname,liblua51.so -o "$OUT/liblua51.so" $OBJS -lm -ldl
rm -rf "$TP/lua-build-$ABI"
echo "[ok] liblua51.so"

# ---------------------------------------------------------------------------
# 2. FreeType 2  →  libfreetype6.so & libfreetype.so
# ---------------------------------------------------------------------------
FREETYPE_SRC="$TP/freetype2"
if [ ! -d "$FREETYPE_SRC" ]; then
	curl -fsSL https://download.savannah.gnu.org/releases/freetype/freetype-2.13.3.tar.gz -o /tmp/freetype.tgz
	mkdir -p "$FREETYPE_SRC"
	tar xzf /tmp/freetype.tgz -C "$FREETYPE_SRC" --strip-components=1
fi
rm -rf "$TP/freetype-build-$ABI"
mkdir -p "$TP/freetype-build-$ABI"
cmake -S "$FREETYPE_SRC" -B "$TP/freetype-build-$ABI" \
	-DCMAKE_TOOLCHAIN_FILE="$NDK/build/cmake/android.toolchain.cmake" \
	-DANDROID_ABI="$ABI" \
	-DANDROID_PLATFORM=android-$API \
	-DCMAKE_BUILD_TYPE=Release \
	-DCMAKE_SHARED_LINKER_FLAGS="-Wl,-z,max-page-size=16384" \
	-DBUILD_SHARED_LIBS=ON \
	-DFT_DISABLE_BZIP2=ON \
	-DFT_DISABLE_BROTLI=ON \
	-DFT_DISABLE_HARFBUZZ=ON \
	-DFT_DISABLE_PNG=ON \
	-DFT_DISABLE_ZLIB=OFF
cmake --build "$TP/freetype-build-$ABI" --config Release -j"$(nproc)"

SO_FILE=$(find "$TP/freetype-build-$ABI" -name "libfreetype*.so*" -type f | head -1)
if [ -n "$SO_FILE" ]; then
	echo "Found CMake FreeType shared lib: $SO_FILE"
	cp "$SO_FILE" "$OUT/libfreetype6.so"
	cp "$SO_FILE" "$OUT/libfreetype.so"
else
	echo "CMake did not produce .so, manually linking with -lz -lm..."
	FREETYPE_OBJS=$(find "$TP/freetype-build-$ABI/CMakeFiles/freetype.dir" -name "*.o" | sort)
	"$CC" -shared $LDFLAGS -Wl,-soname,libfreetype6.so \
		-o "$OUT/libfreetype6.so" \
		$FREETYPE_OBJS -lz -lm
	cp "$OUT/libfreetype6.so" "$OUT/libfreetype.so"
fi
rm -rf "$TP/freetype-build-$ABI"
echo "[ok] libfreetype6.so & libfreetype.so"

# ---------------------------------------------------------------------------
# 3. OpenAL-Soft  →  libsoft_oal.so
# ---------------------------------------------------------------------------
OPENAL_SRC="$TP/openal-soft"
if [ ! -d "$OPENAL_SRC" ]; then
	curl -fsSL https://github.com/kcat/openal-soft/archive/refs/tags/1.23.1.tar.gz -o /tmp/openal.tgz
	mkdir -p "$OPENAL_SRC"
	tar xzf /tmp/openal.tgz -C "$OPENAL_SRC" --strip-components=1
fi
rm -rf "$TP/openal-build-$ABI"
cmake -S "$OPENAL_SRC" -B "$TP/openal-build-$ABI" \
	-DCMAKE_TOOLCHAIN_FILE="$NDK/build/cmake/android.toolchain.cmake" \
	-DANDROID_ABI="$ABI" \
	-DANDROID_PLATFORM=android-$API \
	-DANDROID_STL=c++_static \
	-DCMAKE_BUILD_TYPE=Release \
	-DCMAKE_INSTALL_PREFIX="$TP/openal-android" \
	-DALSOFT_BACKEND_OSS=OFF \
	-DALSOFT_BACKEND_OPENSL=ON \
	-DALSOFT_BACKEND_WAVE=OFF \
	-DALSOFT_EXAMPLES=OFF \
	-DALSOFT_TESTS=OFF \
	-DALSOFT_UTILS=OFF \
	-DALSOFT_INSTALL=ON \
	-DCMAKE_SHARED_LINKER_FLAGS="-Wl,-z,max-page-size=16384"
cmake --build "$TP/openal-build-$ABI" --config Release -j"$(nproc)"
# Try to find the built shared library in several possible locations
OPENAL_SO=$(find "$TP/openal-build-$ABI" -name "libopenal.so" -print -quit)
if [ -z "$OPENAL_SO" ]; then
	cmake --install "$TP/openal-build-$ABI" --config Release 2>/dev/null || true
	OPENAL_SO=$(find "$TP/openal-android" -name "libopenal.so" -print -quit)
	if [ -z "$OPENAL_SO" ]; then
		OPENAL_SO=$(find "$TP" -path "*/openal-android*" -name "libopenal.so" -print -quit)
	fi
fi
if [ -z "$OPENAL_SO" ]; then
	echo "ERROR: libopenal.so not found after CMake build and install"
	exit 1
fi
cp "$OPENAL_SO" "$OUT/libsoft_oal.so"
rm -rf "$TP/openal-build-$ABI" "$TP/openal-android"
echo "[ok] libsoft_oal.so"

echo ""
echo "=== All native libraries built ==="
ls -lh "$OUT"

# Verify all required libraries are present
for lib in liblua51.so libfreetype6.so libsoft_oal.so; do
	if [ ! -f "$OUT/$lib" ]; then
		echo "ERROR: $lib is missing from $OUT!"
		exit 1
	fi
	echo "  OK: $lib"
done
