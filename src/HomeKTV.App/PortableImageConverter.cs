using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using HomeKTV.Core.Portable;

namespace HomeKTV.App;

public sealed class PortableImageConverter : IValueConverter
{
    public object Convert(object? value,Type targetType,object? parameter,CultureInfo culture)
    {
        if(value is not string relative||string.IsNullOrWhiteSpace(relative))return DependencyProperty.UnsetValue;
        try
        {
            var path=PortablePaths.FromBaseDirectory().Resolve(relative);if(!File.Exists(path))return DependencyProperty.UnsetValue;
            var image=new BitmapImage();using var stream=File.OpenRead(path);image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.DecodePixelWidth=128;image.StreamSource=stream;image.EndInit();image.Freeze();return image;
        }
        catch(Exception){return DependencyProperty.UnsetValue;}
    }

    public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture)=>throw new NotSupportedException();
}
