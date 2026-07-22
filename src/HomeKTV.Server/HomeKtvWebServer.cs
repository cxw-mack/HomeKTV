using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using HomeKTV.Core.Abstractions;
using HomeKTV.Core.Configuration;
using HomeKTV.Core.Models;
using HomeKTV.Core.Portable;
using HomeKTV.Core.Queue;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace HomeKTV.Server;

public sealed class HomeKtvWebServer : IAsyncDisposable
{
    public const string SessionIdHeader="X-HomeKTV-Session";
    public const string SessionTokenHeader="X-HomeKTV-Token";
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
    public string LanAddress => _settings.LanModeEnabled?$"http://{NetworkAddressService.GetPreferredLanAddress()}:{Port}":LocalAddress;
    public string LocalAddress => $"http://127.0.0.1:{Port}";
    public bool IsRunning => _app is not null;
    public event EventHandler? QueueChanged;

    public async Task NotifyQueueChangedAsync(CancellationToken cancellationToken=default)
    {
        if(_app is null)return;var hub=_app.Services.GetRequiredService<IHubContext<HomeKtvHub>>();await BroadcastQueueAsync(hub,cancellationToken);
    }

    public async Task NotifyPlaybackChangedAsync(PlaybackSnapshot snapshot,CancellationToken cancellationToken=default)
    {
        _playback.Set(snapshot);if(_app is null)return;var hub=_app.Services.GetRequiredService<IHubContext<HomeKtvHub>>();await hub.Clients.All.SendAsync("playbackChanged",snapshot,cancellationToken);
    }

    public async Task StartAsync(CancellationToken cancellationToken=default)
    {
        if(_app is not null)return;
        var nextPort=Math.Clamp(_settings.ServerPort,1024,65535);
        Exception? lastError=null;
        for(var attempt=0;attempt<20&&nextPort<=65535;attempt++)
        {
            Port=NetworkAddressService.FindAvailablePort(nextPort,Math.Min(20-attempt,65536-nextPort),_settings.LanModeEnabled);
            var app=BuildApplication();
            try
            {
                await app.StartAsync(cancellationToken);_app=app;return;
            }
            catch(Exception exception) when(IsAddressInUse(exception))
            {
                lastError=exception;await app.DisposeAsync();nextPort=Port+1;
            }
            catch
            {
                await app.DisposeAsync();throw;
            }
        }
        throw new IOException("手机点歌服务尝试了多个端口，仍无法启动。",lastError);
    }

    private WebApplication BuildApplication()
    {
        var builder=WebApplication.CreateSlimBuilder(new WebApplicationOptions { ApplicationName=typeof(HomeKtvWebServer).Assembly.FullName,ContentRootPath=_paths.Root });
        builder.WebHost.ConfigureKestrel(options=> { if(_settings.LanModeEnabled)options.ListenAnyIP(Port);else options.ListenLocalhost(Port); });
        builder.Services.AddSignalR(options=> { options.MaximumParallelInvocationsPerClient=4;options.MaximumReceiveMessageSize=32*1024; });
        builder.Services.AddSingleton(_songs);builder.Services.AddSingleton(_queue);builder.Services.AddSingleton(_sessions);builder.Services.AddSingleton(_playback);
        var app=builder.Build();MapEndpoints(app);
        if(Directory.Exists(_paths.Web))
        {
            var provider=new PhysicalFileProvider(_paths.Web);
            app.UseDefaultFiles(new DefaultFilesOptions { FileProvider=provider });app.UseStaticFiles(new StaticFileOptions { FileProvider=provider });
            app.MapFallback(async context => { var index=Path.Combine(_paths.Web,"index.html");if(File.Exists(index)){context.Response.ContentType="text/html; charset=utf-8";context.Response.Headers.CacheControl="no-cache";await context.Response.SendFileAsync(index);}else{context.Response.StatusCode=404;await context.Response.WriteAsync("手机点歌页面尚未构建。",context.RequestAborted);} });
        }
        return app;
    }

    private void MapEndpoints(WebApplication app)
    {
        app.MapGet("/health",()=>Results.Ok(new { status="ok",port=Port,lan=_settings.LanModeEnabled }));
        app.MapPost("/api/session",(CreateSessionRequest request)=> { try{return Results.Ok(_sessions.Create(request.Nickname));}catch(ArgumentException e){return Results.BadRequest(new{error=e.Message});}catch(InvalidOperationException e){return Results.Json(new{error=e.Message},statusCode:429);} });
        app.MapGet("/api/state",async (HttpRequest request,CancellationToken ct)=>{var viewer=Authenticate(request);return Results.Ok(new { playback=_playback.Current,queue=MapQueue(await _queue.GetActiveAsync(ct),viewer?.Id) });});
        app.MapGet("/api/songs",async (string? q,string? language,int? limit,CancellationToken ct)=>Results.Ok(await _songs.SearchAsync(q,language,limit??100,ct)));
        app.MapGet("/api/queue",async (HttpRequest request,CancellationToken ct)=>Results.Ok(MapQueue(await _queue.GetActiveAsync(ct),Authenticate(request)?.Id)));
        app.MapPost("/api/queue",async (EnqueueRequest request,HttpRequest http,IHubContext<HomeKtvHub> hub,CancellationToken ct)=>
        {
            var session=Authenticate(http);if(session is null)return Results.Unauthorized();if(!_settings.MobileOrderingEnabled)return Results.StatusCode(StatusCodes.Status403Forbidden);
            if(await _songs.GetAsync(request.SongId,ct) is not { IsAvailable:true })return Results.BadRequest(new{error="歌曲不存在或媒体不可用。"});
            var item=await _queue.EnqueueAsync(request.SongId,session.Id,session.Nickname,ct);await BroadcastQueueAsync(hub,ct);return Results.Ok(QueueItemDto.From(item,session.Id));
        });
        app.MapDelete("/api/queue/{id:long}",async (long id,HttpRequest request,IHubContext<HomeKtvHub> hub,CancellationToken ct)=>
        { var session=Authenticate(request);if(session is null)return Results.Unauthorized();var removed=await _queue.RemoveAsync(id,session.Id,false,ct);if(removed)await BroadcastQueueAsync(hub,ct);return removed?Results.NoContent():Results.StatusCode(StatusCodes.Status403Forbidden); });
        app.MapPost("/api/queue/{id:long}/move",async (long id,MoveQueueRequest body,HttpRequest request,IHubContext<HomeKtvHub> hub,CancellationToken ct)=>
        {var session=Authenticate(request);if(session is null)return Results.Unauthorized();var changed=await _queue.MoveAsync(id,body.Direction,session.Id,false,ct);if(changed)await BroadcastQueueAsync(hub,ct);return changed?Results.NoContent():Results.StatusCode(StatusCodes.Status409Conflict);});
        app.MapPost("/api/songs/{id:long}/favorite",async (long id,FavoriteRequest body,HttpRequest request,CancellationToken ct)=>
        { var session=Authenticate(request);if(session is null)return Results.Unauthorized();if(await _songs.GetAsync(id,ct) is null)return Results.NotFound();await _songs.SetFavoriteAsync(id,body.IsFavorite,session.Id,ct);return Results.NoContent(); });
        app.MapPost("/api/admin/queue/{id:long}/pin",async (long id,bool? pinned,HttpRequest request,IHubContext<HomeKtvHub> hub,CancellationToken ct)=>
        { if(!IsAdmin(request))return Results.Unauthorized();var changed=await _queue.SetPinnedAsync(id,pinned??true,ct);if(changed)await BroadcastQueueAsync(hub,ct);return changed?Results.NoContent():Results.NotFound(); });
        app.MapPost("/api/admin/queue/{id:long}/move",async (long id,MoveQueueRequest body,HttpRequest request,IHubContext<HomeKtvHub> hub,CancellationToken ct)=>
        {if(!IsAdmin(request))return Results.Unauthorized();var changed=await _queue.MoveAsync(id,body.Direction,null,true,ct);if(changed)await BroadcastQueueAsync(hub,ct);return changed?Results.NoContent():Results.StatusCode(StatusCodes.Status409Conflict);});
        app.MapDelete("/api/admin/queue",async (HttpRequest request,IHubContext<HomeKtvHub> hub,CancellationToken ct)=>
        { if(!IsAdmin(request))return Results.Unauthorized();await _queue.ClearAsync(ct);await BroadcastQueueAsync(hub,ct);return Results.NoContent(); });
        app.MapHub<HomeKtvHub>("/hub");
    }

    private IReadOnlyList<QueueItemDto> MapQueue(IReadOnlyList<QueueItem> items,string? viewerSessionId)=>QueuePlanner.Order(items,_settings.QueueOrderingMode).Select(x=>QueueItemDto.From(x,viewerSessionId)).ToList();
    private async Task BroadcastQueueAsync(IHubContext<HomeKtvHub> hub,CancellationToken ct){await hub.Clients.All.SendAsync("queueChanged",ct);QueueChanged?.Invoke(this,EventArgs.Empty);}
    private GuestSessionDto? Authenticate(HttpRequest request)=>_sessions.Authenticate(request.Headers[SessionIdHeader].ToString(),request.Headers[SessionTokenHeader].ToString());
    private bool IsAdmin(HttpRequest request)
    {
        var supplied=request.Headers["X-Admin-Pin"].ToString();var expected=_settings.AdministratorPin?.Trim()??string.Empty;
        if(expected.Length<4||supplied.Length!=expected.Length)return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied),Encoding.UTF8.GetBytes(expected));
    }
    private static bool IsAddressInUse(Exception exception)=>exception is SocketException {SocketErrorCode:SocketError.AddressAlreadyInUse}||exception.InnerException is not null&&IsAddressInUse(exception.InnerException);

    public async ValueTask DisposeAsync(){if(_app is null)return;var app=_app;_app=null;try{await app.StopAsync(TimeSpan.FromSeconds(3));}finally{await app.DisposeAsync();}}
}
