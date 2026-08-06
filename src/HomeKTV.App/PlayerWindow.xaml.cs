using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Media.Animation;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using System.ComponentModel;
using HomeKTV.App.ViewModels;
using HomeKTV.Lyrics;
using Microsoft.Win32;
using QRCoder;
using Screen=System.Windows.Forms.Screen;

namespace HomeKTV.App;

public partial class PlayerWindow : Window
{
    private readonly MainViewModel _viewModel;private readonly DispatcherTimer _timer;private LrcDocument _lyrics=LrcParser.Parse(null);private string? _loadedLyric;private string? _loadedSlide;private bool _useSlideA;private int _displayIndex;private bool _closeForShutdown;
    public PlayerWindow(MainViewModel viewModel)
    {
        InitializeComponent();DataContext=_viewModel=viewModel;_displayIndex=viewModel.Settings.PlaybackDisplayIndex;
        Resources["LyricShadow"]=new DropShadowEffect{BlurRadius=12,ShadowDepth=1,Color=Colors.Black,Opacity=.9};
        _timer=new DispatcherTimer(TimeSpan.FromMilliseconds(100),DispatcherPriority.Render,TimerTick,Dispatcher);_timer.Start();
        Loaded+=(_,_)=>{VideoView.MediaPlayer=_viewModel.Player?.MediaPlayer;PlaceOnPreferredScreen();LoadIdleBackground();};Closing+=OnClosing;Closed+=OnClosed;SystemEvents.DisplaySettingsChanged+=DisplaySettingsChanged;CreateQr();
    }

    private void PlaceOnPreferredScreen()
    {
        var screens=Screen.AllScreens;if(screens.Length==0)return;_displayIndex=Math.Clamp(_displayIndex,0,screens.Length-1);var target=screens[_displayIndex];
        var useFullscreen=_viewModel.Settings.PlaybackFullscreen&&screens.Length>1;
        if(useFullscreen){WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;Topmost=true;WindowState=WindowState.Normal;UpdateLayout();SetWindowPos(new WindowInteropHelper(this).Handle,IntPtr.Zero,target.Bounds.Left,target.Bounds.Top,target.Bounds.Width,target.Bounds.Height,0x0040);}
        else{WindowStyle=WindowStyle.SingleBorderWindow;ResizeMode=ResizeMode.CanResize;Topmost=false;WindowState=WindowState.Normal;UpdateLayout();var area=target.WorkingArea;var width=Math.Min(1280,area.Width);var height=Math.Min(720,area.Height);SetWindowPos(new WindowInteropHelper(this).Handle,IntPtr.Zero,area.Left+(area.Width-width)/2,area.Top+(area.Height-height)/2,width,height,0x0040);}
    }

    public void MoveToNextScreen(){var count=Screen.AllScreens.Length;if(count==0)return;_displayIndex=(_displayIndex+1)%count;_viewModel.Settings.PlaybackDisplayIndex=_displayIndex;PlaceOnPreferredScreen();}
    public void ShowPlayer(){if(!IsVisible)Show();Activate();}
    public void HidePlayer(){if(!_closeForShutdown)Hide();}
    public void CloseForShutdown(){_closeForShutdown=true;if(IsLoaded)Close();}
    private void CloseButton_Click(object sender,RoutedEventArgs e)=>HidePlayer();

    private async void TimerTick(object? sender,EventArgs e)
    {
        try
        {
            var active=_viewModel.CurrentTitle!="等待点歌";PlaybackOverlay.Visibility=active?Visibility.Visible:Visibility.Collapsed;IdlePanel.Visibility=active?Visibility.Collapsed:Visibility.Visible;
            SlideshowLayer.Visibility=active&&_viewModel.IsSlideshowVisible?Visibility.Visible:Visibility.Collapsed;ApplySlideshowSettings();ApplyLyricsSettings();
            if(SlideshowLayer.Visibility==Visibility.Visible&&_loadedSlide!=_viewModel.CurrentSlideshowRelativePath){_loadedSlide=_viewModel.CurrentSlideshowRelativePath;LoadSlide(_loadedSlide);}
            if(_loadedLyric!=_viewModel.CurrentLyricRelativePath){_loadedLyric=_viewModel.CurrentLyricRelativePath;_lyrics=string.IsNullOrWhiteSpace(_loadedLyric)?LrcParser.Parse(null):await LrcParser.ParseFileAsync(_viewModel.ResolvePortablePath(_loadedLyric));}
            var frame=_lyrics.CreateKaraokeFrame(TimeSpan.FromMilliseconds(_viewModel.PlaybackPositionMs),_viewModel.CurrentLyricOffsetMs);UpdateKaraokeLyrics(frame);
            LyricsPanel.Visibility=ShouldDisplayLyrics()?Visibility.Visible:Visibility.Collapsed;
            if(LyricsPanel.Visibility==Visibility.Collapsed){if(!_viewModel.Settings.Lyrics.KeepSongInformationWhenHidden)SongInformationPanel.Visibility=Visibility.Collapsed;if(!_viewModel.Settings.Lyrics.KeepNextSongWhenHidden)NextSongPanel.Visibility=Visibility.Collapsed;TopInfoPanel.Visibility=SongInformationPanel.Visibility==Visibility.Visible||NextSongPanel.Visibility==Visibility.Visible?Visibility.Visible:Visibility.Collapsed;}
        }
        catch(Exception){TopLyric.Text="歌词文件不可读取";BottomLyric.Text="";}
    }

    private void LoadSlide(string? relativePath)
    {
        if(string.IsNullOrWhiteSpace(relativePath))return;
        try
        {
            var path=_viewModel.ResolvePortablePath(relativePath);var image=new BitmapImage();using(var stream=File.OpenRead(path)){image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.DecodePixelWidth=1920;image.StreamSource=stream;image.EndInit();image.Freeze();}
            var incoming=_useSlideA?SlideImageA:SlideImageB;var outgoing=_useSlideA?SlideImageB:SlideImageA;_useSlideA=!_useSlideA;incoming.Source=image;SlideBackground.Source=image;
            var transition=_viewModel.CurrentSlideshowConfiguration?.Transition??HomeKTV.Core.Models.SlideshowTransition.Fade;if(transition==HomeKTV.Core.Models.SlideshowTransition.None){incoming.BeginAnimation(OpacityProperty,null);outgoing.BeginAnimation(OpacityProperty,null);incoming.Opacity=1;outgoing.Opacity=0;outgoing.Source=null;return;}var duration=transition==HomeKTV.Core.Models.SlideshowTransition.CrossDissolve?650:380;if(transition==HomeKTV.Core.Models.SlideshowTransition.Fade){outgoing.Source=null;outgoing.Opacity=0;}
            var fadeIn=new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(duration));var fadeOut=new DoubleAnimation(outgoing.Opacity,0,TimeSpan.FromMilliseconds(duration));
            fadeOut.Completed+=(_,_)=>{outgoing.Source=null;};incoming.BeginAnimation(OpacityProperty,fadeIn);outgoing.BeginAnimation(OpacityProperty,fadeOut);
        }
        catch(Exception){if(SlideImageA.Source is null&&SlideImageB.Source is null)SlideshowLayer.Background=Brushes.Black;}
    }

    private void ApplySlideshowSettings()
    {
        var configuration=_viewModel.CurrentSlideshowConfiguration;if(configuration is null){TopInfoPanel.Visibility=Visibility.Visible;SongInformationPanel.Visibility=NextSongPanel.Visibility=Visibility.Visible;return;}
        SongInformationPanel.Visibility=configuration.ShowSongInformation?Visibility.Visible:Visibility.Collapsed;NextSongPanel.Visibility=configuration.ShowNextSong?Visibility.Visible:Visibility.Collapsed;TopInfoPanel.Visibility=configuration.ShowSongInformation||configuration.ShowNextSong?Visibility.Visible:Visibility.Collapsed;LyricsPanel.Visibility=configuration.ShowLyrics&&_viewModel.IsLyricsVisible?Visibility.Visible:Visibility.Collapsed;LyricsPanel.VerticalAlignment=configuration.LyricRegionPosition switch{HomeKTV.Core.Models.LyricRegionPosition.Top=>VerticalAlignment.Top,HomeKTV.Core.Models.LyricRegionPosition.Center=>VerticalAlignment.Center,_=>VerticalAlignment.Bottom};LyricsPanel.Margin=configuration.LyricRegionPosition==HomeKTV.Core.Models.LyricRegionPosition.Bottom?new Thickness(0,0,0,54):new Thickness(0,80,0,0);SlideMask.Opacity=Math.Clamp(configuration.ImageMaskOpacity,0,.9);
        var stretch=configuration.FitMode==HomeKTV.Core.Models.SlideshowFitMode.Cover?Stretch.UniformToFill:Stretch.Uniform;SlideImageA.Stretch=SlideImageB.Stretch=stretch;SlideBackground.Visibility=configuration.FitMode==HomeKTV.Core.Models.SlideshowFitMode.ContainBlurBackground?Visibility.Visible:Visibility.Collapsed;if(SlideBackground.Effect is BlurEffect blur)blur.Radius=Math.Clamp(configuration.BackgroundBlurRadius,0,100);
    }

    private bool ShouldDisplayLyrics()
    {
        return HomeKTV.Core.Configuration.LyricsDisplayPolicy.ShouldShowOverlay(_viewModel.Settings.Lyrics,_viewModel.IsLyricsVisible,!string.IsNullOrWhiteSpace(_loadedLyric),_viewModel.CurrentSlideshowConfiguration is not { ShowLyrics:false });
    }

    private void UpdateKaraokeLyrics(KaraokeDisplayFrame frame)
    {
        var empty=_loadedLyric is null?"暂无同步歌词":string.Empty;
        SetLyricLine(TopLyric,frame.TopText.Length==0&&frame.ActiveRow<0?empty:frame.TopText,frame.ActiveRow==0,frame.Progress);
        SetLyricLine(BottomLyric,frame.BottomText,frame.ActiveRow==1,frame.Progress);
    }

    private void SetLyricLine(System.Windows.Controls.TextBlock line,string text,bool active,double progress)
    {
        var settings=_viewModel.Settings.Lyrics;var normal=ParseBrush(settings.FontColor,Brushes.White);var current=ParseBrush(settings.CurrentFontColor,Brushes.DodgerBlue);
        line.Inlines.Clear();line.Foreground=active?current:normal;
        line.Effect=CreateLyricEffect(settings,active);
        if(!active||settings.DisplayMode!=HomeKTV.Core.Models.LyricsDisplayMode.Karaoke||text.Length==0){line.Text=text;return;}
        var highlighted=Math.Clamp((int)Math.Ceiling(text.Length*progress),0,text.Length);
        if(highlighted>0)line.Inlines.Add(new System.Windows.Documents.Run(text[..highlighted]){Foreground=current});
        if(highlighted<text.Length)line.Inlines.Add(new System.Windows.Documents.Run(text[highlighted..]){Foreground=normal});
    }

    private void ApplyLyricsSettings()
    {
        var settings=_viewModel.Settings.Lyrics;
        LyricsPanel.Width=Math.Max(320,ActualWidth-Math.Clamp(settings.HorizontalMargin,0,400)*2);
        LyricsPanel.VerticalAlignment=settings.Position switch{HomeKTV.Core.Models.LyricsOverlayPosition.Top=>VerticalAlignment.Top,HomeKTV.Core.Models.LyricsOverlayPosition.Middle=>VerticalAlignment.Center,HomeKTV.Core.Models.LyricsOverlayPosition.LowerMiddle=>VerticalAlignment.Center,_=>VerticalAlignment.Bottom};
        LyricsPanel.Margin=settings.Position switch{HomeKTV.Core.Models.LyricsOverlayPosition.Top=>new Thickness(0,110,0,0),HomeKTV.Core.Models.LyricsOverlayPosition.LowerMiddle=>new Thickness(0,ActualHeight*.28,0,0),HomeKTV.Core.Models.LyricsOverlayPosition.Middle=>new Thickness(0),_=>new Thickness(0,0,0,54)};
        var family=new FontFamily(settings.FontFamily);TopLyric.FontFamily=BottomLyric.FontFamily=family;
        TopLyric.FontSize=BottomLyric.FontSize=settings.FontSize;
        var outline=ParseColor(settings.OutlineColor,Colors.Black);var effect=settings.ShadowEnabled||settings.OutlineThickness>0?new DropShadowEffect{Color=outline,BlurRadius=Math.Max(settings.ShadowEnabled?8:0,settings.OutlineThickness*2),ShadowDepth=settings.ShadowEnabled?1:0,Opacity=.95}:null;TopLyric.Effect=BottomLyric.Effect=effect;
        LyricsPanel.Background=settings.TranslucentBackground?new SolidColorBrush(Color.FromArgb((byte)Math.Round(255*Math.Clamp(settings.BackgroundOpacity,0,1)),0,0,0)):Brushes.Transparent;
        TopLyric.Margin=new Thickness(0,settings.LineSpacing/2,0,settings.LineSpacing);BottomLyric.Margin=new Thickness(0,settings.LineSpacing,0,settings.LineSpacing/2);
    }

    private static Brush ParseBrush(string value,Brush fallback){try{return new BrushConverter().ConvertFromString(value) as Brush??fallback;}catch(FormatException){return fallback;}catch(NotSupportedException){return fallback;}}
    private static Color ParseColor(string value,Color fallback){try{return (Color)ColorConverter.ConvertFromString(value);}catch(FormatException){return fallback;}catch(NotSupportedException){return fallback;}}
    private static Effect? CreateLyricEffect(HomeKTV.Core.Configuration.LyricsDisplaySettings settings,bool active)
    {
        if(!settings.ShadowEnabled&&settings.OutlineThickness<=0)return null;
        var outline=ParseColor(settings.OutlineColor,Colors.Black);
        return new DropShadowEffect
        {
            Color=outline,
            BlurRadius=Math.Max(1,settings.OutlineThickness*2.5),
            ShadowDepth=active?0:1,
            Opacity=.98
        };
    }

    private void CreateQr()
    {
        QrPanel.Visibility=IdleQrPanel.Visibility=_viewModel.Settings.ShowQrCode&&_viewModel.ServerAddress.StartsWith("http",StringComparison.OrdinalIgnoreCase)?Visibility.Visible:Visibility.Collapsed;if(QrPanel.Visibility!=Visibility.Visible)return;
        using var generator=new QRCodeGenerator();using var data=generator.CreateQrCode(_viewModel.ServerAddress,QRCodeGenerator.ECCLevel.M);using var code=new PngByteQRCode(data);var image=new BitmapImage();using var stream=new MemoryStream(code.GetGraphic(5));image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=stream;image.EndInit();image.Freeze();QrImage.Source=IdleQrImage.Source=image;
    }

    private void LoadIdleBackground()
    {
        try
        {
            var configured=_viewModel.ResolvePortablePath(_viewModel.Settings.IdleBackgroundRelativePath);var path=File.Exists(configured)?configured:Directory.Exists(configured)?Directory.EnumerateFiles(configured).Where(x=>new[]{".jpg",".jpeg",".png",".bmp"}.Contains(Path.GetExtension(x),StringComparer.OrdinalIgnoreCase)).OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).FirstOrDefault():null;
            if(path is null)return;var image=new BitmapImage();using var stream=File.OpenRead(path);image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=stream;image.EndInit();image.Freeze();IdleBackgroundImage.Source=image;
        }
        catch(Exception){IdleBackgroundImage.Source=null;}
    }

    private void DisplaySettingsChanged(object? sender,EventArgs e)=>Dispatcher.Invoke(PlaceOnPreferredScreen);
    private void Window_KeyDown(object sender,System.Windows.Input.KeyEventArgs e)
    {
        var key=e.Key==Key.System?e.SystemKey:e.Key;
        if(HomeKTV.Core.Configuration.LyricsDisplayPolicy.MatchesShortcut(_viewModel.Settings.Lyrics,key.ToString())){_viewModel.ToggleLyrics();e.Handled=true;return;}
        if(key==Key.Escape){WindowStyle=WindowStyle.SingleBorderWindow;ResizeMode=ResizeMode.CanResize;Topmost=false;WindowState=WindowState.Normal;UpdateLayout();var target=Screen.FromHandle(new WindowInteropHelper(this).Handle);var area=target.WorkingArea;var width=Math.Min(1280,area.Width);var height=Math.Min(720,area.Height);SetWindowPos(new WindowInteropHelper(this).Handle,IntPtr.Zero,area.Left+(area.Width-width)/2,area.Top+(area.Height-height)/2,width,height,0x0040);}
    }
    private void OnClosing(object? sender,CancelEventArgs e){if(_closeForShutdown)return;e.Cancel=true;HidePlayer();}
    private void OnClosed(object? sender,EventArgs e){_timer.Stop();SystemEvents.DisplaySettingsChanged-=DisplaySettingsChanged;SlideImageA.Source=SlideImageB.Source=SlideBackground.Source=null;VideoView.MediaPlayer=null;}

    [DllImport("user32.dll",SetLastError=true)]private static extern bool SetWindowPos(IntPtr hWnd,IntPtr hWndInsertAfter,int x,int y,int cx,int cy,uint flags);
}
