using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace Filee.Mobile;

/// <summary>Filee halka seçicisinin Android için dokunmatik başlangıç sürümü.</summary>
public sealed class MainView : UserControl
{
    private readonly List<IStorageFile> _inputs = [];
    private readonly TextBlock _selected, _status;
    private readonly Canvas _donut;
    private readonly Avalonia.Controls.Button _open;
    private bool _busy;

    private static readonly IBrush Dark = Brush("#13141E");
    private static readonly IBrush Panel = Brush("#202131");
    private static readonly IBrush Accent = Brush("#7F77DD");
    private static readonly IBrush White = Brush("#F7F7FF");
    private static readonly IBrush Muted = Brush("#AFB0C6");
    private static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));

    public MainView()
    {
        _selected = new TextBlock
        {
            Text = "Henüz dosya seçilmedi",
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Muted,
            MaxWidth = 310,
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _status = new TextBlock
        {
            Text = "Dosyalar telefonunda dönüştürülür.",
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Muted,
            MaxWidth = 310,
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _open = Button("+ Dosya seç");
        _open.HorizontalAlignment = HorizontalAlignment.Center;
        _open.Padding = new Thickness(24, 13);
        _open.Click += async (_, _) => await PickFiles();

        _donut = new Canvas { Width = 350, Height = 340, HorizontalAlignment = HorizontalAlignment.Center };
        var layout = new StackPanel { Spacing = 14, Margin = new Thickness(12, 30, 12, 30) };
        layout.Children.Add(new TextBlock
        {
            Text = "filee.", FontSize = 43, FontWeight = FontWeight.Bold,
            Foreground = White, TextAlignment = TextAlignment.Center
        });
        layout.Children.Add(new TextBlock
        {
            Text = "Dosyanı seç. Biçime dokun. Dönüştür.",
            FontSize = 14, Foreground = Muted, TextAlignment = TextAlignment.Center
        });
        layout.Children.Add(new Border
        {
            Background = Panel, CornerRadius = new CornerRadius(22),
            Padding = new Thickness(16), Margin = new Thickness(12, 10, 12, 0),
            Child = new StackPanel { Spacing = 14, Children = { _selected, _open } }
        });
        layout.Children.Add(new TextBlock
        {
            Text = "Dönüştürme biçimini seç",
            Foreground = White, FontSize = 16, FontWeight = FontWeight.SemiBold,
            TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 12, 0, 0)
        });
        layout.Children.Add(_donut);
        layout.Children.Add(_status);
        layout.Children.Add(new TextBlock
        {
            Text = "Filee Android topluluk portu • α0.1.2",
            Foreground = Muted, FontSize = 11, TextAlignment = TextAlignment.Center
        });
        Content = new Border
        {
            Background = Dark,
            Child = new ScrollViewer { Content = layout }
        };
        RenderDonut();
    }

    private static Avalonia.Controls.Button Button(string title) => new()
    {
        Content = title, Background = Accent, Foreground = White,
        FontWeight = FontWeight.SemiBold, FontSize = 14,
        BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(18),
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center
    };

    private void RenderDonut()
    {
        _donut.Children.Clear();
        const double cx = 175, cy = 170;
        var ring = new Ellipse
        {
            Width = 292, Height = 292, Fill = Panel,
            Stroke = Accent, StrokeThickness = 2
        };
        Canvas.SetLeft(ring, cx - 146);
        Canvas.SetTop(ring, cy - 146);
        _donut.Children.Add(ring);

        var middle = new Ellipse { Width = 140, Height = 140, Fill = Dark };
        Canvas.SetLeft(middle, cx - 70);
        Canvas.SetTop(middle, cy - 70);
        _donut.Children.Add(middle);

        var caption = new TextBlock
        {
            Text = _inputs.Count == 0 ? "DOSYA\nSEÇ" : $"{_inputs.Count} DOSYA",
            Foreground = White, FontWeight = FontWeight.Bold,
            TextAlignment = TextAlignment.Center, Width = 120
        };
        Canvas.SetLeft(caption, cx - 60);
        Canvas.SetTop(caption, cy - 22);
        _donut.Children.Add(caption);

        string[] formats = _inputs.Count == 0
            ? ["JPG", "PNG", "WEBP", "PDF", "ZIP"]
            : MobileConversion.AvailableFormats(_inputs);
        for (int i = 0; i < formats.Length; i++)
        {
            var format = formats[i];
            var a = -Math.PI / 2 + i * 2 * Math.PI / formats.Length;
            var target = Button(format);
            target.Width = 80;
            target.Height = 48;
            target.IsEnabled = _inputs.Count > 0 && !_busy;
            target.Click += async (_, _) => await Convert(format);
            Canvas.SetLeft(target, cx + 120 * Math.Cos(a) - 40);
            Canvas.SetTop(target, cy + 120 * Math.Sin(a) - 24);
            _donut.Children.Add(target);
        }
    }

    private async Task PickFiles()
    {
        var picker = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (picker is null || !picker.CanOpen) { _status.Text = "Dosya seçici kullanılamıyor."; return; }
        try
        {
            var files = await picker.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Dönüştürülecek dosyaları seç", AllowMultiple = true
            });
            if (files.Count == 0) return;
            _inputs.Clear();
            _inputs.AddRange(files);
            _selected.Text = files.Count == 1 ? files[0].Name : $"{files.Count} dosya seçildi";
            _status.Text = "Halka üzerindeki hedef biçime dokun.";
            RenderDonut();
        }
        catch (Exception e) { _status.Text = $"Seçim başarısız: {e.Message}"; }
    }

    private async Task Convert(string format)
    {
        if (_busy || _inputs.Count == 0) return;
        var picker = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (picker is null) return;
        _busy = true;
        _open.IsEnabled = false;
        RenderDonut();
        try
        {
            if (format == "ZIP")
            {
                var output = await picker.SaveFilePickerAsync(new FilePickerSaveOptions
                { Title = "ZIP kaydet", SuggestedFileName = "filee_archive.zip" });
                if (output is null) return;
                _status.Text = "ZIP hazırlanıyor…";
                await MobileConversion.CreateZipAsync(_inputs, output);
                _status.Text = "ZIP kaydedildi ✓";
            }
            else if (_inputs.Count == 1)
            {
                var input = _inputs[0];
                var output = await picker.SaveFilePickerAsync(new FilePickerSaveOptions
                { Title = "Dosyayı kaydet", SuggestedFileName = MobileConversion.OutputFileName(input.Name, format) });
                if (output is null) return;
                _status.Text = $"{format} dönüştürülüyor…";
                await MobileConversion.ConvertImageAsync(input, output, format);
                _status.Text = "Dönüştürme tamamlandı ✓";
            }
            else
            {
                var folders = await picker.OpenFolderPickerAsync(new FolderPickerOpenOptions
                { Title = $"{format} dosyalarını kaydetmek için klasör seç" });
                if (folders.Count == 0) return;
                for (int i = 0; i < _inputs.Count; i++)
                {
                    _status.Text = $"Dönüştürülüyor: {i + 1}/{_inputs.Count}";
                    var target = await folders[0].CreateFileAsync(MobileConversion.OutputFileName(_inputs[i].Name, format, i));
                    await MobileConversion.ConvertImageAsync(_inputs[i], target, format);
                }
                _status.Text = $"{_inputs.Count} dosya kaydedildi ✓";
            }
        }
        catch (Exception e) { _status.Text = $"Dönüştürme başarısız: {e.Message}"; }
        finally
        {
            _busy = false;
            _open.IsEnabled = true;
            RenderDonut();
        }
    }
}