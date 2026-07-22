using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using HomeKTV.App.ViewModels;
using HomeKTV.Lyrics;
using Microsoft.Win32;
using QRCoder;
using Screen=System.Windows.Forms.Screen;

namespace HomeKTV.App;

public partial class PlayerWindow : Window
{
    private readonly MainViewModel _viewModel;private readonly DispatcherTimer _timer;private LrcDocument _lyrics=LrcParser.Parse(null);private string? _loadedLyric;private int _displayIndex;
    public PlayerWindow(MainViewModel viewModel)
    {
        InitializeComponent();DataContext=_viewModel=viewModel;_displayIndex=viewModel.Settings.PlaybackDisplayIndex;VideoView.MediaPlayer=viewModel.Player?.MediaPlayer;
        Resources["LyricShadow"]=new DropShadowEffect{BlurRadius=12,ShadowDepth=1,Color=Colors.Black,Opacity=.9};
        _timer=new DispatcherTimer(TimeSpan.FromMilliseconds(100),DispatcherPriority.Render,TimerTick,Dispatcher);_timer.Start();
        Loaded+=(_,_)=>{PlaceOnPreferredScreen();LoadIdleBackground();};Closed+=OnClosed;SystemEvents.DisplaySettingsChanged+=DisplaySettingsChanged;CreateQr();
    }

    private void PlaceOnPreferredScreen()
    {
        var screens=Screen.AllScreens;if(screens.Length==0)return;_displayIndex=Math.Clamp(_displayIndex,0,screens.Length-1);var target=screens[_displayIndex];
        var useFullscreen=_viewModel.Settings.PlaybackFullscreen&&screens.Length>1;
        if(useFullscreen){WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;Topmost=true;WindowState=WindowState.Normal;UpdateLayout();SetWindowPos(new WindowInteropHelper(this).Handle,IntPtr.Zero,target.Bounds.Left,target.Bounds.Top,target.Bounds.Width,target.Bounds.Height,0x0040);}
        else{WindowStyle=WindowStyle.SingleBorderWindow;ResizeMode=ResizeMode.CanResize;Topmost=false;WindowState=WindowState.Normal;UpdateLayout();var area=target.WorkingArea;var width=Math.Min(1280,area.Width);var height=Math.Min(720,area.Height);SetWindowPos(new WindowInteropHelper(this).Handle,IntPtr.Zero,area.Left+(area.Width-width)/2,area.Top+(area.Height-height)/2,width,height,0x0040);}
    }

    public void MoveToNextScreen(){var count=Screen.AllScreens.Length;if(count==0)return;_displayIndex=(_displayIndex+1)%count;_viewModel.Settings.PlaybackDisplayIndex=_displayIndex;PlaceOnPreferredScreen();}

    private async void TimerTick(object? sender,EventArgs e)
    {
        try
        {
            var active=_viewModel.CurrentTitle!="等待点歌";PlaybackOverlay.Visibility=active?Visibility.Visible:Visibility.Collapsed;IdlePanel.Visibility=active?Visibility.Collapsed:Visibility.Visible;
            if(_loadedLyric!=_viewModel.CurrentLyricRelativePath){_loadedLyric=_viewModel.CurrentLyricRelativePath;_lyrics=string.IsNullOrWhiteSpace(_loadedLyric)?LrcParser.Parse(null):await LrcParser.ParseFileAsync(_viewModel.ResolvePortablePath(_loadedLyric));}
            var position=_lyrics.Locate(TimeSpan.FromMilliseconds(_viewModel.PlaybackPositionMs),_viewModel.CurrentLyricOffsetMs);PreviousLyric.Text=position.Previous?.Text??"";CurrentLyric.Text=position.Current?.Text??(_loadedLyric is null?"暂无同步歌词":"");NextLyric.Text=position.Next?.Text??"";
        }
        catch(Exception){CurrentLyric.Text="歌词文件不可读取";PreviousLyric.Text=NextLyric.Text="";}
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
    private void Window_KeyDown(object sender,System.Windows.Input.KeyEventArgs e){if(e.Key==Key.Escape){WindowStyle=WindowStyle.SingleBorderWindow;ResizeMode=ResizeMode.CanResize;Topmost=false;WindowState=WindowState.Normal;UpdateLayout();var target=Screen.FromHandle(new WindowInteropHelper(this).Handle);var area=target.WorkingArea;var width=Math.Min(1280,area.Width);var height=Math.Min(720,area.Height);SetWindowPos(new WindowInteropHelper(this).Handle,IntPtr.Zero,area.Left+(area.Width-width)/2,area.Top+(area.Height-height)/2,width,height,0x0040);}}
    private void OnClosed(object? sender,EventArgs e){_timer.Stop();SystemEvents.DisplaySettingsChanged-=DisplaySettingsChanged;VideoView.MediaPlayer=null;}

    [DllImport("user32.dll",SetLastError=true)]private static extern bool SetWindowPos(IntPtr hWnd,IntPtr hWndInsertAfter,int x,int y,int cx,int cy,uint flags);
}
