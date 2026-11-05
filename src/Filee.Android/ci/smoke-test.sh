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
