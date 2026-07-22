using System.Windows;
using System.Windows.Controls;
using HomeKTV.App.ViewModels;
using HomeKTV.Infrastructure.Media;

namespace HomeKTV.App;

public partial class UrlImportWindow : Window
{
    private readonly MainViewModel _viewModel;private CancellationTokenSource? _cancellation;
    public UrlImportWindow(MainViewModel viewModel){InitializeComponent();_viewModel=viewModel;}
    private async void Start_Click(object sender,RoutedEventArgs e)
    {
        var title=TitleBox.Text.Trim();var artist=ArtistBox.Text.Trim();if(string.IsNullOrWhiteSpace(UrlBox.Text)||title.Length==0||artist.Length==0){MessageBox.Show("请填写直链 URL、歌曲名和歌手。","信息不完整");return;}
        StartButton.IsEnabled=false;_cancellation=new CancellationTokenSource();CancelButton.Content="取消下载";
        try
        {
            var language=(LanguageBox.SelectedItem as ComboBoxItem)?.Content?.ToString()??"其他";var progress=new Progress<HttpDownloadProgress>(x=>{Progress.IsIndeterminate=x.TotalBytes is null;Progress.Value=x.Percentage;StatusText.Text=x.TotalBytes is null?$"已下载 {x.BytesReceived/1024/1024} MB":$"下载进度 {x.Percentage}%";});
            await _viewModel.ImportUrlAsync(UrlBox.Text.Trim(),title,artist,language,progress,_cancellation.Token);DialogResult=true;Close();
        }
        catch(OperationCanceledException){StatusText.Text="下载已取消";StartButton.IsEnabled=true;}
        catch(Exception exception){StatusText.Text=exception.Message;MessageBox.Show(exception.Message,"直链导入失败",MessageBoxButton.OK,MessageBoxImage.Error);StartButton.IsEnabled=true;}
        finally{_cancellation?.Dispose();_cancellation=null;CancelButton.Content="关闭";}
    }
    private void Cancel_Click(object sender,RoutedEventArgs e){if(_cancellation is not null){_cancellation.Cancel();return;}DialogResult=false;Close();}
}
