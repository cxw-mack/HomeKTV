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
        var session=await CreateSession(client,"小夏");
        var search=await client.GetFromJsonAsync<Song[]>("api/songs?q=hktk");var song=Assert.Single(search!);
        using var enqueueRequest=Authorized(HttpMethod.Post,"api/queue",session,new { songId=song.Id });
        var enqueue=await client.SendAsync(enqueueRequest);enqueue.EnsureSuccessStatusCode();
        using var stateRequest=Authorized(HttpMethod.Get,"api/state",session);
        var stateResponse=await client.SendAsync(stateRequest);stateResponse.EnsureSuccessStatusCode();
        var state=await stateResponse.Content.ReadFromJsonAsync<JsonElement>();var item=Assert.Single(state.GetProperty("queue").EnumerateArray());
        Assert.True(item.GetProperty("isMine").GetBoolean());Assert.False(item.TryGetProperty("guestSessionId",out _));
    }

    [Fact]
    public async Task SessionTokenCannotBeSpoofedAndQueueDoesNotLeakOwnerCredential()
    {
        await using var fixture=await ServerFixture.CreateAsync();using var client=new HttpClient { BaseAddress=new Uri(fixture.Server.LocalAddress) };
        var first=await CreateSession(client,"甲");var second=await CreateSession(client,"乙");
        using var enqueueRequest=Authorized(HttpMethod.Post,"api/queue",first,new { songId=fixture.SongId });
        var response=await client.SendAsync(enqueueRequest);response.EnsureSuccessStatusCode();var item=await response.Content.ReadFromJsonAsync<QueueItemDto>();
        var publicState=await client.GetStringAsync("api/state");Assert.DoesNotContain(first.Id,publicState,StringComparison.Ordinal);Assert.DoesNotContain(first.AccessToken,publicState,StringComparison.Ordinal);

        using var deniedRequest=Authorized(HttpMethod.Delete,$"api/queue/{item!.Id}",second);
        var denied=await client.SendAsync(deniedRequest);Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);

        using var spoofed=new HttpRequestMessage(HttpMethod.Delete,$"api/queue/{item.Id}");
        spoofed.Headers.Add(HomeKtvWebServer.SessionIdHeader,first.Id);spoofed.Headers.Add(HomeKtvWebServer.SessionTokenHeader,second.AccessToken);
        var unauthorized=await client.SendAsync(spoofed);Assert.Equal(HttpStatusCode.Unauthorized,unauthorized.StatusCode);
    }

    [Fact]
    public async Task AdministratorPinProtectsDestructiveEndpoint()
    {
        await using var fixture=await ServerFixture.CreateAsync();using var client=new HttpClient { BaseAddress=new Uri(fixture.Server.LocalAddress) };
        var denied=await client.DeleteAsync("api/admin/queue");Assert.Equal(HttpStatusCode.Unauthorized,denied.StatusCode);
        using var request=new HttpRequestMessage(HttpMethod.Delete,"api/admin/queue");request.Headers.Add("X-Admin-Pin","2468");var allowed=await client.SendAsync(request);Assert.Equal(HttpStatusCode.NoContent,allowed.StatusCode);
    }

    [Fact]
    public async Task MobileSessionCanToggleLyricsWithoutAdministratorPin()
    {
        await using var fixture=await ServerFixture.CreateAsync();using var client=new HttpClient { BaseAddress=new Uri(fixture.Server.LocalAddress) };
        bool? requested=null;fixture.Server.LyricsVisibilityRequested+=(visible,_)=>{requested=visible;return Task.CompletedTask;};
        var state=await client.GetFromJsonAsync<JsonElement>("api/playback/lyrics");Assert.True(state.GetProperty("visible").GetBoolean());Assert.False(state.GetProperty("available").GetBoolean());
        var denied=await client.PostAsJsonAsync("api/playback/lyrics",new{visible=false});Assert.Equal(HttpStatusCode.Unauthorized,denied.StatusCode);Assert.Null(requested);
        var session=await CreateSession(client,"手机用户");using var allowed=Authorized(HttpMethod.Post,"api/playback/lyrics",session,new{visible=false});var response=await client.SendAsync(allowed);Assert.Equal(HttpStatusCode.NoContent,response.StatusCode);Assert.False(requested);
    }

    [Fact]
    public async Task MobileSessionCanSendPlaybackControlCommands()
    {
        await using var fixture=await ServerFixture.CreateAsync();using var client=new HttpClient { BaseAddress=new Uri(fixture.Server.LocalAddress) };
        await fixture.Server.NotifyPlaybackChangedAsync(new(1,"测试歌曲","HomeKTV","Playing",1200,null,true,true,"Original",true,80));
        PlaybackControlCommand? requested=null;fixture.Server.PlaybackControlRequested+=(command,_)=>{requested=command;return Task.CompletedTask;};
        var denied=await client.PostAsJsonAsync("api/playback/control",new{command="skip"});Assert.Equal(HttpStatusCode.Unauthorized,denied.StatusCode);Assert.Null(requested);
        var session=await CreateSession(client,"手机用户");using var allowed=Authorized(HttpMethod.Post,"api/playback/control",session,new{command="accompaniment"});var response=await client.SendAsync(allowed);Assert.Equal(HttpStatusCode.NoContent,response.StatusCode);Assert.Equal(PlaybackControlCommand.Accompaniment,requested);
    }

    [Fact]
    public async Task MobileSessionCanRestartWithoutAdministratorPin()
    {
        await using var fixture=await ServerFixture.CreateAsync();using var client=new HttpClient { BaseAddress=new Uri(fixture.Server.LocalAddress) };await fixture.Server.NotifyPlaybackChangedAsync(new(1,"测试歌曲","HomeKTV","Playing",1200,null,true,true,"Original",true,80));PlaybackControlCommand? requested=null;fixture.Server.PlaybackControlRequested+=(command,_)=>{requested=command;return Task.CompletedTask;};var session=await CreateSession(client,"iPhone 用户");using var restart=Authorized(HttpMethod.Post,"api/playback/control",session,new{command="restart"});var response=await client.SendAsync(restart);Assert.Equal(HttpStatusCode.NoContent,response.StatusCode);Assert.Equal(PlaybackControlCommand.Restart,requested);
    }

    [Fact]
    public async Task MobileSessionCanAdjustLyricTimingWhenLyricsAreAvailable()
    {
        await using var fixture=await ServerFixture.CreateAsync();using var client=new HttpClient { BaseAddress=new Uri(fixture.Server.LocalAddress) };
        await fixture.Server.NotifyPlaybackChangedAsync(new(1,"测试歌曲","HomeKTV","Playing",1200,null,true,true,"Original",true,80));
        PlaybackControlCommand? requested=null;fixture.Server.PlaybackControlRequested+=(command,_)=>{requested=command;return Task.CompletedTask;};var session=await CreateSession(client,"手机用户");
        using(var decrease=Authorized(HttpMethod.Post,"api/playback/control",session,new{command="lyricsDecreaseFiveSeconds"})){Assert.Equal(HttpStatusCode.NoContent,(await client.SendAsync(decrease)).StatusCode);Assert.Equal(PlaybackControlCommand.LyricsDecreaseFiveSeconds,requested);}
        using var increase=Authorized(HttpMethod.Post,"api/playback/control",session,new{command="lyricsIncreaseFiveSeconds"});Assert.Equal(HttpStatusCode.NoContent,(await client.SendAsync(increase)).StatusCode);Assert.Equal(PlaybackControlCommand.LyricsIncreaseFiveSeconds,requested);
    }

    [Fact]
    public async Task MobilePlaybackControlRejectsUnavailableActionsWithoutAdministratorPin()
    {
        await using var fixture=await ServerFixture.CreateAsync();using var client=new HttpClient { BaseAddress=new Uri(fixture.Server.LocalAddress) };var session=await CreateSession(client,"手机用户");
        using(var idle=Authorized(HttpMethod.Post,"api/playback/control",session,new{command="skip"}))Assert.Equal(HttpStatusCode.Conflict,(await client.SendAsync(idle)).StatusCode);
        await fixture.Server.NotifyPlaybackChangedAsync(new(1,"测试歌曲","HomeKTV","Playing",1200,null,true,true,"Original",false,80));
        using var unavailable=Authorized(HttpMethod.Post,"api/playback/control",session,new{command="accompaniment"});Assert.Equal(HttpStatusCode.Conflict,(await client.SendAsync(unavailable)).StatusCode);
        await fixture.Server.NotifyPlaybackChangedAsync(new(1,"测试歌曲","HomeKTV","Playing",1200,null,true,false,"Original",true,80));
        using var noLyrics=Authorized(HttpMethod.Post,"api/playback/control",session,new{command="lyricsIncreaseFiveSeconds"});Assert.Equal(HttpStatusCode.Conflict,(await client.SendAsync(noLyrics)).StatusCode);
    }

    [Fact]
    public async Task MobileSessionCanAdjustVolumeWithoutAdministratorPin()
    {
        await using var fixture=await ServerFixture.CreateAsync();using var client=new HttpClient { BaseAddress=new Uri(fixture.Server.LocalAddress) };
        int? requested=null;fixture.Server.PlaybackVolumeRequested+=(volume,_)=>{requested=volume;return Task.CompletedTask;};
        var denied=await client.PostAsJsonAsync("api/playback/volume",new{volume=90});Assert.Equal(HttpStatusCode.Unauthorized,denied.StatusCode);Assert.Null(requested);
        var session=await CreateSession(client,"手机用户");using(var invalid=Authorized(HttpMethod.Post,"api/playback/volume",session,new{volume=126}))Assert.Equal(HttpStatusCode.BadRequest,(await client.SendAsync(invalid)).StatusCode);
        using var allowed=Authorized(HttpMethod.Post,"api/playback/volume",session,new{volume=95});var response=await client.SendAsync(allowed);Assert.Equal(HttpStatusCode.NoContent,response.StatusCode);Assert.Equal(95,requested);
    }

    [Fact]
    public async Task LanModeIsReachableThroughAdvertisedIpv4Address()
    {
        var root=Path.Combine(Path.GetTempPath(),"HomeKTV-Lan-"+Guid.NewGuid().ToString("N"));var paths=new PortablePaths(root);paths.EnsureDirectories();await File.WriteAllTextAsync(Path.Combine(paths.Web,"index.html"),"ok");var database=new HomeKtvDatabase(paths);HomeKtvWebServer? server=null;
        try
        {
            await database.InitializeAsync();var settings=new HomeKtvSettings{ServerPort=ServerFixture.GetFreePort(IPAddress.Any),LanModeEnabled=true};server=new HomeKtvWebServer(paths,settings,new SqliteSongRepository(database),new SqliteQueueRepository(database));await server.StartAsync();using var client=new HttpClient{BaseAddress=new Uri(server.LanAddress),Timeout=TimeSpan.FromSeconds(10)};var response=await client.GetAsync("health");Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.DoesNotContain("localhost",server.LanAddress,StringComparison.OrdinalIgnoreCase);
        }
        finally{if(server is not null)await server.DisposeAsync();await database.DisposeAsync();if(Directory.Exists(root))Directory.Delete(root,true);}
    }

    [Fact]
    public void LanAddressCandidatesAreOrderedDistinctIpv4Addresses()
    {
        var addresses=NetworkAddressService.GetLanAddresses();Assert.NotEmpty(addresses);Assert.Equal(addresses[0],NetworkAddressService.GetPreferredLanAddress());Assert.Equal(addresses.Count,addresses.Distinct(StringComparer.Ordinal).Count());Assert.All(addresses,x=>Assert.Equal(System.Net.Sockets.AddressFamily.InterNetwork,IPAddress.Parse(x).AddressFamily));
    }

    private static async Task<GuestSessionGrantDto> CreateSession(HttpClient client,string nickname)
    {
        var response=await client.PostAsJsonAsync("api/session",new{nickname});response.EnsureSuccessStatusCode();return (await response.Content.ReadFromJsonAsync<GuestSessionGrantDto>())!;
    }

    private static HttpRequestMessage Authorized(HttpMethod method,string url,GuestSessionGrantDto session,object? body=null)
    {
        var request=new HttpRequestMessage(method,url);request.Headers.Add(HomeKtvWebServer.SessionIdHeader,session.Id);request.Headers.Add(HomeKtvWebServer.SessionTokenHeader,session.AccessToken);
        if(body is not null)request.Content=JsonContent.Create(body);return request;
    }

    private sealed class ServerFixture : IAsyncDisposable
    {
        private ServerFixture(string root,HomeKtvDatabase database,HomeKtvWebServer server,long songId){Root=root;Database=database;Server=server;SongId=songId;}
        private string Root{get;} private HomeKtvDatabase Database{get;} public HomeKtvWebServer Server{get;} public long SongId{get;}
        public static async Task<ServerFixture> CreateAsync()
        {
            var root=Path.Combine(Path.GetTempPath(),"HomeKTV-Server-"+Guid.NewGuid().ToString("N"));var paths=new PortablePaths(root);paths.EnsureDirectories();await File.WriteAllTextAsync(Path.Combine(paths.Web,"index.html"),"ok");
            var database=new HomeKtvDatabase(paths);await database.InitializeAsync();var songs=new SqliteSongRepository(database);var songId=await songs.UpsertAsync(new Song{Title="海阔天空",ArtistDisplayName="Beyond",PinyinInitials="hktk",VideoRelativePath="Media/MV/test.mp4",FileHash=Guid.NewGuid().ToString("N")});
            var settings=new HomeKtvSettings{ServerPort=GetFreePort(IPAddress.Loopback),LanModeEnabled=false,AdministratorPin="2468"};var server=new HomeKtvWebServer(paths,settings,songs,new SqliteQueueRepository(database));await server.StartAsync();return new(root,database,server,songId);
        }
        public static int GetFreePort(IPAddress address){var listener=new TcpListener(address,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();return port;}
        public async ValueTask DisposeAsync(){await Server.DisposeAsync();await Database.DisposeAsync();if(Directory.Exists(Root))Directory.Delete(Root,true);}
    }
}
