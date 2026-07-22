using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace HomeKTV.App;

public partial class QrCodeWindow : Window
{
    public QrCodeWindow(string address,byte[] png){InitializeComponent();AddressText.Text=address;var image=new BitmapImage();using var stream=new MemoryStream(png);image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=stream;image.EndInit();image.Freeze();QrImage.Source=image;}
}

