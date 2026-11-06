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

## 0.1.2 startup repair

The automated Android 15 launch test caught `java.lang.IllegalStateException: You need to use a Theme.AppCompat theme`. Replaced the Android platform Material theme with `Theme.AppCompat.DayNight.NoActionBar` (required by Avalonia.Android's AppCompatActivity). The 0.1.1 port also migrated to `IActivityApplicationLifetime.MainViewFactory` for Avalonia 12. GitHub Actions now builds and tries to launch each APK in an Android emulator and retains logcat + screenshot diagnostics.

## 0.1.3 PDF pages to PNG/JPG

When the picked files are PDFs, the circular menu now offers JPG and PNG in addition to ZIP. Selecting either asks for a destination folder and rasterizes every PDF page separately using Android's native PdfRenderer. A private cache copy is used because SAF streams may not be seekable. Original PDFs are unchanged; output filenames include a unique batch prefix and page numbers. The current cap is 200 MB per PDF, 300 pages, and 3200 pixels for the longest rendered side. Encrypted/unsupported PDFs are rejected with an error message.

Automated GitHub Actions checks compilation and opening on an Android emulator. Conversion correctness still requires manual device testing with actual PDF inputs.


## 0.1.4 PDF merging and splitting

Adds Android-compatible PDFsharp 6.2.4, the same library used by Filee.Engines. When multiple PDF files are selected, BİRLEŞTİR appears on the ring and preserves vector PDF pages in one output PDF, following the selected order. AYIR exports a separate single-page PDF to a chosen folder. Uses the Android SAF picker, app-private temporary seekable input files (200 MB cap per PDF), unique filename suffixes and up to 300 pages per source. Encrypted or malformed inputs may fail; no OCR or PDF repair is attempted. Tests must verify actual merge/split results on a device; the emulator CI only checks startup.


## 0.1.5 Android SAF/PDFsharp writer repair

In 0.1.4 real device testing PDF merge failed with Arg_NotSupportedException. The suspected issue was PDFsharp using seek operations with non-seekable Android SAF output streams. Merge and split now save PDFs to temporary seekable app-private cache files and copy them to the chosen document URI, then delete the temporary output. Logcat now captures the exception details using FileeConvert. The CI emulator smoke test executes a synthetic PDFsharp 1+2 page merge/split/readback check and requires a PASS log. Device testing with real SAF files remains necessary.


## 0.1.6 Native PDF MIME and post-save validation

A real device reported a merged PDF that could not be opened despite a successful page-count message. On Android, Avalonia's document picker may request the generic MIME `*/*`. Merge PDF saving therefore uses a native Android ACTION_CREATE_DOCUMENT intent with `application/pdf`, retaining the document picker and folder choice. The generated PDF is validated using Android's native PdfRenderer before writing, and the saved user-visible output is reopened and checked for page count, ability to render the first page and non-empty bytes before success is shown. The emulator regression also validates the PDFsharp merge and split samples with Android PdfRenderer; this does not replace a device test of external PDF input/output. PDF split still uses the previous save path and awaits the same MIME-specific upgrade.


## 0.1.7 Photo conversion settings

Added an image-only settings panel with JPG/WEBP quality (60–100), maximum long-edge dimension choices (original <=60 MP, 8192, 4096, 2048), and standard EXIF copy excluding GPS coordinates. Image orientation (EXIF 2–8) is applied to pixels before encoding, and EXIF Orientation is normalized. Images are encoded in app-private cache, verified for dimensions, then copied to the chosen Android SAF document. PNG uses lossless compression, quality slider ignored. Standard capture details are copied only where supported by the Android platform writer; MakerNotes, full metadata, embedded profiles, HDR and 10-bit pixel values are not guaranteed to survive. GitHub's emulator regression covers EXIF orientation 6, JPG EXIF, PNG resizing and WEBP resizing; manual phone tests with camera files are still required.


## 0.1.8 Byte-exact JPEG preservation

A new opt-in setting "JPG'yi aynen kopyala" applies only when source and target are JPEG, output dimensions are set to original, and EXIF preservation is enabled. It copies source JPEG bytes without decoding or encoding; quality slider is disabled in this mode. Consequently the exact bitstream, existing 4:2:2 or 4:2:0 sampling, EXIF MakerNotes, lens metadata, ICC, and **GPS location if present** are all preserved. This is an unmodified *copy* rather than a conversion. Never select it if you intend to strip location metadata or reduce file size. Other image conversions still reencode through Android.Bitmap and may change subsampling and proprietary metadata. Expanded the best-effort standard EXIF field list for reencoded images; this is not a guarantee that unsupported proprietary tags survive. Emulator regression uses a synthetic JPEG and verifies exact SHA-256 equality plus unmodified EXIF orientation, followed by the existing conversion tests.
