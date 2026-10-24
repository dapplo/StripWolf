#!/bin/sh
# Completes StripWolf.app after dotnet publish wrote the executable into Contents/MacOS:
# copies the Info.plist (with the comic book document types) with the version set, and creates Icon.icns.
#
#   ./installer/macos/complete-bundle.sh <path to StripWolf.app> <version, e.g. 1.2.3>
set -e

APP=$1
VERSION=$2
ROOT=$(cd "$(dirname "$0")/../.." && pwd)

if [ -z "$APP" ] || [ -z "$VERSION" ]; then
    echo "Usage: $0 <path to StripWolf.app> <version>" >&2
    exit 1
fi

mkdir -p "$APP/Contents/Resources"
cp "$ROOT/src/StripWolf/StripWolf.Desktop/Info.plist" "$APP/Contents/Info.plist"
plutil -replace CFBundleShortVersionString -string "$VERSION" "$APP/Contents/Info.plist"
plutil -replace CFBundleVersion -string "$VERSION" "$APP/Contents/Info.plist"
plutil -lint "$APP/Contents/Info.plist"

# Icon.icns, from the 256x256 application icon
ICONSET=$(mktemp -d)/Icon.iconset
mkdir -p "$ICONSET"
for SIZE in 16 32 64 128 256; do
    sips -z $SIZE $SIZE "$ROOT/media/Wolf.png" --out "$ICONSET/icon_${SIZE}x${SIZE}.png" >/dev/null
done
cp "$ICONSET/icon_32x32.png" "$ICONSET/icon_16x16@2x.png"
cp "$ICONSET/icon_64x64.png" "$ICONSET/icon_32x32@2x.png"
cp "$ICONSET/icon_256x256.png" "$ICONSET/icon_128x128@2x.png"
rm "$ICONSET/icon_64x64.png"
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/Icon.icns"
rm -rf "$(dirname "$ICONSET")"
