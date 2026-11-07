using System.IO;

namespace Filee.Mobile;

/// <summary>
/// Lightweight in-memory filtering/pagination for SAF folder listings.
/// A folder always remains visible so users can navigate nested directories
/// while filtering images or PDFs.
/// </summary>
internal static class FolderListFilter
{
    internal enum Category { All, Images, Pdf }
    public const int PageSize = 20;
    public const int MaxEnumeratedItems = 500;

    private static readonly HashSet<string> ImageExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".webp", ".bmp",
            ".heic", ".heif", ".hif", ".gif", ".tif", ".tiff", ".avif"
        };

    public static bool Matches(string filename, bool isFolder, Category category) =>
        isFolder || category switch
        {
            Category.All => true,
            Category.Images => ImageExtensions.Contains(Path.GetExtension(filename)),
            Category.Pdf => Path.GetExtension(filename).Equals(
                ".pdf", StringComparison.OrdinalIgnoreCase),
            _ => false
        };

    public static int NextVisibleCount(int current, int total) =>
        Math.Min(total, Math.Max(0, current) + PageSize);

    public static void RunSelfTest()
    {
        try
        {
            var names = new[]
            {
                ("photo.HEIC", false), ("document.PDF", false),
                ("scan.png", false), ("notes.txt", false), ("subfolder", true)
            };
            int Count(Category category) =>
                names.Count(x => Matches(x.Item1, x.Item2, category));

            if (Count(Category.All) != 5 ||
                Count(Category.Images) != 3 ||
                Count(Category.Pdf) != 2 ||
                !Matches("archive.JpEg", false, Category.Images) ||
                !Matches("album", true, Category.Pdf) ||
                Matches("notes.txt", false, Category.Images) ||
                Matches("photo.webp", false, Category.Pdf) ||
                NextVisibleCount(0, 55) != 20 ||
                NextVisibleCount(20, 55) != 40 ||
                NextVisibleCount(40, 55) != 55)
                throw new InvalidOperationException(
                    "File category filtering and paging failed.");

            global::Android.Util.Log.Info("FileeFilterTest",
                "PASS: all/images/PDF filters, folders remain, paging 20/40/55");
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Error("FileeFilterTest", "FAIL: " + ex);
        }
    }
}
