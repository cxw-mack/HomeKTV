using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using HomeKTV.Core.Configuration;
using HomeKTV.Core.Models;
using HomeKTV.Core.Portable;
using HomeKTV.Infrastructure.Data;
using HomeKTV.Server;

namespace HomeKTV.IntegrationTests;

public sealed class ServerIntegrationTests
{
    [Fact]
    public void PortConflictFallsBackToNextAvailablePort()
    {
        using var occupied=new TcpListener(IPAddress.Loopback,0);occupied.Start();var port=((IPEndPoint)occupied.LocalEndpoint).Port;
        var selected=NetworkAddressService.FindAvailablePort(port,5);
        Assert.NotEqual(port,selected);Assert.InRange(selected,port+1,port+4);
    }

    [Fact]
    public async Task SearchSessionAndOrderingApiWorkEndToEnd()
    {
        await using var fixture=await ServerFixture.CreateAsync();
        using var client=new HttpClient { BaseAddress=new Uri(fixture.Server.LocalAddress) };
        var health=await client.GetAsync("health");Assert.Equal(HttpStatusCode.OK,health.StatusCode);
        var sessionResponse=await client.PostAsJsonAsync("api/session",new { nickname="小夏" });sessionResponse.EnsureSuccessStatusCode();
        var session=await sessionResponse.Content.ReadFromJsonAsync<GuestSessionDto>();Assert.NotNull(session);
        var search=await client.GetFromJsonAsync<Song[]>("api/songs?q=hktk");var song=Assert.Single(search!);
        var enqueue=await client.PostAsJsonAsync("api/queue",new { songId=song.Id,sessionId=session!.Id });enqueue.EnsureSuccessStatusCode();
        var state=await client.GetFromJsonAsync<JsonElement>("api/state");Assert.Single(state.GetProperty("queue").EnumerateArray());
    }

    [Fact]
    public async Task OrdinaryGuestCannotDeleteAnotherGuestsQueueItem()
    {
        await using var fixture=await ServerFixture.CreateAsync();using var client=new HttpClient { BaseAddress=new Uri(fixture.Server.LocalAddress) };
        var first=await CreateSession(client,"甲");var second=await CreateSession(client,"乙");
        var response=await client.PostAsJsonAsync("api/queue",new { songId=fixture.SongId,sessionId=first.Id });var item=await response.Content.ReadFromJsonAsync<QueueItem>();
        var denied=await client.DeleteAsync($"api/queue/{item!.Id}?sessionId={second.Id}");Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
    }

    [Fact]
    public async Task AdministratorPinProtectsDestructiveEndpoint()
    {
        await using var fixture=await ServerFixture.CreateAsync();using var client=new HttpClient { BaseAddress=new Uri(fixture.Server.LocalAddress) };
        var denied=await client.DeleteAsync("api/admin/queue");Assert.Equal(HttpStatusCode.Unauthorized,denied.StatusCode);
        using var request=new HttpRequestMessage(HttpMethod.Delete,"api/admin/queue");request.Headers.Add("X-Admin-Pin","2468");var allowed=await client.SendAsync(request);Assert.Equal(HttpStatusCode.NoContent,allowed.StatusCode);
    }

    private static async Task<GuestSessionDto> CreateSession(HttpClient client,string nickname){var response=await client.PostAsJsonAsync("api/session",new{nickname});return (await response.Content.ReadFromJsonAsync<GuestSessionDto>())!;}

    private sealed class ServerFixture : IAsyncDisposable
    {
        private ServerFixture(string root,HomeKtvDatabase database,HomeKtvWebServer server,long songId){Root=root;Database=database;Server=server;SongId=songId;}
        private string Root{get;} private HomeKtvDatabase Database{get;} public HomeKtvWebServer Server{get;} public long SongId{get;}
        public static async Task<ServerFixture> CreateAsync()
        {
            var root=Path.Combine(Path.GetTempPath(),"HomeKTV-Server-"+Guid.NewGuid().ToString("N"));var paths=new PortablePaths(root);paths.EnsureDirectories();await File.WriteAllTextAsync(Path.Combine(paths.Web,"index.html"),"ok");
            var database=new HomeKtvDatabase(paths);await database.InitializeAsync();var songs=new SqliteSongRepository(database);var songId=await songs.UpsertAsync(new Song{Title="海阔天空",ArtistDisplayName="Beyond",PinyinInitials="hktk",VideoRelativePath="Media/MV/test.mp4",FileHash=Guid.NewGuid().ToString("N")});
            var settings=new HomeKtvSettings{ServerPort=GetFreePort(),LanModeEnabled=false,AdministratorPin="2468"};var server=new HomeKtvWebServer(paths,settings,songs,new SqliteQueueRepository(database));await server.StartAsync();return new(root,database,server,songId);
        }
        private static int GetFreePort(){var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();return port;}
        public async ValueTask DisposeAsync(){await Server.DisposeAsync();await Database.DisposeAsync();if(Directory.Exists(Root))Directory.Delete(Root,true);}
    }
}

