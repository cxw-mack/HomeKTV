using HomeKTV.Core.Models;
using HomeKTV.Infrastructure.Data;
using HomeKTV.Infrastructure.Media;

namespace HomeKTV.IntegrationTests;

public sealed class SongDeletionFeatureTests
{
    [Fact]
    public async Task CompleteDeletionRemovesDatabaseRelationsAndOwnedFiles()
    {
        await using var fixture=await FeatureTestRoot.CreateAsync();
        var songs=new SqliteSongRepository(fixture.Database);var queue=new SqliteQueueRepository(fixture.Database);
        var song=new Song{Title="删除测试",ArtistDisplayName="HomeKTV",VideoRelativePath="Media/MV/delete.mp4",LyricRelativePath="Media/Lyrics/HomeKTV/delete.lrc",CoverRelativePath="Media/Covers/delete.jpg",OriginalAudioRelativePath="Media/Generated/1/original.flac",VocalAudioRelativePath="Media/Generated/1/vocals.flac",AccompanimentAudioRelativePath="Media/Generated/1/accompaniment.flac",SlideshowDirectoryRelativePath="Media/Slideshows/Songs/1",SlideshowConfigRelativePath="Media/Slideshows/Songs/1/slideshow.json",FileHash="delete-all"};
        await songs.UpsertAsync(song);
        foreach(var relative in new[]{song.VideoRelativePath,song.LyricRelativePath!,song.CoverRelativePath!,song.OriginalAudioRelativePath!,song.VocalAudioRelativePath!,song.AccompanimentAudioRelativePath!,song.SlideshowConfigRelativePath!})
        {
            var absolute=fixture.Paths.Resolve(relative);Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);await File.WriteAllBytesAsync(absolute,[1,2,3]);
        }
        await queue.EnqueueAsync(song.Id,"owner","测试");await songs.SetFavoriteAsync(song.Id,true,"owner");await songs.RecordPlaybackAsync(song.Id,"测试","Finished");

        var result=await new SongDeletionService(fixture.Paths,songs).DeleteAsync(song.Id);

        Assert.Null(await songs.GetAsync(song.Id));Assert.Empty(result.Warnings);Assert.True(File.Exists(fixture.Paths.Resolve(result.BackupRelativePath)));
        foreach(var relative in new[]{song.VideoRelativePath,song.LyricRelativePath!,song.CoverRelativePath!})Assert.False(File.Exists(fixture.Paths.Resolve(relative)));
        Assert.False(Directory.Exists(Path.Combine(fixture.Paths.Generated,song.Id.ToString())));Assert.False(Directory.Exists(Path.Combine(fixture.Paths.SlideshowSongs,song.Id.ToString())));
        await using var connection=await fixture.Database.OpenConnectionAsync();
        foreach(var table in new[]{"Songs","QueueItems","Favorites","PlayHistory"})
        {
            var command=connection.CreateCommand();command.CommandText=$"SELECT COUNT(*) FROM {table} WHERE SongId=$id";if(table=="Songs")command.CommandText="SELECT COUNT(*) FROM Songs WHERE Id=$id";command.Parameters.AddWithValue("$id",song.Id);Assert.Equal(0L,Convert.ToInt64(await command.ExecuteScalarAsync()));
        }
    }

    [Fact]
    public async Task CompleteDeletionPreservesFilesReferencedByAnotherSong()
    {
        await using var fixture=await FeatureTestRoot.CreateAsync();var songs=new SqliteSongRepository(fixture.Database);
        var sharedLyric=Path.Combine(fixture.Paths.Lyrics,"shared.lrc");await File.WriteAllTextAsync(sharedLyric,"[00:00.00]shared");
        var firstVideo=Path.Combine(fixture.Paths.Mv,"first.mp4");var secondVideo=Path.Combine(fixture.Paths.Mv,"second.mp4");await File.WriteAllBytesAsync(firstVideo,[1]);await File.WriteAllBytesAsync(secondVideo,[2]);
        var first=new Song{Title="一",ArtistDisplayName="歌手",VideoRelativePath=fixture.Paths.ToRelative(firstVideo),LyricRelativePath=fixture.Paths.ToRelative(sharedLyric),FileHash="shared-1"};
        var second=new Song{Title="二",ArtistDisplayName="歌手",VideoRelativePath=fixture.Paths.ToRelative(secondVideo),LyricRelativePath=fixture.Paths.ToRelative(sharedLyric),FileHash="shared-2"};
        await songs.UpsertAsync(first);await songs.UpsertAsync(second);

        var result=await new SongDeletionService(fixture.Paths,songs).DeleteAsync(first.Id);

        Assert.True(File.Exists(sharedLyric));Assert.False(File.Exists(firstVideo));Assert.Contains(first.LyricRelativePath!,result.PreservedSharedRelativePaths);Assert.NotNull(await songs.GetAsync(second.Id));
    }

    [Fact]
    public async Task DeletionRejectsLegacyPathOutsidePortableRoot()
    {
        await using var fixture=await FeatureTestRoot.CreateAsync();
        await using(var connection=await fixture.Database.OpenConnectionAsync())
        {
            var command=connection.CreateCommand();command.CommandText="INSERT INTO Songs(Title,ArtistDisplayName,VideoRelativePath,FileHash,CreatedAt,UpdatedAt) VALUES('unsafe','artist','../outside.mp4','unsafe-delete',$now,$now);";command.Parameters.AddWithValue("$now",DateTimeOffset.UtcNow.ToString("O"));await command.ExecuteNonQueryAsync();
        }
        var songs=new SqliteSongRepository(fixture.Database);var song=Assert.Single(await songs.SearchAsync("unsafe"));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>new SongDeletionService(fixture.Paths,songs).InspectAsync(song.Id));
        Assert.NotNull(await songs.GetAsync(song.Id));
    }
}
