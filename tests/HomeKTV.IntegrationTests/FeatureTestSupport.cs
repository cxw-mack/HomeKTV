using System.Diagnostics;
using HomeKTV.Core.Portable;
using HomeKTV.Infrastructure.Data;

namespace HomeKTV.IntegrationTests;

internal sealed class FeatureTestRoot : IAsyncDisposable
{
    private FeatureTestRoot(string root,PortablePaths paths,HomeKtvDatabase database){Root=root;Paths=paths;Database=database;}
    public string Root { get; }
    public PortablePaths Paths { get; }
    public HomeKtvDatabase Database { get; }
    public static string RepositoryRoot
    {
        get
        {
            var directory=new DirectoryInfo(AppContext.BaseDirectory);
            while(directory is not null&&!File.Exists(Path.Combine(directory.FullName,"HomeKTV.sln")))directory=directory.Parent;
            return directory?.FullName??throw new DirectoryNotFoundException("找不到 HomeKTV 仓库根目录。");
        }
    }
    public static string Ffmpeg=>Path.Combine(RepositoryRoot,"runtime-assets","FFmpeg","ffmpeg.exe");
    public static string Ffprobe=>Path.Combine(RepositoryRoot,"runtime-assets","FFmpeg","ffprobe.exe");
    public static async Task<FeatureTestRoot> CreateAsync(bool initializeDatabase=true)
    {
        var root=Path.Combine(Path.GetTempPath(),"HomeKTV-Features-"+Guid.NewGuid().ToString("N"));var paths=new PortablePaths(root);paths.EnsureDirectories();
        var database=new HomeKtvDatabase(paths);if(initializeDatabase)await database.InitializeAsync();return new FeatureTestRoot(root,paths,database);
    }
    public async Task<string> GenerateMediaAsync(string relative,string codecArguments,string duration="1.2",IEnumerable<string>? metadata=null)
    {
        var target=Path.Combine(Root,relative);Directory.CreateDirectory(Path.GetDirectoryName(target)!);var args=new List<string>{"-v","error","-y","-f","lavfi","-i","sine=frequency=440:sample_rate=44100","-t",duration};args.AddRange(codecArguments.Split(' ',StringSplitOptions.RemoveEmptyEntries));if(metadata is not null)foreach(var item in metadata){args.Add("-metadata");args.Add(item);}args.Add(target);await RunAsync(Ffmpeg,args,Root);return target;
    }
    public static async Task<(int ExitCode,string Output,string Error)> RunAsync(string executable,IEnumerable<string> arguments,string workingDirectory)
    {
        var start=new ProcessStartInfo{FileName=executable,WorkingDirectory=workingDirectory,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};foreach(var argument in arguments)start.ArgumentList.Add(argument);using var process=Process.Start(start)??throw new InvalidOperationException("无法启动测试进程。");var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();await process.WaitForExitAsync();var result=(process.ExitCode,await output,await error);if(result.ExitCode!=0)throw new InvalidOperationException(result.Item3);return result;
    }
    public async ValueTask DisposeAsync()
    {
        await Database.DisposeAsync();
        for(var attempt=0;attempt<20;attempt++)
        {
            try{if(Directory.Exists(Root))Directory.Delete(Root,true);return;}
            catch(IOException) when(attempt<19){await Task.Delay(100);}
            catch(UnauthorizedAccessException) when(attempt<19){await Task.Delay(100);}
        }
    }
}
