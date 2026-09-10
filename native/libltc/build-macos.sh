#!/bin/sh
# libltc をソースから arm64(Apple Silicon 専用)dylib としてビルドし、アプリ同梱用に install_name を @rpath 化する
# usage: build-macos.sh <出力ディレクトリ>   → <出力ディレクトリ>/libltc.dylib
set -eu
OUT=${1:?出力ディレクトリを指定}
HERE=$(cd "$(dirname "$0")" && pwd)
VER=1.3.2
URL=https://github.com/x42/libltc/releases/download/v1.3.2/libltc-1.3.2.tar.gz
SHA256=0a6d42cd6c21e925a27fa560dc45ac80057d275f23342102825909c02d3b1249
WORK=${LIBLTC_WORK:-$HERE/work}
mkdir -p "$WORK" "$OUT"
cd "$WORK"
[ -f libltc-$VER.tar.gz ] || curl -fsSL -o libltc-$VER.tar.gz "$URL"
echo "$SHA256  libltc-$VER.tar.gz" | shasum -a 256 -c -
rm -rf libltc-$VER && tar xzf libltc-$VER.tar.gz
cd libltc-$VER
./configure --disable-static --disable-dependency-tracking \
  CFLAGS="-arch arm64 -O2 -mmacosx-version-min=12.0" \
  LDFLAGS="-arch arm64" >configure.log
make -j"$(sysctl -n hw.ncpu)" >make.log
DYLIB=$(ls src/.libs/libltc.*.dylib | grep -E 'libltc\.[0-9]+\.dylib' | head -1)
cp "$DYLIB" "$OUT/libltc.dylib"
install_name_tool -id @rpath/libltc.dylib "$OUT/libltc.dylib"
codesign --force -s - "$OUT/libltc.dylib"
cp COPYING "$OUT/libltc-COPYING.txt"
# 検証: arm64 のみ・@rpath・署名
[ "$(lipo -archs "$OUT/libltc.dylib")" = arm64 ]
otool -D "$OUT/libltc.dylib" | grep -q '^@rpath/libltc.dylib$'
codesign -v "$OUT/libltc.dylib"
echo "ok: $OUT/libltc.dylib ($(lipo -archs "$OUT/libltc.dylib"))"
