# Filee Android — community port (alpha 0.1)

This Android fork is not an official Filee release. Filee original authors: © 2026 Myeonggu Kim (KnifeLemon) and Filee contributors, MIT license. Original source: https://github.com/KnifeLemon/Filee

## What is implemented
- Native Android startup using .NET 10 + Avalonia.Android 12.1.4.
- Filee-inspired circular, touch-controlled output selection.
- Android file picker and save picker (Storage Access Framework through Avalonia).
- JPEG, PNG, WEBP image conversion using Android bitmap codecs; decoded HEIC/HEIF may work depending on hardware.
- Image to one-page PDF using Android PdfDocument.
- One or more files to a ZIP archive and multiple image conversion.
- Direct reference to Filee.Core (the original format registry and pipeline types), with integration planned.

## Not yet implemented
- Original desktop donut drag/drop, presets, background services and history.
- Original Filee.Engines / JobQueue integration.
- 263/270 format parity, RAW support, HDR metadata preservation, DOCX/HWPX conversions, PDF merge/split, all video/audio formats and external FFmpeg, LibreOffice and others.
- Engine status/preset editor and Android-specific sharing intents.

**Note:** The first adapter uses BitmapFactory and may downsample photos larger than 4096 pixels. Output is not lossless or guaranteed to preserve HDR/EXIF. JPEG/WEBP quality is fixed at 90. Do not overwrite original files.

## Building and testing
On a machine with .NET SDK 10, Android SDK API 36 and Java 21:

    dotnet workload install android
    dotnet publish src/Filee.Android/Filee.Android.csproj -c Release -p:AndroidPackageFormat=apk

GitHub Actions can attempt a build via the "Filee Android (experimental)" workflow on the feature/android-alpha branch, and will upload the APK as a run artifact if successful.

No successful APK compilation, installation or device validation has been claimed yet. A real Android 16 / arm64 phone test is required before describing the port as working.

## Planned next steps
1. Get the first CI build green; install on Android and test picker, PDF, ZIP, image formats.
2. Adapt Android SAF content:// URIs to Filee.Core's local-file-based conversion pipeline.
3. Verify and replace incompatible Android ARM64 native engines, then connect Filee.Engines and JobQueue.
4. Port video/audio via Android-compatible FFmpeg and verify HEVC HDR PQ/10-bit metadata.
5. Continue format parity and mobile preset/UI work, while preserving MIT and third-party notices.
