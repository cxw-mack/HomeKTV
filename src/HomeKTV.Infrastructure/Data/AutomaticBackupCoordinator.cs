using HomeKTV.Core.Portable;
using Serilog;

namespace HomeKTV.Infrastructure.Data;

public sealed class AutomaticBackupCoordinator(DatabaseBackupService backupService,PortablePaths paths,int intervalHours,ILogger logger) : IAsyncDisposable
{
    private readonly CancellationTokenSource _cancellation=new();
    private Task? _loop;

    public void Start()
    {
        if(_loop is not null)return;_loop=RunAsync(_cancellation.Token);
    }

    public async Task<bool> RunOnceAsync(CancellationToken cancellationToken=default)
    {
        var interval=TimeSpan.FromHours(Math.Clamp(intervalHours,1,24*30));
        var latest=Directory.Exists(paths.Backups)?Directory.EnumerateFiles(paths.Backups,"HomeKTV-*.db").Select(x=>new FileInfo(x)).OrderByDescending(x=>x.LastWriteTimeUtc).FirstOrDefault():null;
        if(latest is not null&&DateTime.UtcNow-latest.LastWriteTimeUtc<interval)return false;
        await backupService.BackupAsync("定时自动备份",cancellationToken);return true;
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await TryRunOnceAsync(cancellationToken);
            using var timer=new PeriodicTimer(TimeSpan.FromMinutes(15));
            while(await timer.WaitForNextTickAsync(cancellationToken))await TryRunOnceAsync(cancellationToken);
        }
        catch(OperationCanceledException)when(cancellationToken.IsCancellationRequested){}
    }

    private async Task TryRunOnceAsync(CancellationToken cancellationToken)
    {
        try{if(await RunOnceAsync(cancellationToken))logger.Information("Automatic database backup completed");}
        catch(OperationCanceledException)when(cancellationToken.IsCancellationRequested){throw;}
        catch(Exception exception){logger.Error(exception,"Automatic database backup failed; the next scheduled attempt will retry");}
    }

    public async ValueTask DisposeAsync()
    {
        _cancellation.Cancel();if(_loop is not null)try{await _loop;}catch(OperationCanceledException){} _cancellation.Dispose();
    }
}
