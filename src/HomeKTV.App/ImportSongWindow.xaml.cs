using System.Windows;
using System.Windows.Controls;

namespace HomeKTV.App;

public partial class ImportSongWindow : Window
{
    public ImportSongWindow(string title,string artist){InitializeComponent();TitleBox.Text=title;ArtistBox.Text=artist;}
    public string SongTitle=>TitleBox.Text.Trim();public string Artist=>ArtistBox.Text.Trim();public string SongLanguage=>(LanguageBox.SelectedItem as ComboBoxItem)?.Content?.ToString()??"其他";
    public int? OriginalTrack=>int.TryParse(OriginalBox.Text,out var value)?value:null;public int? AccompanimentTrack=>int.TryParse(AccompanimentBox.Text,out var value)?value:null;
    private void Cancel_Click(object sender,RoutedEventArgs e){DialogResult=false;Close();}
    private void Import_Click(object sender,RoutedEventArgs e){if(SongTitle.Length==0||Artist.Length==0){MessageBox.Show("请填写歌曲名和歌手。", "信息不完整");return;}DialogResult=true;Close();}
}
