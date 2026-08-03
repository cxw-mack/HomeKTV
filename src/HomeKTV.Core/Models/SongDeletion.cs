namespace HomeKTV.Core.Models;

public sealed record SongDeletionPreview(
    Song Song,
    IReadOnlyList<string> RelativePaths,
    string GeneratedDirectoryRelativePath,
    string SlideshowDirectoryRelativePath);

public sealed record SongDeletionResult(
    long SongId,
    string BackupRelativePath,
    IReadOnlyList<string> DeletedRelativePaths,
    IReadOnlyList<string> PreservedSharedRelativePaths,
    IReadOnlyList<string> Warnings);
