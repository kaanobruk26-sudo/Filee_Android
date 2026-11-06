namespace Filee.Mobile;

/// <summary>
/// Converts photos sequentially to bound mobile memory use.
/// A single bad photo is reported without stopping the remaining queue.
/// </summary>
internal static class BatchConversionQueue
{
    public sealed record ItemResult(int Index, string Name, bool Succeeded, string? Error);
    public sealed record Progress(int Completed, int Total, int Succeeded, int Failed, ItemResult Item);
    public sealed record Summary(IReadOnlyList<ItemResult> Items)
    {
        public int Total => Items.Count;
        public int Succeeded => Items.Count(x => x.Succeeded);
        public int Failed => Total - Succeeded;
    }

    public static async Task<Summary> RunAsync<T>(
        IReadOnlyList<T> items,
        Func<T, int, Task> convert,
        Func<T, string> getName,
        Action<Progress>? onProgress = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(convert);
        ArgumentNullException.ThrowIfNull(getName);

        var results = new List<ItemResult>(items.Count);
        var succeeded = 0;
        var failed = 0;

        for (int i = 0; i < items.Count; i++)
        {
            var name = getName(items[i]);
            ItemResult result;
            try
            {
                await convert(items[i], i);
                result = new ItemResult(i, name, true, null);
                succeeded++;
            }
            catch (Exception ex)
            {
                failed++;
                var message = ex.Message.Length > 200 ? ex.Message[..200] + "…" : ex.Message;
                result = new ItemResult(i, name, false, ex.GetType().Name + ": " + message);
                global::Android.Util.Log.Warn("FileeBatch",
                    $"Item {i + 1}/{items.Count} {name} failed: {ex}");
            }
            results.Add(result);
            onProgress?.Invoke(new Progress(i + 1, items.Count, succeeded, failed, result));
        }
        return new Summary(results.AsReadOnly());
    }

    public static async Task RunSelfTestAsync()
    {
        try
        {
            var names = new[] { "a.heic", "bad.heic", "c.jpg", "bad2.jpg" };
            var attempted = new List<int>();
            var updates = new List<Progress>();
            var summary = await RunAsync(names,
                async (_, index) =>
                {
                    await Task.Yield();
                    attempted.Add(index);
                    if (index is 1 or 3) throw new InvalidDataException("synthetic decode failure");
                },
                name => name, updates.Add);
            if (summary.Total != 4 || summary.Succeeded != 2 || summary.Failed != 2 ||
                !attempted.SequenceEqual(new[] { 0, 1, 2, 3 }) ||
                updates.Count != 4 || updates[^1].Completed != 4 ||
                updates[^1].Succeeded != 2 || updates[^1].Failed != 2 ||
                !summary.Items[2].Succeeded || summary.Items[1].Succeeded)
                throw new InvalidOperationException("Queue progress/result regression.");
            global::Android.Util.Log.Info("FileeBatchTest",
                "PASS: 4 attempted, 2 succeeded, 2 failed, queue continued");
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Error("FileeBatchTest", "FAIL: " + ex);
        }
    }
}
