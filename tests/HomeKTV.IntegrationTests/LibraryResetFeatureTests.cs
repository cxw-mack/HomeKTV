using HomeKTV.Infrastructure.Data;
using HomeKTV.Infrastructure.Media;

namespace HomeKTV.IntegrationTests;

public sealed class LibraryResetFeatureTests
{
    [Fact]
    public async Task ClearRemovesLibraryDataAndMediaButPreservesApplicationData()
    {
        await using var fixture=await FeatureTestRoot.CreateAsync();
        var now=DateTimeOffset.UtcNow.ToString("O");
        await using(var connection=await fixture.Database.OpenConnectionAsync())
        {
            var command=connection.CreateCommand();
            command.CommandText="""
                INSERT INTO Songs(Title,ArtistDisplayName,VideoRelativePath,AudioRelativePath,LyricRelativePath,CoverRelativePath,CreatedAt,UpdatedAt,FileHash)
                VALUES('清空测试','测试歌手','Media/MV/clear.mp4','Media/Audio/测试歌手/clear.mp3','Media/Lyrics/clear.lrc','Media/Covers/clear.jpg',$now,'','clear-library');
                INSERT INTO Singers(Name) VALUES('测试歌手');
                INSERT INTO SongSingers(SongId,SingerId) VALUES((SELECT Id FROM Songs WHERE FileHash='clear-library'),(SELECT Id FROM Singers WHERE Name='测试歌手'));
                INSERT INTO Favorites(SongId,GuestSessionId,CreatedAt) VALUES((SELECT Id FROM Songs WHERE FileHash='clear-library'),'desktop',$now);
                INSERT INTO PlayHistory(SongId,RequestedBy,PlayedAt,Result) VALUES((SELECT Id FROM Songs WHERE FileHash='clear-library'),'desktop',$now,'Finished');
                INSERT INTO QueueItems(SongId,GuestSessionId,RequestedBy,RequestedAt,Position) VALUES((SELECT Id FROM Songs WHERE FileHash='clear-library'),'desktop','desktop',$now,1);
                INSERT INTO MediaInspections(SongId,Availability,Details,InspectedAt) VALUES((SELECT Id FROM Songs WHERE FileHash='clear-library'),1,'ok',$now);
                INSERT INTO ImportTasks(Source,State,CreatedAt) VALUES('batch-folder','Completed',$now);
                INSERT INTO ApplicationSettings(Key,Value,UpdatedAt) VALUES('keep-me','yes',$now);
                """;
            command.Parameters.AddWithValue("$now",now);
            await command.ExecuteNonQueryAsync();
        }

        var files=new Dictionary<string,byte[]>
        {
            ["Media/MV/clear.mp4"]=[1,2,3],
            ["Media/Audio/测试歌手/clear.mp3"]=[4,5],
            ["Media/Audio/测试歌手/Accompaniment/clear.flac"]=[6],
            ["Media/Lyrics/clear.lrc"]=[7],
            ["Media/Covers/clear.jpg"]=[8],
            ["Media/Covers/Singers/测试歌手.jpg"]=[9],
            ["Media/Generated/1/accompaniment.flac"]=[10],
            ["Media/Slideshows/Songs/1/slide.jpg"]=[11],
            ["Media/Slideshows/Songs/1/slideshow.json"]=[12],
            ["Media/ImportBox/待导入/伴奏.mp3"]=[13],
            ["Data/AiTasks.json"]=[14]
        };
        foreach(var item in files)
        {
            var path=fixture.Paths.Resolve(item.Key);Directory.CreateDirectory(Path.GetDirectoryName(path)!);await File.WriteAllBytesAsync(path,item.Value);
        }
        var preserved=new Dictionary<string,byte[]>
        {
            ["Media/Backgrounds/keep.jpg"]=[21],
            ["Media/Slideshows/Defaults/keep.bmp"]=[22],
            ["Data/Settings.json"]=[23],
            ["Data/Backups/old.db"]=[24],
            ["Models/keep.bin"]=[25]
        };
        foreach(var item in preserved)
        {
            var path=fixture.Paths.Resolve(item.Key);Directory.CreateDirectory(Path.GetDirectoryName(path)!);await File.WriteAllBytesAsync(path,item.Value);
        }

        var backups=new DatabaseBackupService(fixture.Database,fixture.Paths);
        var result=await new LibraryResetService(fixture.Paths,fixture.Database,backups).ClearAsync();

        Assert.Equal(1,result.DeletedSongCount);
        Assert.Equal(files.Count,result.DeletedFileCount);
        Assert.Equal(files.Values.Sum(value=>(long)value.Length),result.DeletedFileBytes);
        Assert.Empty(result.Warnings);
        Assert.True(await DatabaseBackupService.IsValidDatabaseAsync(result.BackupPath));
        foreach(var path in files.Keys)Assert.False(File.Exists(fixture.Paths.Resolve(path)),path);
        foreach(var path in preserved.Keys)Assert.True(File.Exists(fixture.Paths.Resolve(path)),path);
        foreach(var directory in new[]{fixture.Paths.Mv,fixture.Paths.Audio,fixture.Paths.Lyrics,fixture.Paths.Covers,fixture.Paths.Generated,fixture.Paths.SlideshowSongs,fixture.Paths.ImportBox})
            Assert.Empty(Directory.EnumerateFiles(directory,"*",SearchOption.AllDirectories));

        await using var verify=await fixture.Database.OpenConnectionAsync();
        foreach(var table in new[]{"Songs","Singers","SongSingers","Favorites","PlayHistory","QueueItems","MediaInspections","ImportTasks"})
        {
            var command=verify.CreateCommand();command.CommandText=$"SELECT COUNT(*) FROM {table};";
            Assert.Equal(0L,Convert.ToInt64(await command.ExecuteScalarAsync()));
        }
        var keptSetting=verify.CreateCommand();keptSetting.CommandText="SELECT Value FROM ApplicationSettings WHERE Key='keep-me';";Assert.Equal("yes",Convert.ToString(await keptSetting.ExecuteScalarAsync()));
        var categoryCount=verify.CreateCommand();categoryCount.CommandText="SELECT COUNT(*) FROM Categories;";Assert.True(Convert.ToInt64(await categoryCount.ExecuteScalarAsync())>0);
        var trash=fixture.Paths.Resolve("Data/LibraryResetTrash");
        Assert.True(!Directory.Exists(trash)||!Directory.EnumerateFileSystemEntries(trash).Any());
    }

    [Fact]
    public async Task ClearSupportsCustomMediaRoot()
    {
        await using var fixture=await FeatureTestRoot.CreateAsync();
        var custom=fixture.Paths.Resolve("乐库");var media=Path.Combine(custom,"MV","custom.mp4");Directory.CreateDirectory(Path.GetDirectoryName(media)!);await File.WriteAllBytesAsync(media,[1,2]);
        var backups=new DatabaseBackupService(fixture.Database,fixture.Paths);
        var result=await new LibraryResetService(fixture.Paths,fixture.Database,backups,"乐库").ClearAsync();
        Assert.Equal(1,result.DeletedFileCount);
        Assert.False(File.Exists(media));
        Assert.True(Directory.Exists(Path.Combine(custom,"MV")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(custom,"MV")));
    }

    [Fact]
    public async Task ClearNeverMovesPortableRootForUnsafeLegacyPath()
    {
        await using var fixture=await FeatureTestRoot.CreateAsync();
        var sentinel=fixture.Paths.Resolve("keep-at-root.txt");await File.WriteAllTextAsync(sentinel,"keep");
        await using(var connection=await fixture.Database.OpenConnectionAsync())
        {
            var command=connection.CreateCommand();command.CommandText="INSERT INTO Songs(Title,ArtistDisplayName,VideoRelativePath,CreatedAt,UpdatedAt,FileHash) VALUES('unsafe','artist','.',$now,$now,'unsafe-root');";command.Parameters.AddWithValue("$now",DateTimeOffset.UtcNow.ToString("O"));await command.ExecuteNonQueryAsync();
        }
        var result=await new LibraryResetService(fixture.Paths,fixture.Database,new DatabaseBackupService(fixture.Database,fixture.Paths)).ClearAsync();
        Assert.True(File.Exists(sentinel));
        Assert.Contains(result.Warnings,warning=>warning.Contains("为保护程序数据已跳过",StringComparison.Ordinal));
    }
}
