using System.Windows;
using HomeKTV.Library;

namespace HomeKTV.App;

public partial class BatchImportWindow : Window
{
    private readonly IReadOnlyList<MediaImportCandidate> _candidates;

    public BatchImportWindow(IReadOnlyList<MediaImportCandidate> candidates)
    {
        InitializeComponent();
        _candidates = candidates;
        CandidateGrid.ItemsSource = candidates;
        UpdateCount();
    }

    public IReadOnlyList<MediaImportCandidate> SelectedCandidates => _candidates.Where(candidate => candidate.IsSelected).ToList();

    private void SelectAll_Click(object sender, RoutedEventArgs e) => SetSelection(_ => true);
    private void ClearSelection_Click(object sender, RoutedEventArgs e) => SetSelection(_ => false);
    private void SelectComplete_Click(object sender, RoutedEventArgs e) => SetSelection(candidate => candidate.RecognitionStatus == "完整");
    private void CandidateGrid_CurrentCellChanged(object? sender, EventArgs e) => UpdateCount();
    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        CandidateGrid.CommitEdit();
        var selected = SelectedCandidates;
        if (selected.Count == 0)
        {
            MessageBox.Show("请至少勾选一首歌曲。", "没有选择", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (selected.Any(candidate => string.IsNullOrWhiteSpace(candidate.Artist) || string.IsNullOrWhiteSpace(candidate.Title)))
        {
            MessageBox.Show("已勾选歌曲的歌手和歌曲名不能为空。", "信息不完整", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
        Close();
    }

    private void SetSelection(Func<MediaImportCandidate, bool> selector)
    {
        foreach (var candidate in _candidates) candidate.IsSelected = selector(candidate);
        CandidateGrid.Items.Refresh();
        UpdateCount();
    }

    private void UpdateCount()
    {
        var selected = _candidates.Count(candidate => candidate.IsSelected);
        var withAccompaniment = _candidates.Count(candidate => candidate.AccompanimentMediaPath is not null);
        var withLyrics = _candidates.Count(candidate => candidate.LyricPath is not null);
        CountText.Text = $"识别 {_candidates.Count} 首 · 已选 {selected} 首 · 伴奏 {withAccompaniment} 首 · 歌词 {withLyrics} 首";
    }
}
