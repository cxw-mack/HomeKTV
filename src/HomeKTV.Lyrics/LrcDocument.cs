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
}

public sealed record LrcPosition(LrcLine? Previous, LrcLine? Current, LrcLine? Next, int Index);

