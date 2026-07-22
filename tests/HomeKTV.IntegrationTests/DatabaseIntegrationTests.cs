using HomeKTV.Core.Models;
using HomeKTV.Core.Portable;
using HomeKTV.Infrastructure.Configuration;
using HomeKTV.Infrastructure.Data;
using HomeKTV.Infrastructure.Media;
using Serilog;

namespace HomeKTV.IntegrationTests;

public sealed class DatabaseIntegrationTests
{
    [Fact]
    public async Task NewDatabaseInitializesAndMigrationIsIdempotent()
    {
        await using var fixture = await TestDatabase.CreateAsync();
        await fixture.Database.InitializeAsync();
        Assert.True(File.Exists(fixture.Paths.Database));
        Assert.True(await fixture.Database.QuickCheckAsync());
        await using var connection = await fixture.Database.OpenConnectionAsync();
        var command = connection.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM SchemaMigrations;";
        Assert.Equal(1L, Convert.ToInt64(await command.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task SongRoundTripsAndSearchesByPinyinInitials()
    {
        await using var fixture = await TestDatabase.CreateAsync();
        var repository = new SqliteSongRepository(fixture.Database);
        var id = await repository.UpsertAsync(new Song { Title="海阔天空",ArtistDisplayName="Beyond",Pinyin="haikuotiankong",PinyinInitials="hktk",VideoRelativePath="Media/MV/demo.mp4",FileHash="abcd" });
        var song = Assert.Single(await repository.SearchAsync("hktk"));
        Assert.Equal(id, song.Id); Assert.Equal("Media/MV/demo.mp4", song.VideoRelativePath);
    }

    [Fact]
    public async Task RepositoryRejectsAbsoluteMediaPath()
    {
        await using var fixture = await TestDatabase.CreateAsync();
        var repository = new SqliteSongRepository(fixture.Database);
        await Assert.ThrowsAsync<ArgumentException>(() => repository.UpsertAsync(new Song { Title="x",ArtistDisplayName="y",VideoRelativePath=Path.GetFullPath("x.mp4") }));
    }

    [Fact]
    public async Task RepositoryRejectsMediaPathThatEscapesPortableRoot()
    {
        await using var fixture = await TestDatabase.CreateAsync();
        var repository = new SqliteSongRepository(fixture.Database);
        await Assert.ThrowsAsync<ArgumentException>(() => repository.UpsertAsync(new Song { Title="x",ArtistDisplayName="y",VideoRelativePath="../outside.mp4" }));
    }

    [Fact]
    public async Task QueueOwnerCannotRemoveAnotherGuestsSongButAdministratorCan()
    {
        await using var fixture = await TestDatabase.CreateAsync();
        var songs = new SqliteSongRepository(fixture.Database);
        var songId = await songs.UpsertAsync(new Song { Title="歌",ArtistDisplayName="歌手",VideoRelativePath="Media/MV/a.mp4",FileHash="queue-hash" });
        var queue = new SqliteQueueRepository(fixture.Database);
        var item = await queue.EnqueueAsync(songId, "owner", "小明");
        Assert.False(await queue.RemoveAsync(item.Id, "other", false));
        Assert.True(await queue.RemoveAsync(item.Id, null, true));
    }

    [Fact]
    public async Task QueueOwnerCanOnlyReorderTheirOwnWaitingSongs()
    {
        await using var fixture=await TestDatabase.CreateAsync();var songs=new SqliteSongRepository(fixture.Database);var songId=await songs.UpsertAsync(new Song{Title="queue",ArtistDisplayName="artist",VideoRelativePath="Media/MV/q.mp4",FileHash="move-hash"});var queue=new SqliteQueueRepository(fixture.Database);
        var first=await queue.EnqueueAsync(songId,"owner","A");var other=await queue.EnqueueAsync(songId,"other","B");var second=await queue.EnqueueAsync(songId,"owner","A");Assert.True(await queue.MoveAsync(second.Id,-1,"owner",false));Assert.False(await queue.MoveAsync(other.Id,-1,"owner",false));
        var active=await queue.GetActiveAsync();Assert.Equal(new[]{second.Id,other.Id,first.Id},active.OrderBy(x=>x.Position).Select(x=>x.Id));
    }

    [Fact]
    public async Task InterruptedQueueStatesRecoverToWaiting()
    {
        await using var fixture=await TestDatabase.CreateAsync();var songs=new SqliteSongRepository(fixture.Database);
        var songId=await songs.UpsertAsync(new Song{Title="恢复",ArtistDisplayName="歌手",VideoRelativePath="Media/MV/recover.mp4",FileHash="recover-hash"});
        var queue=new SqliteQueueRepository(fixture.Database);var item=await queue.EnqueueAsync(songId,"owner","小明");await queue.SetStateAsync(item.Id,QueueItemState.Playing);
        Assert.Equal(1,await queue.RecoverInterruptedAsync());var recovered=Assert.Single(await queue.GetActiveAsync());Assert.Equal(QueueItemState.Waiting,recovered.State);
    }

    [Fact]
    public async Task SettingsCorruptionIsPreservedAndRecovered()
    {
        var root=Path.Combine(Path.GetTempPath(),"HomeKTV-Settings-"+Guid.NewGuid().ToString("N"));var paths=new PortablePaths(root);paths.EnsureDirectories();
        try { await File.WriteAllTextAsync(paths.Settings,"{broken");var store=new JsonSettingsStore(paths);var result=await store.LoadAsync();Assert.NotNull(result.RecoveryMessage);Assert.Equal(16888,result.Settings.ServerPort);Assert.Single(Directory.GetFiles(paths.Data,"Settings.corrupt-*.json")); }
        finally { Directory.Delete(root,true); }
    }

    [Fact]
    public async Task Sha256DetectsDuplicateContent()
    {
        var root=Path.Combine(Path.GetTempPath(),"HomeKTV-Hash-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try { var a=Path.Combine(root,"a.bin");var b=Path.Combine(root,"b.bin");await File.WriteAllTextAsync(a,"same");await File.WriteAllTextAsync(b,"same");Assert.Equal(await FileHashService.ComputeSha256Async(a),await FileHashService.ComputeSha256Async(b)); }
        finally { Directory.Delete(root,true); }
    }

    [Fact]
    public async Task BackupIsValidAndRecorded()
    {
        await using var fixture = await TestDatabase.CreateAsync();
        var service = new DatabaseBackupService(fixture.Database, fixture.Paths);
        var path = await service.BackupAsync("test");
        Assert.True(File.Exists(path));
        await using var connection=await fixture.Database.OpenConnectionAsync();var command=connection.CreateCommand();command.CommandText="SELECT COUNT(*) FROM DatabaseBackups;";Assert.Equal(1L,Convert.ToInt64(await command.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task LocalImporterCopiesMediaAndCreatesRelativeSongRecord()
    {
        await using var fixture=await TestDatabase.CreateAsync();var source=Path.Combine(fixture.Root,"source.mp4");await File.WriteAllBytesAsync(source,[0,1,2,3,4]);
        var repository=new SqliteSongRepository(fixture.Database);var importer=new LocalMediaImporter(fixture.Paths,fixture.Database,repository,new FfprobeMediaInspector(Path.Combine(fixture.Root,"missing-ffprobe.exe")));
        var result=await importer.ImportAsync(new LocalImportRequest(source,"测试导入","歌手"));
        Assert.NotNull(result.Song);Assert.False(Path.IsPathRooted(result.Song!.VideoRelativePath));Assert.True(File.Exists(fixture.Paths.Resolve(result.Song.VideoRelativePath)));Assert.Single(await repository.SearchAsync("测试导入"));Assert.Single(await repository.SearchAsync("csdr"));
    }

    [Fact]
    public async Task LocalImporterHonorsConfiguredPortableMediaRoot()
    {
        await using var fixture=await TestDatabase.CreateAsync();var source=Path.Combine(fixture.Root,"custom-source.mp4");await File.WriteAllBytesAsync(source,[4,3,2,1]);var repository=new SqliteSongRepository(fixture.Database);
        var importer=new LocalMediaImporter(fixture.Paths,fixture.Database,repository,new FfprobeMediaInspector(Path.Combine(fixture.Root,"missing-ffprobe.exe")),"LibraryMedia");var result=await importer.ImportAsync(new LocalImportRequest(source,"自定义媒体","歌手"));
        Assert.NotNull(result.Song);Assert.StartsWith("LibraryMedia/MV/",result.Song!.VideoRelativePath,StringComparison.Ordinal);Assert.True(File.Exists(fixture.Paths.Resolve(result.Song.VideoRelativePath)));
    }

    [Fact]
    public async Task LyricOffsetUpdateDoesNotOverwriteSongMetadata()
    {
        await using var fixture=await TestDatabase.CreateAsync();var repository=new SqliteSongRepository(fixture.Database);
        var id=await repository.UpsertAsync(new Song{Title="完整歌曲",ArtistDisplayName="歌手",VideoRelativePath="Media/MV/full.mp4",FileHash="full-hash",DurationMs=12345,Pinyin="wanzhenggequ"});
        await repository.SetLyricOffsetAsync(id,350);var song=await repository.GetAsync(id);Assert.NotNull(song);Assert.Equal(350,song.LyricOffsetMs);Assert.Equal(12345,song.DurationMs);Assert.Equal("full-hash",song.FileHash);
    }

    [Fact]
    public async Task PlaybackHistoryUpdatesPopularityFields()
    {
        await using var fixture=await TestDatabase.CreateAsync();var repository=new SqliteSongRepository(fixture.Database);
        var id=await repository.UpsertAsync(new Song{Title="播放历史",ArtistDisplayName="歌手",VideoRelativePath="Media/MV/history.mp4",FileHash="history-hash"});
        await repository.RecordPlaybackAsync(id,"小夏","Finished");var song=await repository.GetAsync(id);Assert.Equal(1,song!.PlayCount);Assert.NotNull(song.LastPlayedAt);
        await using var connection=await fixture.Database.OpenConnectionAsync();var command=connection.CreateCommand();command.CommandText="SELECT Result FROM PlayHistory WHERE SongId=$id;";command.Parameters.AddWithValue("$id",id);Assert.Equal("Finished",Convert.ToString(await command.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task MediaInspectionPersistsMissingStatusAndDisablesSong()
    {
        await using var fixture=await TestDatabase.CreateAsync();var repository=new SqliteSongRepository(fixture.Database);
        await repository.UpsertAsync(new Song{Title="缺失",ArtistDisplayName="歌手",VideoRelativePath="Media/MV/missing.mp4",FileHash="missing-inspection"});
        var service=new MediaInspectionService(fixture.Paths,fixture.Database,repository,new FfprobeMediaInspector(Path.Combine(fixture.Root,"missing-ffprobe.exe")));
        var result=Assert.Single(await service.InspectAllAsync());Assert.Equal(MediaAvailability.Missing,result.Availability);Assert.False((await repository.GetAsync(result.SongId))!.IsAvailable);
    }

    [Fact]
    public async Task MediaInspectionRepairsUnsafeLegacyLyricPath()
    {
        await using var fixture=await TestDatabase.CreateAsync();var video=Path.Combine(fixture.Paths.Mv,"legacy.mp4");Directory.CreateDirectory(fixture.Paths.Mv);await File.WriteAllBytesAsync(video,[1]);
        await using(var connection=await fixture.Database.OpenConnectionAsync()){var command=connection.CreateCommand();command.CommandText="INSERT INTO Songs(Title,ArtistDisplayName,VideoRelativePath,LyricRelativePath,CreatedAt,UpdatedAt) VALUES('legacy','artist','Media/MV/legacy.mp4','../outside.lrc',$now,$now);";command.Parameters.AddWithValue("$now",DateTimeOffset.UtcNow.ToString("O"));await command.ExecuteNonQueryAsync();}
        var repository=new SqliteSongRepository(fixture.Database);var service=new MediaInspectionService(fixture.Paths,fixture.Database,repository,new FfprobeMediaInspector(Path.Combine(fixture.Root,"missing-ffprobe.exe")));var result=Assert.Single(await service.InspectAllAsync());
        Assert.Equal(MediaAvailability.NoLyrics,result.Availability);Assert.Null((await repository.GetAsync(result.SongId))!.LyricRelativePath);
    }

    [Fact]
    public async Task AutomaticBackupRunsOnlyWhenIntervalIsDue()
    {
        await using var fixture=await TestDatabase.CreateAsync();using var logger=new LoggerConfiguration().CreateLogger();
        await using var coordinator=new AutomaticBackupCoordinator(new DatabaseBackupService(fixture.Database,fixture.Paths),fixture.Paths,24,logger);
        Assert.True(await coordinator.RunOnceAsync());Assert.False(await coordinator.RunOnceAsync());
    }

    [Fact]
    public async Task RestoreRollsDatabaseBackToSelectedValidBackup()
    {
        await using var fixture=await TestDatabase.CreateAsync();var repository=new SqliteSongRepository(fixture.Database);await repository.UpsertAsync(new Song{Title="备份前",ArtistDisplayName="歌手",VideoRelativePath="Media/MV/a.mp4",FileHash="restore-a"});
        var service=new DatabaseBackupService(fixture.Database,fixture.Paths);var backup=await service.BackupAsync("restore-test");await repository.UpsertAsync(new Song{Title="备份后",ArtistDisplayName="歌手",VideoRelativePath="Media/MV/b.mp4",FileHash="restore-b"});
        await service.RestoreAsync(backup);Assert.Empty(await repository.SearchAsync("备份后"));Assert.Single(await repository.SearchAsync("备份前"));
    }

    [Fact]
    public async Task DatabaseValidationRejectsCorruptRecoveryCandidate()
    {
        var path=Path.Combine(Path.GetTempPath(),"HomeKTV-corrupt-"+Guid.NewGuid().ToString("N")+".db");try{await File.WriteAllTextAsync(path,"not a sqlite database");Assert.False(await DatabaseBackupService.IsValidDatabaseAsync(path));}finally{File.Delete(path);}
    }

    [Fact]
    public async Task ManualTranscodeReportsMissingPortableFfmpeg()
    {
        var root=Path.Combine(Path.GetTempPath(),"HomeKTV-transcode-"+Guid.NewGuid().ToString("N"));var paths=new PortablePaths(root);paths.EnsureDirectories();var input=Path.Combine(root,"input.mp4");await File.WriteAllBytesAsync(input,[1]);
        try{var service=new FfmpegTranscodeService(paths);var error=await Assert.ThrowsAsync<FileNotFoundException>(()=>service.TranscodeToH264Async(input,true));Assert.Contains("FFmpeg",error.Message,StringComparison.OrdinalIgnoreCase);}finally{Directory.Delete(root,true);}
    }

    [Fact]
    public async Task MissingMediaFileProducesFriendlyFileError()
    {
        var missing=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"),"missing.mp4");
        await Assert.ThrowsAsync<FileNotFoundException>(()=>FileHashService.ComputeSha256Async(missing));
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private TestDatabase(string root, PortablePaths paths, HomeKtvDatabase database) { Root=root;Paths=paths;Database=database; }
        public string Root { get; } public PortablePaths Paths { get; } public HomeKtvDatabase Database { get; }
        public static async Task<TestDatabase> CreateAsync(){var root=Path.Combine(Path.GetTempPath(),"HomeKTV-Db-"+Guid.NewGuid().ToString("N"));var paths=new PortablePaths(root);var db=new HomeKtvDatabase(paths);await db.InitializeAsync();return new(root,paths,db);}
        public async ValueTask DisposeAsync(){await Database.DisposeAsync();if(Directory.Exists(Root))Directory.Delete(Root,true);}
    }
}
