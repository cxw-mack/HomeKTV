#!/bin/bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")" && pwd)"
cd "$ROOT"

if ! command -v swift >/dev/null 2>&1; then
  echo "Swift is not available. Install Xcode 15 or newer from the Mac App Store first."
  exit 1
fi
if ! command -v codesign >/dev/null 2>&1 || ! command -v lipo >/dev/null 2>&1 || ! command -v ditto >/dev/null 2>&1; then
  echo "Xcode command-line tools are incomplete. Run: xcode-select --install"
  exit 1
fi

swift test -c release

APP="$ROOT/dist/HomeKTV-Mac.app"
ZIP="$ROOT/dist/HomeKTV-Mac-macos.zip"
rm -rf "$APP" "$ZIP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

# Build both supported Mac architectures so one package works on Intel and Apple Silicon Macs.
swift build -c release --arch arm64
ARM_BIN="$(swift build -c release --arch arm64 --show-bin-path)/HomeKTVMac"
swift build -c release --arch x86_64
INTEL_BIN="$(swift build -c release --arch x86_64 --show-bin-path)/HomeKTVMac"
lipo -create "$ARM_BIN" "$INTEL_BIN" -output "$APP/Contents/MacOS/HomeKTVMac"
cp "$ROOT/Info.plist" "$APP/Contents/Info.plist"
codesign --force --deep --sign - "$APP"
codesign --verify --deep --strict --verbose=2 "$APP"
ditto -c -k --sequesterRsrc --keepParent "$APP" "$ZIP"
echo "Created: $APP"
echo "Created: $ZIP"
