using HomeKTV.Core.Models;
using HomeKTV.Core.Portable;
using HomeKTV.Infrastructure.Configuration;
using HomeKTV.Infrastructure.Data;
using HomeKTV.Infrastructure.Media;

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

    private sealed class TestDatabase : IAsyncDisposable
    {
        private TestDatabase(string root, PortablePaths paths, HomeKtvDatabase database) { Root=root;Paths=paths;Database=database; }
        public string Root { get; } public PortablePaths Paths { get; } public HomeKtvDatabase Database { get; }
        public static async Task<TestDatabase> CreateAsync(){var root=Path.Combine(Path.GetTempPath(),"HomeKTV-Db-"+Guid.NewGuid().ToString("N"));var paths=new PortablePaths(root);var db=new HomeKtvDatabase(paths);await db.InitializeAsync();return new(root,paths,db);}
        public async ValueTask DisposeAsync(){await Database.DisposeAsync();if(Directory.Exists(Root))Directory.Delete(Root,true);}
    }
}

