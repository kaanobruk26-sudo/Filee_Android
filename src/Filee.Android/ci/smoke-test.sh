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


echo "Testing MediaStore unredacted original read..."
adb shell pm grant "$package" android.permission.ACCESS_MEDIA_LOCATION
adb shell am force-stop "$package"
adb logcat -c
adb shell am start -n "$activity" --ez filee_original_selftest true
sleep 12
adb logcat -d -v brief -s FileeOriginalTest:I '*:S' | tee /tmp/filee-ci/original-media-self-test.txt
if ! grep -q 'FileeOriginalTest.*PASS' /tmp/filee-ci/original-media-self-test.txt; then
  echo "Unredacted original MediaStore read FAILED" >&2
  exit 1
fi
echo "MediaStore original SHA-256 check PASSED"


echo "Testing real CC0 HEIC decode, resize and EXIF orientation..."
adb shell am force-stop "$package"
adb logcat -c
adb shell am start -n "$activity" --ez filee_heif_selftest true
sleep 14
adb logcat -d -v brief -s FileeHeifTest:I '*:S' | tee /tmp/filee-ci/heif-self-test.txt
if ! grep -q 'FileeHeifTest.*PASS' /tmp/filee-ci/heif-self-test.txt; then
  echo "HEIC/HEIF conversion regression FAILED" >&2
  exit 1
fi
echo "HEIC/HEIF conversion regression PASSED"


echo "Testing batch queue progress and per-file error isolation..."
adb shell am force-stop "$package"
adb logcat -c
adb shell am start -n "$activity" --ez filee_batch_selftest true
sleep 6
adb logcat -d -v brief -s FileeBatchTest:I '*:S' | tee /tmp/filee-ci/batch-self-test.txt
if ! grep -q 'FileeBatchTest.*PASS' /tmp/filee-ci/batch-self-test.txt; then
  echo "Batch queue regression FAILED" >&2
  exit 1
fi
echo "Batch queue regression PASSED"


echo "Testing saved-folder browser Android intent..."
adb shell am force-stop "$package"
adb logcat -c
adb shell am start -n "$activity" --ez filee_folder_selftest true
sleep 5
adb logcat -d -v brief -s FileeFolderTest:I '*:S' | tee /tmp/filee-ci/folder-self-test.txt
if ! grep -q 'FileeFolderTest.*PASS' /tmp/filee-ci/folder-self-test.txt; then
  echo "Saved-folder intent regression FAILED" >&2
  exit 1
fi
echo "Saved-folder intent regression PASSED"
