using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace HomeKTV.App;

public partial class ImportSongWindow : Window
{
    private IReadOnlyList<string> _slideshowImages=[];
    public ImportSongWindow(string title,string artist,string? lyricPath=null){InitializeComponent();TitleBox.Text=title;ArtistBox.Text=artist;LyricPathBox.Text=lyricPath??string.Empty;}
    public string SongTitle=>TitleBox.Text.Trim();public string Artist=>ArtistBox.Text.Trim();public string SongLanguage=>(LanguageBox.SelectedItem as ComboBoxItem)?.Content?.ToString()??"其他";public long CategoryId=>LanguageBox.SelectedIndex+1;
    public int? OriginalTrack=>int.TryParse(OriginalBox.Text,out var value)?value:null;public int? AccompanimentTrack=>int.TryParse(AccompanimentBox.Text,out var value)?value:null;
    public string? AccompanimentMediaPath=>string.IsNullOrWhiteSpace(AccompanimentPathBox.Text)?null:AccompanimentPathBox.Text;
    public string? LyricPath=>string.IsNullOrWhiteSpace(LyricPathBox.Text)?null:LyricPathBox.Text;
    public IReadOnlyList<string> SlideshowImages=>_slideshowImages;
    private void ChooseAccompaniment_Click(object sender,RoutedEventArgs e)
    {
        var dialog=new OpenFileDialog{Title="选择独立伴奏（音频或视频）",Filter="支持的伴奏媒体|*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.mpeg;*.mpg;*.m4v;*.webm;*.mp3;*.flac;*.wav;*.m4a;*.aac;*.ogg;*.opus;*.wma|音频文件|*.mp3;*.flac;*.wav;*.m4a;*.aac;*.ogg;*.opus;*.wma|视频文件|*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.mpeg;*.mpg;*.m4v;*.webm|所有文件|*.*"};
        if(dialog.ShowDialog(this)==true)AccompanimentPathBox.Text=dialog.FileName;
    }
    private void ClearAccompaniment_Click(object sender,RoutedEventArgs e)=>AccompanimentPathBox.Clear();
    private void ChooseLyric_Click(object sender,RoutedEventArgs e)
    {
        var dialog=new OpenFileDialog{Title="选择同步歌词",Filter="同步歌词|*.lrc;*.txt|LRC 歌词|*.lrc|文本歌词|*.txt|所有文件|*.*"};
        if(dialog.ShowDialog(this)==true)LyricPathBox.Text=dialog.FileName;
    }
    private void ClearLyric_Click(object sender,RoutedEventArgs e)=>LyricPathBox.Clear();
    private void ChooseSlideshow_Click(object sender,RoutedEventArgs e)
    {
        var dialog=new OpenFileDialog{Title="选择纯音频幻灯片图片",Filter="支持的图片|*.jpg;*.jpeg;*.png;*.webp;*.bmp|所有文件|*.*",Multiselect=true};
        if(dialog.ShowDialog(this)!=true)return;
        _slideshowImages=dialog.FileNames;SlideshowPathBox.Text=$"已选择 {_slideshowImages.Count} 张图片";
    }
    private void ClearSlideshow_Click(object sender,RoutedEventArgs e){_slideshowImages=[];SlideshowPathBox.Clear();}
    private void Cancel_Click(object sender,RoutedEventArgs e){DialogResult=false;Close();}
    private void Import_Click(object sender,RoutedEventArgs e){if(SongTitle.Length==0||Artist.Length==0){MessageBox.Show("请填写歌曲名和歌手。", "信息不完整");return;}DialogResult=true;Close();}
}
