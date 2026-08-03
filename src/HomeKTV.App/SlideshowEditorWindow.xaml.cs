using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using HomeKTV.App.ViewModels;
using HomeKTV.Core.Models;
using HomeKTV.Core.Portable;
using HomeKTV.Infrastructure.Media;
using Microsoft.Win32;

namespace HomeKTV.App;

public partial class SlideshowEditorWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly PortablePaths _paths;
    private Song? _song;
    private SlideshowConfiguration _configuration = new();

    public SlideshowEditorWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel; _paths = PortablePaths.FromBaseDirectory();
        AudioSongs = new ObservableCollection<Song>(viewModel.Songs.Where(IsAudioSong));
        DataContext = this;
        Loaded += (_, _) => { SongBox.SelectedItem = AudioSongs.FirstOrDefault(); if (SongBox.SelectedItem is null) StatusText.Text = "歌库中还没有纯音频歌曲"; };
    }

    public ObservableCollection<Song> AudioSongs { get; }
    public ObservableCollection<SlideshowImageItem> Images { get; } = [];
    public IReadOnlyList<SlideshowTransition> Transitions { get; } = Enum.GetValues<SlideshowTransition>();
    public IReadOnlyList<SlideshowFitMode> FitModes { get; } = Enum.GetValues<SlideshowFitMode>();
    public IReadOnlyList<LyricRegionPosition> LyricPositions { get; } = Enum.GetValues<LyricRegionPosition>();

    private static bool IsAudioSong(Song song) => song.MediaType is SongMediaType.Audio or SongMediaType.AudioWithSlideshow || (!string.IsNullOrWhiteSpace(song.AudioRelativePath) && string.IsNullOrWhiteSpace(song.VideoRelativePath));
    private async void SongBox_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (IsLoaded && SongBox.SelectedItem is Song song) await RunUiAsync(() => LoadSongAsync(song)); }
    private async Task LoadSongAsync(Song song)
    {
        _song = song; _configuration = await _viewModel.LoadSlideshowAsync(song.Id); Images.Clear();
        foreach (var relative in _configuration.ImageRelativePaths) TryAddItem(relative);
        IntervalBox.Text = _configuration.IntervalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        TransitionBox.SelectedItem = _configuration.Transition; FitBox.SelectedItem = _configuration.FitMode; LyricPositionBox.SelectedItem = _configuration.LyricRegionPosition;
        ShuffleBox.IsChecked = _configuration.Shuffle; LoopBox.IsChecked = _configuration.Loop; CoverFirstBox.IsChecked = _configuration.ShowCoverFirst; ShowLyricsBox.IsChecked = _configuration.ShowLyrics; ShowInfoBox.IsChecked = _configuration.ShowSongInformation; ShowNextBox.IsChecked = _configuration.ShowNextSong; BlurSlider.Value = _configuration.BackgroundBlurRadius; MaskSlider.Value = _configuration.ImageMaskOpacity;
        if (Images.Count > 0) ImageList.SelectedIndex = 0; else ShowDefaultPreview();
        StatusText.Text = $"《{song.Title}》：{Images.Count} 张自定义图片";
    }

    private void TryAddItem(string relative)
    {
        try
        {
            var absolute = _paths.Resolve(relative); if (!File.Exists(absolute) || !TryReadImage(absolute, out var details)) return;
            Images.Add(new SlideshowImageItem(relative, absolute, details));
        }
        catch (Exception exception) { System.Diagnostics.Debug.WriteLine($"跳过无法读取的幻灯片图片 {relative}: {exception}"); }
    }

    private async void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        if (_song is null) return;
        var dialog = new OpenFileDialog { Filter = "图片|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff;*.webp", Multiselect = true };
        if (dialog.ShowDialog(this) == true) await AddSourcesAsync(dialog.FileNames);
    }

    private async void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_song is null) return;
        var dialog = new OpenFolderDialog { Title = "选择图片文件夹", Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        var files = Directory.EnumerateFiles(dialog.FolderName).Where(SlideshowImageResolver.IsSupportedImage).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        await AddSourcesAsync(files);
    }

    private async Task AddSourcesAsync(IEnumerable<string> paths)
    {
        if (_song is null) return;
        var valid = paths.Where(path => Path.GetExtension(path).Equals(".webp",StringComparison.OrdinalIgnoreCase)?SlideshowImageResolver.IsUsableImage(path):TryReadImage(path, out _)).ToList();
        await RunUiAsync(async () =>
        {
            var added = await _viewModel.AddSlideshowImagesAsync(_song, valid);
            foreach (var relative in added) TryAddItem(relative);
            StatusText.Text = $"新增 {added.Count} 张，已跳过损坏或重复图片";
        });
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e) => MoveSelected(-1);
    private void MoveDown_Click(object sender, RoutedEventArgs e) => MoveSelected(1);
    private void MoveSelected(int delta)
    {
        if (ImageList.SelectedItem is not SlideshowImageItem item) return;
        var index = Images.IndexOf(item); var target = index + delta; if (target < 0 || target >= Images.Count) return;
        Images.Move(index, target); ImageList.SelectedIndex = target;
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (ImageList.SelectedItem is not SlideshowImageItem item || _song is null) return;
        var index = Images.IndexOf(item); Images.Remove(item);
        var songDirectory = Path.GetFullPath(Path.Combine(_paths.SlideshowSongs, _song.Id.ToString())) + Path.DirectorySeparatorChar;
        var absolute = Path.GetFullPath(item.AbsolutePath);
        if (absolute.StartsWith(songDirectory, StringComparison.OrdinalIgnoreCase) && File.Exists(absolute)) try { File.Delete(absolute); } catch (IOException exception) { StatusText.Text = "图片已从列表移除，但文件正在使用：" + exception.Message; }
        ImageList.SelectedIndex = Images.Count == 0 ? -1 : Math.Min(index, Images.Count - 1); if (Images.Count == 0) ShowDefaultPreview();
    }

    private void SetCover_Click(object sender, RoutedEventArgs e)
    {
        if (_song is null || ImageList.SelectedItem is not SlideshowImageItem item) return;
        _song.CoverRelativePath = item.RelativePath; CoverFirstBox.IsChecked = true; StatusText.Text = "已设为封面；保存配置后生效";
    }

    private async void RotateLeft_Click(object sender, RoutedEventArgs e) => await RotateSelectedAsync(-90);
    private async void RotateRight_Click(object sender, RoutedEventArgs e) => await RotateSelectedAsync(90);
    private async Task RotateSelectedAsync(double angle)
    {
        if (ImageList.SelectedItem is not SlideshowImageItem item) return;
        await RunUiAsync(async () =>
        {
            await Task.Run(() => RotateFile(item.AbsolutePath, angle));
            if (!TryReadImage(item.AbsolutePath, out var details)) throw new InvalidDataException("旋转后的图片无法读取。");
            var index = Images.IndexOf(item); Images[index] = new SlideshowImageItem(item.RelativePath, item.AbsolutePath, details + " · 已旋转"); ImageList.SelectedIndex = index; ShowPreview(Images[index]);
        });
    }

    private static void RotateFile(string path, double angle)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        BitmapEncoder encoder = extension switch { ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = 92 }, ".png" => new PngBitmapEncoder(), ".bmp" => new BmpBitmapEncoder(), ".gif" => new GifBitmapEncoder(), ".tif" or ".tiff" => new TiffBitmapEncoder(), _ => throw new NotSupportedException("该图片格式不支持无损本地旋转，请先转换为 JPG 或 PNG。") };
        BitmapFrame source; using (var input = File.OpenRead(path)) { var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad); source = decoder.Frames[0]; }
        var rotated = new TransformedBitmap(source, new System.Windows.Media.RotateTransform(angle)); rotated.Freeze(); encoder.Frames.Add(BitmapFrame.Create(rotated));
        var temporary = path + ".rotate"; using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None)) encoder.Save(output); File.Move(temporary, path, true);
    }

    private void RestoreDefaults_Click(object sender, RoutedEventArgs e) { Images.Clear(); ShowDefaultPreview(); StatusText.Text = "已选择程序默认背景；保存后生效"; }
    private void ShowDefaultPreview()
    {
        var path = Directory.Exists(_paths.SlideshowDefaults) ? Directory.EnumerateFiles(_paths.SlideshowDefaults).FirstOrDefault(SlideshowImageResolver.IsSupportedImage) : null;
        if (path is null) { PreviewImage.Source = null; EmptyPreviewText.Visibility = Visibility.Visible; return; }
        ShowPreview(new SlideshowImageItem(_paths.ToRelative(path), path, "程序默认背景"));
    }

    private void ImageList_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (ImageList.SelectedItem is SlideshowImageItem item) ShowPreview(item); }
    private void ShowPreview(SlideshowImageItem item)
    {
        try { PreviewImage.Source = LoadBitmap(item.AbsolutePath, 1000); EmptyPreviewText.Visibility = Visibility.Collapsed; }
        catch (Exception) { PreviewImage.Source = null; EmptyPreviewText.Text = "图片损坏，已跳过"; EmptyPreviewText.Visibility = Visibility.Visible; }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_song is null) return;
        if (!double.TryParse(IntervalBox.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var interval) || interval is < 1 or > 120) { MessageBox.Show(this, "切换间隔必须为 1 到 120 秒。", "参数无效", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        _configuration.SongId = _song.Id; _configuration.IntervalSeconds = interval; _configuration.Transition = TransitionBox.SelectedItem is SlideshowTransition transition ? transition : SlideshowTransition.Fade; _configuration.FitMode = FitBox.SelectedItem is SlideshowFitMode fit ? fit : SlideshowFitMode.ContainBlurBackground; _configuration.LyricRegionPosition = LyricPositionBox.SelectedItem is LyricRegionPosition position ? position : LyricRegionPosition.Bottom;
        _configuration.Shuffle = ShuffleBox.IsChecked == true; _configuration.Loop = LoopBox.IsChecked != false; _configuration.ShowCoverFirst = CoverFirstBox.IsChecked != false; _configuration.ShowLyrics = ShowLyricsBox.IsChecked != false; _configuration.ShowSongInformation = ShowInfoBox.IsChecked != false; _configuration.ShowNextSong = ShowNextBox.IsChecked != false; _configuration.BackgroundBlurRadius = BlurSlider.Value; _configuration.ImageMaskOpacity = MaskSlider.Value; _configuration.ImageRelativePaths = Images.Select(x => x.RelativePath).ToList();
        await RunUiAsync(async () => { await _viewModel.SaveSlideshowAsync(_song, _configuration); StatusText.Text = "幻灯片配置已保存"; });
    }

    private static bool TryReadImage(string path, out string details)
    {
        details = string.Empty;
        try { var image = LoadBitmap(path, 128); details = $"{image.PixelWidth} × {image.PixelHeight} · {new FileInfo(path).Length / 1024:N0} KB"; return image.PixelWidth > 0 && image.PixelHeight > 0; }
        catch (Exception) { return false; }
    }
    private static BitmapImage LoadBitmap(string path, int decodeWidth) { var image = new BitmapImage(); using var stream = File.OpenRead(path); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.DecodePixelWidth = decodeWidth; image.StreamSource = stream; image.EndInit(); image.Freeze(); return image; }
    private async void Reload_Click(object sender, RoutedEventArgs e) { if (_song is not null) await RunUiAsync(() => LoadSongAsync(_song)); }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private async Task RunUiAsync(Func<Task> action) { try { IsEnabled = false; await action(); } catch (Exception exception) { MessageBox.Show(this, exception.Message, "幻灯片编辑失败", MessageBoxButton.OK, MessageBoxImage.Error); } finally { IsEnabled = true; } }
}

public sealed record SlideshowImageItem(string RelativePath, string AbsolutePath, string Details) { public string DisplayName => Path.GetFileName(AbsolutePath); }
