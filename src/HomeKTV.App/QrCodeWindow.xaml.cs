using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace HomeKTV.App;

public partial class QrCodeWindow : Window
{
    private readonly Action? _configureAccess;
    public QrCodeWindow(string address,IReadOnlyList<string> addresses,byte[] png,Action? configureAccess=null){InitializeComponent();_configureAccess=configureAccess;ConfigureButton.Visibility=configureAccess is null?Visibility.Collapsed:Visibility.Visible;AddressText.Text=address;var alternatives=addresses.Where(x=>!string.Equals(x,address,StringComparison.OrdinalIgnoreCase)).ToList();FallbackText.Text=alternatives.Count==0?"":$"扫码无响应时，在手机浏览器尝试备用地址：\n{string.Join("\n",alternatives)}";var image=new BitmapImage();using var stream=new MemoryStream(png);image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=stream;image.EndInit();image.Freeze();QrImage.Source=image;}
    private void CopyAddress_Click(object sender,RoutedEventArgs e)=>Clipboard.SetText(AddressText.Text);
    private void ConfigureAccess_Click(object sender,RoutedEventArgs e)=>_configureAccess?.Invoke();
}
