using HomeKTV.Core.Portable;
using Serilog;

namespace HomeKTV.Infrastructure.Data;

public sealed class AutomaticBackupCoordinator(DatabaseBackupService backupService,PortablePaths paths,int intervalHours,ILogger logger) : IAsyncDisposable
{
    private readonly CancellationTokenSource _cancellation=new();
    private readonly object _lifetimeSync=new();
    private Task? _loop;
    private Task? _disposeTask;
    private bool _disposed;

    public void Start()
    {
        lock(_lifetimeSync)
        {
            ObjectDisposedException.ThrowIf(_disposed,this);
            _loop??=Task.Run(()=>RunAsync(_cancellation.Token),CancellationToken.None);
        }
    }

    public async Task<bool> RunOnceAsync(CancellationToken cancellationToken=default)
    {
        var interval=TimeSpan.FromHours(Math.Clamp(intervalHours,1,24*30));
        var latest=Directory.Exists(paths.Backups)?Directory.EnumerateFiles(paths.Backups,"HomeKTV-*.db").Select(x=>new FileInfo(x)).OrderByDescending(x=>x.LastWriteTimeUtc).FirstOrDefault():null;
        if(latest is not null&&DateTime.UtcNow-latest.LastWriteTimeUtc<interval)return false;
        await backupService.BackupAsync("定时自动备份",cancellationToken).ConfigureAwait(false);return true;
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await TryRunOnceAsync(cancellationToken).ConfigureAwait(false);
            using var timer=new PeriodicTimer(TimeSpan.FromMinutes(15));
            while(await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))await TryRunOnceAsync(cancellationToken).ConfigureAwait(false);
        }
        catch(OperationCanceledException)when(cancellationToken.IsCancellationRequested){}
    }

    private async Task TryRunOnceAsync(CancellationToken cancellationToken)
    {
        try{if(await RunOnceAsync(cancellationToken).ConfigureAwait(false))logger.Information("Automatic database backup completed");}
        catch(OperationCanceledException)when(cancellationToken.IsCancellationRequested){throw;}
        catch(Exception exception){logger.Error(exception,"Automatic database backup failed; the next scheduled attempt will retry");}
    }

    public async ValueTask DisposeAsync()
    {
        Task disposeTask;lock(_lifetimeSync){disposeTask=_disposeTask??=DisposeCoreAsync();}await disposeTask.ConfigureAwait(false);
    }

    private async Task DisposeCoreAsync()
    {
        Task? loop;lock(_lifetimeSync){_disposed=true;loop=_loop;}
        _cancellation.Cancel();if(loop is not null)try{await loop.ConfigureAwait(false);}catch(OperationCanceledException){} _cancellation.Dispose();
    }
}
