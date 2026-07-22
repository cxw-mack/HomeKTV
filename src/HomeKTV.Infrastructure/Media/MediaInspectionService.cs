using System.Text.Json;
using HomeKTV.Core.Abstractions;
using HomeKTV.Core.Models;
using HomeKTV.Core.Portable;
using HomeKTV.Infrastructure.Data;

namespace HomeKTV.Infrastructure.Media;

public sealed record MediaInspectionReportItem(long SongId,string Title,MediaAvailability Availability,string Details,string? VideoCodec=null,string? AudioCodec=null,int AudioTrackCount=0,string? AudioTracks=null)
{
    public string AvailabilityDisplay=>Availability switch{MediaAvailability.Healthy=>"正常",MediaAvailability.Missing=>"文件缺失",MediaAvailability.Unreadable=>"无法读取",MediaAvailability.NoAudio=>"没有音轨",MediaAvailability.NoLyrics=>"没有歌词",MediaAvailability.Duplicate=>"重复文件",_=>Availability.ToString()};
    public string TechnicalDetails=>string.Join(" · ",new[]{VideoCodec,AudioCodec,AudioTrackCount>0?$"{AudioTrackCount} 音轨":null,AudioTracks}.Where(x=>!string.IsNullOrWhiteSpace(x)));
}

public sealed class MediaInspectionService(PortablePaths paths,HomeKtvDatabase database,ISongRepository songs,FfprobeMediaInspector inspector)
{
    public async Task<IReadOnlyList<MediaInspectionReportItem>> InspectAllAsync(IProgress<(int Current,int Total,string Title)>? progress=null,CancellationToken cancellationToken=default)
    {
        var all=await songs.SearchAsync(null,null,5000,cancellationToken);var results=new List<MediaInspectionReportItem>(all.Count);var current=0;
        foreach(var song in all)
        {
            cancellationToken.ThrowIfCancellationRequested();progress?.Report((++current,all.Count,song.Title));
            var result=await InspectOneAsync(song,cancellationToken);results.Add(result);
        }
        return results;
    }

    public async Task<MediaInspectionReportItem> InspectOneAsync(Song song,CancellationToken cancellationToken=default)
    {
        MediaAvailability availability;string details;MediaProbeResult? probe=null;
        string? absolute=null;
        try{absolute=paths.Resolve(song.VideoRelativePath);}
        catch(Exception exception) when(exception is ArgumentException or InvalidOperationException){availability=MediaAvailability.Missing;details="媒体路径无效："+exception.Message;return await PersistAsync(song,availability,details,null,cancellationToken);}
        if(!File.Exists(absolute)){availability=MediaAvailability.Missing;details="MV 文件不存在";return await PersistAsync(song,availability,details,null,cancellationToken);}
        if(!inspector.IsAvailable)
        {
            availability=!HasReadableLyrics(song)?MediaAvailability.NoLyrics:MediaAvailability.Healthy;
            details="FFprobe 不可用；已完成文件存在性检查";return await PersistAsync(song,availability,details,null,cancellationToken);
        }
        try
        {
            probe=await inspector.InspectAsync(absolute,cancellationToken);
            song.DurationMs=probe.DurationMs;song.Width=probe.Width;song.Height=probe.Height;song.FileSize=probe.FileSize;
            if(probe.AudioTrackCount==0){availability=MediaAvailability.NoAudio;details="媒体不包含音频轨道";}
            else if(!HasReadableLyrics(song)){availability=MediaAvailability.NoLyrics;details="媒体可播放，但没有可用 LRC 歌词";}
            else{availability=MediaAvailability.Healthy;details="媒体与歌词检查通过";}
        }
        catch(Exception exception) when(exception is InvalidDataException or IOException or UnauthorizedAccessException or JsonException)
        {availability=MediaAvailability.Unreadable;details=exception.Message;}
        return await PersistAsync(song,availability,details,probe,cancellationToken);
    }

    private bool HasReadableLyrics(Song song)
    {
        if(string.IsNullOrWhiteSpace(song.LyricRelativePath))return false;
        try{return File.Exists(paths.Resolve(song.LyricRelativePath));}
        catch(Exception exception) when(exception is ArgumentException or InvalidOperationException){song.LyricRelativePath=null;return false;}
    }

    private async Task<MediaInspectionReportItem> PersistAsync(Song song,MediaAvailability availability,string details,MediaProbeResult? probe,CancellationToken cancellationToken)
    {
        song.IsAvailable=availability is MediaAvailability.Healthy or MediaAvailability.NoLyrics;
        await songs.UpsertAsync(song,cancellationToken);
        var serialized=probe is null?details:details+" | "+JsonSerializer.Serialize(new{probe.Format,probe.VideoCodec,probe.AudioCodec,probe.DurationMs,probe.Width,probe.Height,probe.FrameRate,probe.AudioTrackCount,probe.Channels,probe.AudioTracks});
        await database.WriteAsync(async (connection,transaction,ct)=>{var command=connection.CreateCommand();command.Transaction=transaction;command.CommandText="INSERT INTO MediaInspections(SongId,Availability,Details,InspectedAt) VALUES($song,$availability,$details,$now);";command.Parameters.AddWithValue("$song",song.Id);command.Parameters.AddWithValue("$availability",(int)availability);command.Parameters.AddWithValue("$details",serialized);command.Parameters.AddWithValue("$now",DateTimeOffset.UtcNow.ToString("O"));await command.ExecuteNonQueryAsync(ct);return 0;},cancellationToken);
        var tracks=probe is null?null:string.Join("; ",probe.AudioTracks.Select(x=>$"#{x.Index} {x.Codec}/{x.Channels}ch{(string.IsNullOrWhiteSpace(x.Title)?string.Empty:" "+x.Title)}"));
        return new MediaInspectionReportItem(song.Id,song.Title,availability,details,probe?.VideoCodec,probe?.AudioCodec,probe?.AudioTrackCount??0,tracks);
    }
}
