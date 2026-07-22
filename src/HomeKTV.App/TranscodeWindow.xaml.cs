using System.Windows;
using System.IO;
using HomeKTV.App.ViewModels;
using HomeKTV.Infrastructure.Media;
using Microsoft.Win32;

namespace HomeKTV.App;

public partial class TranscodeWindow : Window
{
    private readonly MainViewModel _viewModel;private CancellationTokenSource? _cancellation;
    public TranscodeWindow(MainViewModel viewModel){InitializeComponent();_viewModel=viewModel;}
    private void Choose_Click(object sender,RoutedEventArgs e){var dialog=new OpenFileDialog{Filter="视频文件|*.mp4;*.mkv;*.avi;*.mov;*.m4v;*.webm|所有文件|*.*"};if(dialog.ShowDialog(this)==true)PathBox.Text=dialog.FileName;}
    private async void Start_Click(object sender,RoutedEventArgs e)
    {
        if(!File.Exists(PathBox.Text)){MessageBox.Show("请先选择有效媒体文件。");return;}StartButton.IsEnabled=false;CancelButton.Content="取消转码";_cancellation=new CancellationTokenSource();
        try
        {
            var progress=new Progress<TranscodeProgress>(x=>{Progress.IsIndeterminate=x.Percentage is null;if(x.Percentage is not null)Progress.Value=x.Percentage.Value;StatusText.Text=x.Message;});
            var output=await _viewModel.TranscodeAsync(PathBox.Text,KeepOriginalBox.IsChecked!=false,progress,_cancellation.Token);MessageBox.Show("转码完成：\n"+output,"FFmpeg",MessageBoxButton.OK,MessageBoxImage.Information);DialogResult=true;Close();
        }
        catch(OperationCanceledException){StatusText.Text="转码已取消";StartButton.IsEnabled=true;}
        catch(Exception exception){StatusText.Text=exception.Message;MessageBox.Show(exception.Message,"转码失败",MessageBoxButton.OK,MessageBoxImage.Error);StartButton.IsEnabled=true;}
        finally{_cancellation?.Dispose();_cancellation=null;CancelButton.Content="关闭";}
    }
    private void Cancel_Click(object sender,RoutedEventArgs e){if(_cancellation is not null){_cancellation.Cancel();return;}Close();}
}
