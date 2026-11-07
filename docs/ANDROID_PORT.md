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


## 0.1.9 Android original MediaStore read

The original Canon photo and v0.1.8 copy had equal JPEG image data but differed in 2 GPSVersionID EXIF bytes. Android MediaProvider can redact location EXIF when read without permission. This version requests ACCESS_MEDIA_LOCATION **only** when the opt-in exact JPG copy mode is used. It resolves MediaStore media content URIs, including SAF media-document `image:ID` references, and opens them using MediaStore.SetRequireOriginal plus ContentResolver.OpenFileDescriptor. If permission is denied, the provider is unsupported, or the original cannot be provided, the operation fails visibly rather than silently copying possibly redacted bytes. A direct file URI is copied without needing the media location permission. Exact mode retains any GPS location data, and does not reencode JPEG. An emulator test imports a synthetic EXIF JPEG into MediaStore and verifies an unredacted SHA-256 match. Real-device testing with the previously uploaded Canon original is still required.


## 0.1.10 Native gallery picker for original JPEG

The regular file picker may return documents-provider URLs rather than MediaStore URLs, causing v0.1.9 exact-copy mode to reject otherwise valid photos. This update adds a "Galeriden özgün JPG seç" button using Android ACTION_PICK filtered to the MediaStore images collection and image/jpeg. The returned content URI is converted to an Avalonia IStorageFile through TryGetFileFromPathAsync. Gallery selection enables exact copy, original dimensions and EXIF retention. Unsupported gallery provider URIs still fail transparently. The original file picker is preserved for ordinary conversions. Actual Xiaomi gallery URI must be checked in device testing; emulator regression verifies MediaStore original reads but does not exercise a human-driven gallery picker.


## 0.1.11 Native gallery URI conversion repair

The Xiaomi test exposed the Avalonia TryGetFileFromPathAsync return of null for an Android content URI selected by the native gallery picker. This update keeps the selected Android URI directly instead of trying to create an IStorageFile from it. The native gallery route shows only JPG and passes the URI to the Android MediaStore original-byte reader; it asks for ACCESS_MEDIA_LOCATION only on exact copy. Source bytes are cached, output written with the standard SAF save picker, then output is re-opened and SHA-256 checked against the source before success is shown. If the URI's provider does not support original bytes the error is explicit. A synthetic MediaStore URI filename query is added to the existing emulator regression test. Interactive Xiaomi gallery selection and SAF output verification remain to be tested on-device.


## 0.1.12 Third-party gallery provider fallback

Xiaomi's ACTION_PICK returns content://com.android.fileexplorer.myprovider, which v0.1.11 rejected. The native JPEG copy path now distinguishes MediaStore original-byte access from other picker-granted content providers. For MediaStore URI, ACCESS_MEDIA_LOCATION + SetRequireOriginal remain required to read unredacted original. For unrecognized content authorities, the app reads ContentResolver.OpenInputStream on the picker-selected URI, writes its bytes without image decoding, and checks the SHA-256 of the user-visible saved output. **Important:** source-to-output SHA-256 equality proves only that selected provider-supplied bytes are copied intact; it cannot prove the provider didn't already redact EXIF/GPS. The UI explicitly states that caveat when the provider is not MediaStore. The emulator synthetic test also reads a sample MediaStore URI through the generic streaming helper; the actual Xiaomi provider remains for user device testing.


## 0.1.13 Content provider JPEG readback false negative

A Xiaomi 14T Pro test showed an InvalidDataException after saving the JPEG, because OpenReadAsync on the saved SAF output returned a stream whose SHA256 did not match the pre-write cache. The user exported both JPGs in an archive ('2 .zip') and these files were byte-for-byte identical, length 4,184,760 bytes and SHA-256 613102ad4aaaad357ca8024fa0ab218de5b4e3b125997b5ec9fa521fa3a03999. This proves that the particular failure was a false-negative readback check rather than corrupt JPEG output. Android document/media providers can return EXIF/GPS-redacted views. The app still hashes and checks the saved file via SAF, but a mismatch or inaccessible readback now produces an explicit **saved but unverified** status rather than a misleading conversion-failed exception. A match is still required for a 'verified' claim. The emulator regression adds a hash-mismatch classification test. An external ZIP/exported-byte comparison remains necessary for unmatched readback, and the app must not claim it is byte-exact in that case.


## 0.1.14 HEIC/HEIF decoding validation

Recognizes `.heic`, `.heif`, and Canon/Fujifilm `.hif` image extensions as image inputs for JPG/PNG/WEBP conversion. HEIC decoding uses the Android built-in image codecs (offline, no extra native dependency); unsupported 10-bit HDR/4:2:2 device codecs produce a specific user-facing error rather than a generic crash. Android emulator CI downloads two CC0 real HEIC fixtures from heic.fyi with pinned SHA-256 hashes and embeds them in the APK solely for regression tests. The offline emulator test covers 8-bit HEIC->JPG, resized HEIC->PNG, and orientation-6 HEIC->JPG with expected output dimensions. These synthetic HEICs do not prove decoding of all phone or Canon HIF profiles. JPEG/PDF tests remain unchanged. Fixture license: CC0 1.0, heic.fyi/heic-sample-files.

 
## 0.1.15 HEIC metadata and color-managed output

Replaced Android framework EXIF write/read in the re-encoding path with AndroidX ExifInterface 1.4.2.3 (stable binding of AndroidX 1.4.2). This improves reading HEIF tags and writing JPEG/PNG/WebP standard EXIF. Only an explicit whitelist of non-GPS fields is transferred. For each field, the output is reopened and its presence checked (rational values can be normalized). The Android log identifies fields missing from the output. MakerNotes and proprietary metadata are not relocated into new images, and sensitive location metadata is not copied. EXIF Orientation is set to 1 because the pixel orientation has been corrected; EXIF ColorSpace is set to sRGB.

BitmapFactory now requests ARGB_8888 and sRGB explicitly. The decoded bitmap and encoded output color spaces are checked so wide-gamut input is intentionally converted for SDR compatibility instead of incorrectly transplanting HEIC ICC metadata onto sRGB JPEG pixels. Conversion stops if sRGB cannot be established reliably, rather than silently producing wrong colors. The original HEIC ICC and HDR/10-bit information are **not preserved** in the re-encoded output. A dedicated Android emulator regression verifies model, make, lens, date, orientation and declared sRGB on a synthetic image, while the previous CC0 HEIC/PDF/JPEG regression tests remain required. The user's original HEIC file was analyzed locally and is *not committed to this repository*.


## 0.1.16 Photo batch queue and progress

The UI now includes a batch progress bar, completed/total/succeeded/failed counters, and a scrollable per-file result log. BatchConversionQueue runs sequentially to bound memory usage and continues with later images if any photo fails. Failed partial outputs are deleted when the Android storage provider permits it. JPG, PNG and WEBP are supported batch targets; unsupported combinations now show an explicit error. A new emulator smoke test uses four jobs with two deliberate failures, verifying all items were attempted and the correct results were reported in order. Real-device batch HEIC/JPG testing is still needed.


## 0.1.17 Compact settings, visible results and saved folder

Photo settings now start collapsed with a live summary (resolution, quality or exact JPEG mode, EXIF). The user can expand them without losing selected values. Successful photo/ZIP conversions also show a result panel, and the screen scrolls to results when work completes. Batch results remain per file and include counts and errors. A saved-folder button appears when an Android SAF content URI can be obtained from a folder or output; it opens Android ACTION_OPEN_DOCUMENT using EXTRA_INITIAL_URI. Some Android providers ignore the initial URI, so browsing is best effort and not guaranteed to open the exact folder. An emulator test validates the intent structure; on-device Xiaomi navigation remains to be confirmed.


## 0.1.18 Exact saved folder preview on Xiaomi

The user confirmed v0.1.17 successfully converted three photos to their chosen 'Filee Test' folder but tapping 'Kaydedilen klasörü göster' opened the parent 'Download'. For batch conversions and PDF folder outputs, v0.1.18 now retains the exact IStorageFolder selected by the user and lists the folder's items **inside Filee**, independent of Android's initial-location picker behavior. The selected folder's name appears in the result panel and up to 100 file/folder names are listed; results are read via IStorageFolder.GetItemsAsync. For single-file outputs the app attempts GetParentAsync; if no folder access exists, the UI says so instead of claiming the desired folder opened. An optional external Android document chooser remains but is labeled as a best-effort action. The external intent now normalizes SAF tree URIs into actual document URIs via DocumentsContract.BuildDocumentUriUsingTree/GetTreeDocumentId. The native test verifies Filee Test subfolder ID is retained rather than dropping to Download. Actual Xiaomi folder listing still requires phone confirmation.
