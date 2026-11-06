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
    private global::Android.Net.Uri? _galleryJpegUri;
    private string _galleryJpegName = "fotoğraf.jpg";
    private readonly TextBlock _selected, _status;
    private readonly Canvas _donut;
    private readonly Avalonia.Controls.Button _open;
    private readonly Avalonia.Controls.Button _openGallery;
    private readonly ComboBox _sizeChoice;
    private readonly Slider _qualitySlider;
    private readonly TextBlock _qualityLabel;
    private readonly Avalonia.Controls.CheckBox _preserveExif;
    private readonly Avalonia.Controls.CheckBox _copyExactJpeg;
    private readonly Border _imageSettings;
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

        _openGallery = Button("Galeriden özgün JPG seç");
        _openGallery.HorizontalAlignment = HorizontalAlignment.Center;
        _openGallery.Padding = new Thickness(24, 11);
        _openGallery.Click += async (_, _) => await PickOriginalJpegFromGallery();

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
            Child = new StackPanel { Spacing = 12, Children = { _selected, _open, _openGallery } }
        });
        layout.Children.Add(new TextBlock
        {
            Text = "Dönüştürme biçimini seç",
            Foreground = White, FontSize = 16, FontWeight = FontWeight.SemiBold,
            TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 12, 0, 0)
        });

        _sizeChoice = new ComboBox
        {
            ItemsSource = new[] { "Orijinal çözünürlük (en fazla 60 MP)", "En fazla 8192 px", "En fazla 4096 px", "En fazla 2048 px" },
            SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch,
            Foreground = White
        };
        _qualityLabel = new TextBlock { Text = "JPG / WEBP kalite: 92", Foreground = White, FontSize = 14 };
        _qualitySlider = new Slider { Minimum = 60, Maximum = 100, Value = 92, TickFrequency = 1 };
        _qualitySlider.PropertyChanged += (_, e) =>
        {
            if (e.Property.Name == nameof(Slider.Value))
                _qualityLabel.Text = $"JPG / WEBP kalite: {(int)_qualitySlider.Value}";
        };
        _preserveExif = new Avalonia.Controls.CheckBox
        {
            Content = "Yeniden kodlamada standart EXIF'i koru (GPS hariç)",
            IsChecked = true, Foreground = Muted
        };
        _copyExactJpeg = new Avalonia.Controls.CheckBox
        {
            Content = "JPG'yi aynen kopyala (yeniden sıkıştırma yok)",
            IsChecked = false, Foreground = White, IsVisible = false
        };
        _copyExactJpeg.PropertyChanged += (_, e) =>
        {
            if (e.Property.Name == nameof(Avalonia.Controls.CheckBox.IsChecked))
            {
                _qualitySlider.IsEnabled = _copyExactJpeg.IsChecked != true;
                _qualityLabel.Text = _copyExactJpeg.IsChecked == true
                    ? "Kalite ayarı uygulanmaz (orijinal JPG kopyalanır)"
                    : $"JPG / WEBP kalite: {(int)_qualitySlider.Value}";
            }
        };
        _sizeChoice.PropertyChanged += (_, e) =>
        {
            if (e.Property.Name == nameof(ComboBox.SelectedIndex) && _sizeChoice.SelectedIndex != 0)
                _copyExactJpeg.IsChecked = false;
            _copyExactJpeg.IsEnabled = _sizeChoice.SelectedIndex == 0 && _preserveExif.IsChecked == true;
        };
        _preserveExif.PropertyChanged += (_, e) =>
        {
            if (e.Property.Name == nameof(Avalonia.Controls.CheckBox.IsChecked))
            {
                if (_preserveExif.IsChecked != true) _copyExactJpeg.IsChecked = false;
                _copyExactJpeg.IsEnabled = _sizeChoice.SelectedIndex == 0 && _preserveExif.IsChecked == true;
            }
        };
        _imageSettings = new Border
        {
            Background = Panel, CornerRadius = new CornerRadius(20),
            Padding = new Thickness(16),
            Margin = new Thickness(12, 0, 12, 0),
            IsVisible = false,
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock { Text = "Fotoğraf ayarları", FontSize = 17,
                        FontWeight = FontWeight.SemiBold, Foreground = White },
                    new TextBlock { Text = "Çıkış çözünürlüğü", Foreground = Muted },
                    _sizeChoice,
                    _qualityLabel,
                    _qualitySlider,
                    _preserveExif,
                    _copyExactJpeg,
                    new TextBlock
                    {
                        Text = "Aynen kopyala için galeriden JPG seç. Görüntü yeniden sıkıştırılmaz. MediaStore dışı sağlayıcılar GPS/EXIF'i önceden gizleyebilir.",
                        TextWrapping = TextWrapping.Wrap, Foreground = Muted, FontSize = 12
                    },
                    new TextBlock
                    {
                        Text = "PNG kayıpsızdır. HDR/10-bit ve tüm üretici özel EXIF alanları korunmaz.",
                        TextWrapping = TextWrapping.Wrap, Foreground = Muted, FontSize = 12
                    }
                }
            }
        };
        layout.Children.Add(_donut);
        layout.Children.Add(_imageSettings);
        layout.Children.Add(_status);
        layout.Children.Add(new TextBlock
        {
            Text = "Filee Android topluluk portu • α0.1.14",
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
        _imageSettings.IsVisible = _galleryJpegUri is not null ||
            (_inputs.Count > 0 && _inputs.All(x => MobileConversion.IsImage(x.Name)));
        _copyExactJpeg.IsVisible = _galleryJpegUri is not null ||
            (_inputs.Count > 0 && _inputs.All(x => MobileConversion.IsJpeg(x.Name)));
        if (!_copyExactJpeg.IsVisible) _copyExactJpeg.IsChecked = false;
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
            Text = _galleryJpegUri is not null ? "1 DOSYA" :
                   _inputs.Count == 0 ? "DOSYA\nSEÇ" : $"{_inputs.Count} DOSYA",
            Foreground = White, FontWeight = FontWeight.Bold,
            TextAlignment = TextAlignment.Center, Width = 120
        };
        Canvas.SetLeft(caption, cx - 60);
        Canvas.SetTop(caption, cy - 22);
        _donut.Children.Add(caption);

        string[] formats = _galleryJpegUri is not null
            ? ["JPG"]
            : _inputs.Count == 0
                ? ["JPG", "PNG", "WEBP", "PDF", "ZIP"]
                : MobileConversion.AvailableFormats(_inputs);
        for (int i = 0; i < formats.Length; i++)
        {
            var format = formats[i];
            var a = -Math.PI / 2 + i * 2 * Math.PI / formats.Length;
            var target = Button(format);
            target.Width = 80;
            target.Height = 48;
            target.IsEnabled = (_galleryJpegUri is not null || _inputs.Count > 0) && !_busy;
            target.Click += async (_, _) => await Convert(format);
            Canvas.SetLeft(target, cx + 120 * Math.Cos(a) - 40);
            Canvas.SetTop(target, cy + 120 * Math.Sin(a) - 24);
            _donut.Children.Add(target);
        }
    }

    private async Task PickOriginalJpegFromGallery()
    {
        if (_busy) return;
        try
        {
            var activity = MainActivity.Current
                ?? throw new InvalidOperationException("Android galeri penceresi açılamıyor.");
            var mediaUri = await activity.PickOriginalJpegAsync();
            if (mediaUri is null) return;

            // Do not route the native Android content:// URI through Avalonia's
            // TryGetFileFromPathAsync: on many devices this returns null.
            // Keep Android's URI and its read grant until the exact-copy action.
            var name = OriginalMediaAccess.GetGalleryJpegName(mediaUri);
            _galleryJpegUri = mediaUri;
            _galleryJpegName = name;
            _inputs.Clear();
            _sizeChoice.SelectedIndex = 0;
            _preserveExif.IsChecked = true;
            _selected.Text = name;
            RenderDonut();
            _copyExactJpeg.IsChecked = true;
            _status.Text = "JPG seçildi. Dosya değiştirilmeden kopyalanacak; bazı sağlayıcılar GPS/EXIF'i gizleyebilir.";
        }
        catch (Exception e)
        {
            global::Android.Util.Log.Error("FileeGalleryPicker", e.ToString());
            _status.Text = $"Galeri seçimi başarısız: {e.Message}";
        }
    }

    private MobileConversion.ImageOptions CurrentImageOptions(string target)
    {
        int max = _sizeChoice.SelectedIndex switch
        {
            1 => 8192,
            2 => 4096,
            3 => 2048,
            _ => 0
        };
        return new MobileConversion.ImageOptions(
            (int)_qualitySlider.Value, max, _preserveExif.IsChecked == true,
            target == "JPG" && _copyExactJpeg.IsChecked == true &&
            _inputs.All(x => MobileConversion.IsJpeg(x.Name)));
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
            _galleryJpegUri = null;
            _copyExactJpeg.IsChecked = false;
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
        if (_busy || (_inputs.Count == 0 && _galleryJpegUri is null)) return;
        var picker = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (picker is null) return;
        _busy = true;
        _open.IsEnabled = false;
        _openGallery.IsEnabled = false;
        RenderDonut();
        try
        {
            if (_galleryJpegUri is not null)
            {
                if (format != "JPG")
                    throw new NotSupportedException("Özgün galeri modunda yalnızca JPG kopyası yapılabilir.");
                if (_sizeChoice.SelectedIndex != 0 || _copyExactJpeg.IsChecked != true ||
                    _preserveExif.IsChecked != true)
                    throw new InvalidOperationException(
                        "Özgün kopya için orijinal çözünürlük, EXIF koruma ve aynen kopyala açık olmalı.");

                var original = _galleryJpegUri;
                await OriginalMediaAccess.EnsureOriginalAccessAsync(original);
                var output = await picker.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Özgün JPEG kopyasını kaydet",
                    SuggestedFileName = MobileConversion.OutputFileName(_galleryJpegName, "JPG")
                });
                if (output is null) return;
                _status.Text = "JPG değiştirilmeden kopyalanıyor ve doğrulanıyor…";
                var (bytes, hash, confirmedOriginal, readbackVerified) =
                    await OriginalMediaAccess.CopyGalleryJpegExactAsync(original, output);
                if (readbackVerified)
                {
                    _status.Text = confirmedOriginal
                        ? $"Özgün JPEG birebir doğrulandı ✓ {bytes:N0} bayt"
                        : $"Seçilen JPEG birebir doğrulandı ✓ {bytes:N0} bayt. " +
                          "Sağlayıcının gizlemiş olabileceği EXIF ayrı konudur.";
                }
                else
                {
                    _status.Text = $"JPG kaydedildi ✓ {bytes:N0} bayt. " +
                        "Android yeniden okumada farklı veri gösterdi; birebirlik uygulama içinde " +
                        "doğrulanamadı. Dosyaları ZIP olarak karşılaştır. SHA-256: " +
                        hash[..12] + "…";
                }
            }
            else if (format == "ZIP")
            {
                var output = await picker.SaveFilePickerAsync(new FilePickerSaveOptions
                { Title = "ZIP kaydet", SuggestedFileName = "filee_archive.zip" });
                if (output is null) return;
                _status.Text = "ZIP hazırlanıyor…";
                await MobileConversion.CreateZipAsync(_inputs, output);
                _status.Text = "ZIP kaydedildi ✓";
            }

            else if (format == "BİRLEŞTİR" && _inputs.Count >= 2 &&
                     _inputs.All(x => MobileConversion.IsPdf(x.Name)))
            {
                var activity = MainActivity.Current
                    ?? throw new InvalidOperationException("Android PDF kayıt ekranı kullanılamıyor.");
                var output = await activity.PickPdfSaveUriAsync("filee_birlesik.pdf");
                if (output is null) return;
                _status.Text = "PDF dosyaları birleştiriliyor ve doğrulanıyor…";
                var progress = new Progress<string>(message => _status.Text = message);
                var pages = await MobileConversion.MergePdfsAsync(_inputs, output, progress);
                _status.Text = $"PDF doğrulandı ve kaydedildi: {pages} sayfa ✓";
            }
            else if (format == "AYIR" && _inputs.All(x => MobileConversion.IsPdf(x.Name)))
            {
                var folders = await picker.OpenFolderPickerAsync(new FolderPickerOpenOptions
                { Title = "Ayrılan PDF sayfalarının kaydedileceği klasörü seç" });
                if (folders.Count == 0) return;
                _status.Text = "PDF sayfaları ayrılıyor…";
                var progress = new Progress<string>(message => _status.Text = message);
                var pages = await MobileConversion.SplitPdfsAsync(_inputs, folders[0], progress);
                _status.Text = $"{pages} ayrı PDF oluşturuldu ✓";
            }
            else if (_inputs.All(x => MobileConversion.IsPdf(x.Name)) &&
                     format is "JPG" or "PNG")
            {
                var folders = await picker.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "PDF sayfalarının kaydedileceği klasörü seçin"
                });
                if (folders.Count == 0) return;
                _status.Text = "PDF sayfaları dönüştürülüyor…";
                var progress = new Progress<string>(message => _status.Text = message);
                var converted = await MobileConversion.ConvertPdfPagesAsync(
                    _inputs, folders[0], format, progress);
                _status.Text = $"{converted} sayfa {format} olarak kaydedildi ✓";
            }
            else if (_inputs.Count == 1)
            {
                var input = _inputs[0];
                var options = CurrentImageOptions(format);
                if (options.ExactJpegCopy)
                    await OriginalMediaAccess.EnsureOriginalAccessAsync(input);
                var output = await picker.SaveFilePickerAsync(new FilePickerSaveOptions
                { Title = "Dosyayı kaydet", SuggestedFileName = MobileConversion.OutputFileName(input.Name, format) });
                if (output is null) return;
                _status.Text = $"{format} dönüştürülüyor…";
                await MobileConversion.ConvertImageAsync(input, output, format, options);
                _status.Text = options.ExactJpegCopy
                    ? "JPG dosyası değiştirilmeden kopyalandı ✓"
                    : "Dönüştürme tamamlandı ✓";
            }
            else
            {
                var options = CurrentImageOptions(format);
                if (options.ExactJpegCopy)
                    foreach (var input in _inputs)
                        await OriginalMediaAccess.EnsureOriginalAccessAsync(input);
                var folders = await picker.OpenFolderPickerAsync(new FolderPickerOpenOptions
                { Title = $"{format} dosyalarını kaydetmek için klasör seç" });
                if (folders.Count == 0) return;
                for (int i = 0; i < _inputs.Count; i++)
                {
                    _status.Text = $"Dönüştürülüyor: {i + 1}/{_inputs.Count}";
                    var target = await folders[0].CreateFileAsync(MobileConversion.OutputFileName(_inputs[i].Name, format, i));
                    await MobileConversion.ConvertImageAsync(_inputs[i], target, format, options);
                }
                _status.Text = _copyExactJpeg.IsChecked == true && format == "JPG"
                    ? $"{_inputs.Count} JPG değiştirilmeden kopyalandı ✓"
                    : $"{_inputs.Count} dosya kaydedildi ✓";
            }
        }
        catch (Exception e)
        {
            global::Android.Util.Log.Error("FileeConvert", e.ToString());
            _status.Text = $"Dönüştürme başarısız ({e.GetType().Name}): {e.Message}";
        }
        finally
        {
            _busy = false;
            _open.IsEnabled = true;
            _openGallery.IsEnabled = true;
            RenderDonut();
        }
    }
}