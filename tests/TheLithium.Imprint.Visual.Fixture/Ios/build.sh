#!/usr/bin/env bash
# Builds the iOS fixture app for the simulator with clang, without an Xcode project.
# Usage: build.sh <output.app>. Needs Xcode with an iOS simulator SDK.
set -euo pipefail

output="$1"
here="$(cd "$(dirname "$0")" && pwd)"
sdk="$(xcrun --sdk iphonesimulator --show-sdk-path)"
arch="$(uname -m)"

rm -rf "$output"
mkdir -p "$output"
xcrun --sdk iphonesimulator clang -fobjc-arc -arch "$arch" -mios-simulator-version-min=15.0 -isysroot "$sdk" \
  -framework UIKit -framework Foundation -framework CoreGraphics "$here/main.m" -o "$output/Fixture"
cp "$here/Info.plist" "$output/Info.plist"
codesign --force --sign - "$output"
echo "Built $output for $arch"
