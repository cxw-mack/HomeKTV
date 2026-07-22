using System.IO;
using System.Windows;
using HomeKTV.App.ViewModels;
using HomeKTV.Infrastructure.Media;
using HomeKTV.Library;
using Microsoft.Win32;
using QRCoder;

namespace HomeKTV.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel; private PlayerWindow? _playerWindow; private bool _loaded;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();DataContext=_viewModel=viewModel;
        Loaded+=OnLoaded;Closed+=(_,_)=>_playerWindow?.Close();
        viewModel.ShowQrRequested+=(_,_)=>ShowQr();viewModel.ImportRequested+=async (_,_)=>await ImportAsync();viewModel.RestoreRequested+=async (_,_)=>await RestoreAsync();viewModel.OpenPlayerRequested+=(_,_)=>OpenPlayer();
    }

    private async void OnLoaded(object sender,RoutedEventArgs e)
    {
        if(_loaded)return;_loaded=true;
        try{await _viewModel.InitializeAsync();if(_viewModel.Player is not null)OpenPlayer();}
        catch(Exception exception){MessageBox.Show("HomeKTV 初始化界面失败："+exception.Message,"启动失败",MessageBoxButton.OK,MessageBoxImage.Error);}
    }

    private void OpenPlayer()
    {
        if(_viewModel.Player is null){MessageBox.Show("LibVLC 未正确加载，大屏播放器暂不可用。请检查 Runtime/LibVLC。","播放器不可用",MessageBoxButton.OK,MessageBoxImage.Warning);return;}
        if(_playerWindow is null||!_playerWindow.IsLoaded){_playerWindow=new PlayerWindow(_viewModel);_playerWindow.Closed+=(_,_)=>_playerWindow=null;_playerWindow.Show();}
        else{_playerWindow.Activate();}
    }

    private void ShowQr()
    {
        if(!_viewModel.ServerAddress.StartsWith("http",StringComparison.OrdinalIgnoreCase)){MessageBox.Show(_viewModel.ServerAddress);return;}
        using var generator=new QRCodeGenerator();using var data=generator.CreateQrCode(_viewModel.ServerAddress,QRCodeGenerator.ECCLevel.Q);using var code=new PngByteQRCode(data);
        new QrCodeWindow(_viewModel.ServerAddress,code.GetGraphic(12)).ShowDialog();
    }

    private async Task ImportAsync()
    {
        var dialog=new OpenFileDialog { Title="选择要导入的 MV",Filter="视频文件|*.mp4;*.mkv;*.avi;*.mov;*.m4v;*.webm|所有文件|*.*",Multiselect=false };
        if(dialog.ShowDialog(this)!=true)return;
        var stem=Path.GetFileNameWithoutExtension(dialog.FileName);var artist="未知歌手";var title=stem;
        if(MediaFileNameParser.TryParse(dialog.FileName,out var parsed)){artist=parsed!.Artist;title=parsed.Title;}
        var metadata=new ImportSongWindow(title,artist);if(metadata.ShowDialog()!=true)return;
        var directory=Path.GetDirectoryName(dialog.FileName)!;var lyric=FindCompanion(directory,stem,".lrc");var cover=FindCompanion(directory,stem,".jpg")??FindCompanion(directory,stem,".png");
        await _viewModel.ImportFileAsync(new LocalImportRequest(dialog.FileName,metadata.SongTitle,metadata.Artist,metadata.SongLanguage,LyricPath:lyric,CoverPath:cover,OriginalAudioTrack:metadata.OriginalTrack,AccompanimentAudioTrack:metadata.AccompanimentTrack));
    }

    private async Task RestoreAsync()
    {
        var dialog=new OpenFileDialog{Title="选择 HomeKTV 数据库备份",Filter="SQLite 备份|*.db",InitialDirectory=Path.Combine(AppContext.BaseDirectory,"Data","Backups")};
        if(dialog.ShowDialog(this)==true&&MessageBox.Show("恢复前会自动备份当前数据库。继续吗？","恢复数据库",MessageBoxButton.YesNo,MessageBoxImage.Warning)==MessageBoxResult.Yes)await _viewModel.RestoreDatabaseAsync(dialog.FileName);
    }

    private void PauseButton_Click(object sender,RoutedEventArgs e)=>_viewModel.TogglePauseCommand.Execute(null);
    private static string? FindCompanion(string directory,string stem,string extension){var path=Path.Combine(directory,stem+extension);return File.Exists(path)?path:null;}
}
