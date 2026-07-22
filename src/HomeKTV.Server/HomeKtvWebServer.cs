using System.Security.Cryptography;
using System.Text;
using HomeKTV.Core.Abstractions;
using HomeKTV.Core.Configuration;
using HomeKTV.Core.Models;
using HomeKTV.Core.Portable;
using HomeKTV.Core.Queue;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace HomeKTV.Server;

public sealed class HomeKtvWebServer : IAsyncDisposable
{
    private readonly PortablePaths _paths;
    private readonly HomeKtvSettings _settings;
    private readonly ISongRepository _songs;
    private readonly IQueueRepository _queue;
    private readonly GuestSessionRegistry _sessions;
    private readonly PlaybackSnapshotStore _playback;
    private WebApplication? _app;

    public HomeKtvWebServer(PortablePaths paths, HomeKtvSettings settings, ISongRepository songs, IQueueRepository queue, GuestSessionRegistry? sessions=null, PlaybackSnapshotStore? playback=null)
    { _paths=paths;_settings=settings;_songs=songs;_queue=queue;_sessions=sessions??new();_playback=playback??new(); }

    public int Port { get; private set; }
    public string LanAddress => $"http://{NetworkAddressService.GetPreferredLanAddress()}:{Port}";
    public string LocalAddress => $"http://127.0.0.1:{Port}";
    public bool IsRunning => _app is not null;

    public async Task StartAsync(CancellationToken cancellationToken=default)
    {
        if(_app is not null)return;
        Port=NetworkAddressService.FindAvailablePort(_settings.ServerPort);
        var builder=WebApplication.CreateSlimBuilder(new WebApplicationOptions { ApplicationName=typeof(HomeKtvWebServer).Assembly.FullName,ContentRootPath=_paths.Root });
        builder.WebHost.ConfigureKestrel(options=> { if(_settings.LanModeEnabled)options.ListenAnyIP(Port);else options.ListenLocalhost(Port); });
        builder.Services.AddSignalR(options=> { options.MaximumParallelInvocationsPerClient=4;options.MaximumReceiveMessageSize=32*1024; });
        builder.Services.AddSingleton(_songs);builder.Services.AddSingleton(_queue);builder.Services.AddSingleton(_sessions);builder.Services.AddSingleton(_playback);
        var app=builder.Build();
        MapEndpoints(app);
        if(Directory.Exists(_paths.Web))
        {
            var provider=new PhysicalFileProvider(_paths.Web);
            app.UseDefaultFiles(new DefaultFilesOptions { FileProvider=provider });
            app.UseStaticFiles(new StaticFileOptions { FileProvider=provider });
            app.MapFallback(async context => { var index=Path.Combine(_paths.Web,"index.html");if(File.Exists(index)){context.Response.ContentType="text/html; charset=utf-8";await context.Response.SendFileAsync(index);}else{context.Response.StatusCode=404;await context.Response.WriteAsync("手机点歌页面尚未构建。",context.RequestAborted);} });
        }
        _app=app;
        try { await app.StartAsync(cancellationToken); }
        catch { _app=null;await app.DisposeAsync();throw; }
    }

    private void MapEndpoints(WebApplication app)
    {
        app.MapGet("/health",()=>Results.Ok(new { status="ok",port=Port,lan=_settings.LanModeEnabled }));
        app.MapPost("/api/session",(CreateSessionRequest request)=> { try{return Results.Ok(_sessions.Create(request.Nickname));}catch(ArgumentException e){return Results.BadRequest(new{error=e.Message});} });
        app.MapGet("/api/state",async (CancellationToken ct)=>Results.Ok(new { playback=_playback.Current,queue=Order(await _queue.GetActiveAsync(ct)) }));
        app.MapGet("/api/songs",async (string? q,string? language,int? limit,CancellationToken ct)=>Results.Ok(await _songs.SearchAsync(q,language,limit??100,ct)));
        app.MapGet("/api/queue",async (CancellationToken ct)=>Results.Ok(Order(await _queue.GetActiveAsync(ct))));
        app.MapPost("/api/queue",async (EnqueueRequest request,IHubContext<HomeKtvHub> hub,CancellationToken ct)=>
        {
            var session=_sessions.Get(request.SessionId);if(session is null)return Results.Unauthorized();
            if(await _songs.GetAsync(request.SongId,ct) is not { IsAvailable:true })return Results.BadRequest(new{error="歌曲不存在或媒体不可用。"});
            var item=await _queue.EnqueueAsync(request.SongId,session.Id,session.Nickname,ct);await BroadcastQueueAsync(hub,ct);return Results.Ok(item);
        });
        app.MapDelete("/api/queue/{id:long}",async (long id,string sessionId,IHubContext<HomeKtvHub> hub,CancellationToken ct)=>
        { if(_sessions.Get(sessionId) is null)return Results.Unauthorized();var removed=await _queue.RemoveAsync(id,sessionId,false,ct);if(removed)await BroadcastQueueAsync(hub,ct);return removed?Results.NoContent():Results.StatusCode(StatusCodes.Status403Forbidden); });
        app.MapPost("/api/songs/{id:long}/favorite",async (long id,FavoriteRequest request,CancellationToken ct)=>
        { if(_sessions.Get(request.SessionId) is null)return Results.Unauthorized();await _songs.SetFavoriteAsync(id,request.IsFavorite,request.SessionId,ct);return Results.NoContent(); });
        app.MapPost("/api/admin/queue/{id:long}/pin",async (long id,HttpRequest request,IHubContext<HomeKtvHub> hub,CancellationToken ct)=>
        { if(!IsAdmin(request))return Results.Unauthorized();var changed=await _queue.PinAsync(id,ct);if(changed)await BroadcastQueueAsync(hub,ct);return changed?Results.NoContent():Results.NotFound(); });
        app.MapDelete("/api/admin/queue",async (HttpRequest request,IHubContext<HomeKtvHub> hub,CancellationToken ct)=>
        { if(!IsAdmin(request))return Results.Unauthorized();await _queue.ClearAsync(ct);await BroadcastQueueAsync(hub,ct);return Results.NoContent(); });
        app.MapHub<HomeKtvHub>("/hub");
    }

    private IReadOnlyList<QueueItem> Order(IReadOnlyList<QueueItem> items)=>QueuePlanner.Order(items,_settings.QueueOrderingMode);
    private async Task BroadcastQueueAsync(IHubContext<HomeKtvHub> hub,CancellationToken ct)=>await hub.Clients.All.SendAsync("queueChanged",Order(await _queue.GetActiveAsync(ct)),ct);
    private bool IsAdmin(HttpRequest request)
    {
        var supplied=request.Headers["X-Admin-Pin"].ToString();var expected=_settings.AdministratorPin??string.Empty;
        return supplied.Length==expected.Length && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied),Encoding.UTF8.GetBytes(expected));
    }

    public async ValueTask DisposeAsync(){if(_app is null)return;var app=_app;_app=null;try{await app.StopAsync(TimeSpan.FromSeconds(3));}finally{await app.DisposeAsync();}}
}
