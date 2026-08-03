namespace HomeKTV.Lyrics;

public sealed record LrcLine(TimeSpan Timestamp, string Text);

public sealed class LrcDocument
{
    public LrcDocument(IReadOnlyList<LrcLine> lines, IReadOnlyDictionary<string, string> metadata)
    {
        Lines = lines;
        Metadata = metadata;
    }

    public IReadOnlyList<LrcLine> Lines { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }

    public LrcPosition Locate(TimeSpan playbackPosition, int offsetMs = 0)
    {
        var adjusted = playbackPosition + TimeSpan.FromMilliseconds(offsetMs);
        if (Lines.Count == 0) return new(null, null, null, -1);

        var low = 0;
        var high = Lines.Count - 1;
        var index = -1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            if (Lines[middle].Timestamp <= adjusted)
            {
                index = middle;
                low = middle + 1;
            }
            else high = middle - 1;
        }

        return new(
            index > 0 ? Lines[index - 1] : null,
            index >= 0 ? Lines[index] : null,
            index + 1 < Lines.Count ? Lines[index + 1] : index < 0 ? Lines[0] : null,
            index);
    }

    public KaraokeDisplayFrame CreateKaraokeFrame(TimeSpan playbackPosition, int offsetMs = 0)
    {
        var position = Locate(playbackPosition, offsetMs);
        if (Lines.Count == 0) return KaraokeDisplayFrame.Empty;
        if (position.Index < 0)
            return new KaraokeDisplayFrame(Lines[0].Text, Lines.Count > 1 ? Lines[1].Text : string.Empty, -1, 0);

        var current = Lines[position.Index];
        var next = position.Index + 1 < Lines.Count ? Lines[position.Index + 1] : null;
        var activeRow = position.Index % 2;
        var startMs = current.Timestamp.TotalMilliseconds;
        var endMs = next?.Timestamp.TotalMilliseconds ?? startMs + 5000;
        var adjustedMs = playbackPosition.TotalMilliseconds + offsetMs;
        var progress = Math.Clamp((adjustedMs - startMs) / Math.Max(100, endMs - startMs), 0, 1);
        return activeRow == 0
            ? new KaraokeDisplayFrame(current.Text, next?.Text ?? string.Empty, 0, progress)
            : new KaraokeDisplayFrame(next?.Text ?? string.Empty, current.Text, 1, progress);
    }
}

public sealed record LrcPosition(LrcLine? Previous, LrcLine? Current, LrcLine? Next, int Index);
public sealed record KaraokeDisplayFrame(string TopText, string BottomText, int ActiveRow, double Progress)
{
    public static KaraokeDisplayFrame Empty { get; } = new(string.Empty, string.Empty, -1, 0);
}
