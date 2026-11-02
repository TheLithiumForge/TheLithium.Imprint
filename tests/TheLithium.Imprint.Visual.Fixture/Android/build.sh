#!/usr/bin/env bash
# Builds the Android fixture APK from its Java source with the SDK's own build tools, without Gradle.
# Usage: build.sh <output.apk>. Needs ANDROID_HOME (or ANDROID_SDK_ROOT) with a platform and build-tools, and a JDK.
set -euo pipefail

output="$(realpath -m "$1")"
here="$(cd "$(dirname "$0")" && pwd)"
sdk="${ANDROID_HOME:-${ANDROID_SDK_ROOT:?Set ANDROID_HOME to the Android SDK.}}"
tools="$(ls -d "$sdk"/build-tools/*/ | sort -V | tail -n 1)"
platform="$(ls -d "$sdk"/platforms/android-*/ | sort -V | tail -n 1)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

echo "Using build tools $tools and platform $platform"
mkdir -p "$work/classes" "$work/dex"
javac -source 11 -target 11 -Xlint:-options -classpath "$platform/android.jar" -d "$work/classes" \
  $(find "$here/src" -name '*.java')
"$tools/d8" --lib "$platform/android.jar" --min-api 26 --output "$work/dex" $(find "$work/classes" -name '*.class')
"$tools/aapt2" link -o "$work/unsigned.apk" -I "$platform/android.jar" --manifest "$here/AndroidManifest.xml"
(cd "$work/dex" && zip -q -u "$work/unsigned.apk" classes.dex)
"$tools/zipalign" -f 4 "$work/unsigned.apk" "$work/aligned.apk"
keytool -genkeypair -keystore "$work/fixture.keystore" -storepass android -keypass android -alias fixture \
  -keyalg RSA -keysize 2048 -validity 1 -dname "CN=Imprint fixture" > /dev/null 2>&1
"$tools/apksigner" sign --ks "$work/fixture.keystore" --ks-pass pass:android --out "$output" "$work/aligned.apk"
"$tools/apksigner" verify "$output"
echo "Built $output"
