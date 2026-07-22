using System.Windows;
using HomeKTV.Library;

namespace HomeKTV.App;

public partial class BatchImportWindow : Window
{
    private readonly IReadOnlyList<MediaImportCandidate> _candidates;
    public BatchImportWindow(IReadOnlyList<MediaImportCandidate> candidates)
    {
        InitializeComponent();_candidates=candidates;CandidateGrid.ItemsSource=candidates;CountText.Text=$"识别到 {candidates.Count} 个媒体文件";
    }
    public IReadOnlyList<MediaImportCandidate> SelectedCandidates=>_candidates.Where(x=>x.IsSelected).ToList();
    private void Cancel_Click(object sender,RoutedEventArgs e){DialogResult=false;Close();}
    private void Import_Click(object sender,RoutedEventArgs e){if(SelectedCandidates.Count==0){MessageBox.Show("请至少勾选一首歌曲。","没有选择",MessageBoxButton.OK,MessageBoxImage.Information);return;}DialogResult=true;Close();}
}
