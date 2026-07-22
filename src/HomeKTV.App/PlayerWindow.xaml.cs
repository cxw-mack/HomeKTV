using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HomeKTV.App.ViewModels;
using HomeKTV.Lyrics;
using Microsoft.Win32;
using QRCoder;
using Screen=System.Windows.Forms.Screen;

namespace HomeKTV.App;

public partial class PlayerWindow : Window
{
    private readonly MainViewModel _viewModel;private readonly DispatcherTimer _timer;private LrcDocument _lyrics=LrcParser.Parse(null);private string? _loadedLyric;
    public PlayerWindow(MainViewModel viewModel)
    {
        InitializeComponent();DataContext=_viewModel=viewModel;VideoView.MediaPlayer=viewModel.Player?.MediaPlayer;
        Resources["LyricShadow"]=new DropShadowEffect{BlurRadius=12,ShadowDepth=1,Color=Colors.Black,Opacity=.9};
        _timer=new DispatcherTimer(TimeSpan.FromMilliseconds(100),DispatcherPriority.Render,TimerTick,Dispatcher);_timer.Start();
        Loaded+=(_,_)=>PlaceOnPreferredScreen();Closed+=OnClosed;SystemEvents.DisplaySettingsChanged+=DisplaySettingsChanged;CreateQr();
    }

    private void PlaceOnPreferredScreen()
    {
        var screens=Screen.AllScreens;var hasSecond=screens.Length>1;var index=hasSecond?Math.Clamp(_viewModel.Settings.PlaybackDisplayIndex,1,screens.Length-1):0;var target=screens[index];
        if(hasSecond&&_viewModel.Settings.PlaybackFullscreen){WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;Topmost=true;WindowState=WindowState.Normal;Left=target.Bounds.Left;Top=target.Bounds.Top;Width=target.Bounds.Width;Height=target.Bounds.Height;}
        else{WindowStyle=WindowStyle.SingleBorderWindow;ResizeMode=ResizeMode.CanResize;Topmost=false;WindowState=WindowState.Normal;Width=1280;Height=720;Left=SystemParameters.WorkArea.Left+(SystemParameters.WorkArea.Width-Width)/2;Top=SystemParameters.WorkArea.Top+(SystemParameters.WorkArea.Height-Height)/2;}
    }

    private async void TimerTick(object? sender,EventArgs e)
    {
        try
        {
            if(_loadedLyric!=_viewModel.CurrentLyricRelativePath){_loadedLyric=_viewModel.CurrentLyricRelativePath;_lyrics=string.IsNullOrWhiteSpace(_loadedLyric)?LrcParser.Parse(null):await LrcParser.ParseFileAsync(_viewModel.ResolvePortablePath(_loadedLyric));}
            var position=_lyrics.Locate(TimeSpan.FromMilliseconds(_viewModel.PlaybackPositionMs),_viewModel.CurrentLyricOffsetMs);PreviousLyric.Text=position.Previous?.Text??"";CurrentLyric.Text=position.Current?.Text??(_loadedLyric is null?"暂无同步歌词":"");NextLyric.Text=position.Next?.Text??"";
        }
        catch(IOException){CurrentLyric.Text="歌词文件不可读取";PreviousLyric.Text=NextLyric.Text="";}
    }

    private void CreateQr()
    {
        QrPanel.Visibility=_viewModel.Settings.ShowQrCode&&_viewModel.ServerAddress.StartsWith("http",StringComparison.OrdinalIgnoreCase)?Visibility.Visible:Visibility.Collapsed;if(QrPanel.Visibility!=Visibility.Visible)return;
        using var generator=new QRCodeGenerator();using var data=generator.CreateQrCode(_viewModel.ServerAddress,QRCodeGenerator.ECCLevel.M);using var code=new PngByteQRCode(data);var image=new BitmapImage();using var stream=new MemoryStream(code.GetGraphic(5));image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=stream;image.EndInit();image.Freeze();QrImage.Source=image;
    }

    private void DisplaySettingsChanged(object? sender,EventArgs e)=>Dispatcher.Invoke(PlaceOnPreferredScreen);
    private void Window_KeyDown(object sender,System.Windows.Input.KeyEventArgs e){if(e.Key==Key.Escape){WindowStyle=WindowStyle.SingleBorderWindow;ResizeMode=ResizeMode.CanResize;Topmost=false;WindowState=WindowState.Normal;Width=1280;Height=720;WindowStartupLocation=WindowStartupLocation.CenterScreen;}}
    private void OnClosed(object? sender,EventArgs e){_timer.Stop();SystemEvents.DisplaySettingsChanged-=DisplaySettingsChanged;VideoView.MediaPlayer=null;}
}
