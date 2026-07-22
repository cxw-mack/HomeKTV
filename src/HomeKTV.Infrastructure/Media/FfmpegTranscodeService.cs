using System.Diagnostics;
using System.ComponentModel;
using System.Globalization;
using HomeKTV.Core.Portable;

namespace HomeKTV.Infrastructure.Media;

public sealed record TranscodeProgress(double? Percentage,string Message);

public sealed class FfmpegTranscodeService(PortablePaths paths)
{
    private string FfmpegPath=>Path.Combine(paths.Ffmpeg,"ffmpeg.exe");
    private string FfprobePath=>Path.Combine(paths.Ffmpeg,"ffprobe.exe");
    public bool IsAvailable=>File.Exists(FfmpegPath);

    public async Task<string> TranscodeToH264Async(string inputPath,bool keepOriginal,IProgress<TranscodeProgress>? progress=null,CancellationToken cancellationToken=default)
    {
        if(!File.Exists(FfmpegPath))throw new FileNotFoundException("未找到便携版 FFmpeg。",FfmpegPath);
        if(!File.Exists(inputPath))throw new FileNotFoundException("找不到待转码媒体。",inputPath);
        var durationMs=0L;if(File.Exists(FfprobePath))try{durationMs=(await new FfprobeMediaInspector(FfprobePath).InspectAsync(inputPath,cancellationToken)).DurationMs;}catch(InvalidDataException){}
        var directory=Path.GetDirectoryName(inputPath)!;var stem=Path.GetFileNameWithoutExtension(inputPath);var inputIsMp4=string.Equals(Path.GetExtension(inputPath),".mp4",StringComparison.OrdinalIgnoreCase);
        var output=keepOriginal?UniqueTarget(directory,stem+" - H264",".mp4"):inputIsMp4?inputPath:UniqueTarget(directory,stem,".mp4");var temporary=output+"."+Guid.NewGuid().ToString("N")+".transcoding.mp4";
        try
        {
            var start=new ProcessStartInfo{FileName=FfmpegPath,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(var argument in new[]{"-hide_banner","-y","-i",inputPath,"-map","0:v:0","-map","0:a?","-c:v","libx264","-preset","medium","-crf","20","-c:a","aac","-b:a","192k","-movflags","+faststart","-progress","pipe:1","-nostats",temporary})start.ArgumentList.Add(argument);
            using var process=Process.Start(start)??throw new InvalidOperationException("无法启动 FFmpeg。");
            using var registration=cancellationToken.Register(()=>TryKill(process));
            var errorTask=process.StandardError.ReadToEndAsync(cancellationToken);string? line;
            while((line=await process.StandardOutput.ReadLineAsync(cancellationToken)) is not null)
            {
                if((line.StartsWith("out_time_us=",StringComparison.Ordinal)||line.StartsWith("out_time_ms=",StringComparison.Ordinal))&&long.TryParse(line[(line.IndexOf('=')+1)..],NumberStyles.Integer,CultureInfo.InvariantCulture,out var microseconds))
                {double? percentage=durationMs>0?Math.Clamp(microseconds/1000d/durationMs*100d,0d,100d):null;progress?.Report(new TranscodeProgress(percentage,percentage is null?"正在转码…":$"正在转码 {percentage:0}%"));}
            }
            await process.WaitForExitAsync(cancellationToken);var error=await errorTask;if(process.ExitCode!=0)throw new InvalidDataException("FFmpeg 转码失败："+error.Trim());
            cancellationToken.ThrowIfCancellationRequested();if(!File.Exists(temporary)||new FileInfo(temporary).Length==0)throw new InvalidDataException("FFmpeg 未生成有效输出文件。");
            File.Move(temporary,output,true);
            if(!keepOriginal&&!string.Equals(inputPath,output,StringComparison.OrdinalIgnoreCase))try{File.Delete(inputPath);}catch{HttpMediaDownloadService.TryDelete(output);throw;}
            progress?.Report(new TranscodeProgress(100,"转码完成"));return output;
        }
        catch{HttpMediaDownloadService.TryDelete(temporary);throw;}
    }

    private static string UniqueTarget(string directory,string stem,string extension){var target=Path.Combine(directory,stem+extension);var index=2;while(File.Exists(target))target=Path.Combine(directory,$"{stem} ({index++}){extension}");return target;}
    private static void TryKill(Process process){try{if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){}catch(Win32Exception){}}
}
