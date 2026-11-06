#!/usr/bin/env bash
set -euo pipefail

package="org.filee.mobile.community"
mkdir -p /tmp/filee-ci
trap 'adb logcat -d -v time > /tmp/filee-ci/logcat.txt 2>/dev/null || true; adb exec-out screencap -p > /tmp/filee-ci/screen.png 2>/dev/null || true' EXIT

apk=$(find src/Filee.Android/bin/Release -path '*/publish/*Signed.apk' -print -quit)
if [[ -z "$apk" ]]; then
  echo "Signed APK not found." >&2
  exit 1
fi

echo "APK: $apk"
adb install -r "$apk"
adb logcat -c
adb shell am force-stop "$package" || true
adb shell monkey -p "$package" -c android.intent.category.LAUNCHER 1
sleep 12

pid=$(adb shell pidof "$package" || true)
if [[ -z "$pid" ]]; then
  echo "App closed or crashed during startup." >&2
  adb logcat -d -v brief | grep -E 'FATAL EXCEPTION|AndroidRuntime|Avalonia|Filee' | tail -80 || true
  exit 1
fi
echo "Startup smoke check PASSED. App remains running with PID $pid."


echo "Testing PDFsharp merge and split on Android emulator..."
activity=$(adb shell cmd package resolve-activity --brief "$package" | tr -d '\r' | tail -n 1)
if [[ "$activity" != *"/"* ]]; then
  echo "Launcher activity not resolved: $activity" >&2
  exit 1
fi
adb shell am force-stop "$package"
adb logcat -c
adb shell am start -n "$activity" --ez filee_pdf_selftest true
sleep 10
adb logcat -d -v brief -s FileePdfTest:I '*:S' | tee /tmp/filee-ci/pdf-self-test.txt
if ! grep -q 'FileePdfTest.*PASS' /tmp/filee-ci/pdf-self-test.txt; then
  echo "Android PDF engine regression check FAILED" >&2
  exit 1
fi
echo "Android PDF engine regression check PASSED"


echo "Testing native image conversion, EXIF, orientation, dimensions..."
adb shell am force-stop "$package"
adb logcat -c
adb shell am start -n "$activity" --ez filee_image_selftest true
sleep 12
adb logcat -d -v brief -s FileeImageTest:I '*:S' | tee /tmp/filee-ci/image-self-test.txt
if ! grep -q 'FileeImageTest.*PASS' /tmp/filee-ci/image-self-test.txt; then
  echo "Android image conversion regression check FAILED" >&2
  exit 1
fi
echo "Android image conversion regression check PASSED"
