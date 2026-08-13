using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HomeKTV.Core.Portable;

namespace HomeKTV.Infrastructure.Media;

public sealed record SingerPhotoLookupResult(
    string SingerName,
    string RelativePath,
    string WikidataId,
    string SourceUrl,
    string License,
    string Attribution);

public sealed class SingerPhotoLookupService
{
    private static readonly HttpClient SharedClient = CreateClient();
    private static readonly TimeSpan MissCacheDuration = TimeSpan.FromDays(1);
    private static readonly string[] ArtistDescriptionMarkers =
    [
        "歌手","歌唱家","音乐家","音乐人","艺人","乐队","乐团","组合","饶舌",
        "singer","musician","artist","rapper","band","vocalist","songwriter"
    ];
    private readonly PortablePaths _paths;
    private readonly HttpClient _client;

    public SingerPhotoLookupService(PortablePaths paths,HttpClient? client=null)
    {
        _paths=paths;
        _client=client??SharedClient;
        Directory.CreateDirectory(paths.SingerCovers);
    }

    public string? GetCachedRelativePath(string singerName)
    {
        var prefix=GetCacheKey(singerName)+".";
        var file=Directory.EnumerateFiles(_paths.SingerCovers,prefix+"*",SearchOption.TopDirectoryOnly)
            .FirstOrDefault(path=>IsImageExtension(Path.GetExtension(path)));
        return file is null?null:_paths.ToRelative(file);
    }

    public bool HasRecentMiss(string singerName)
    {
        var path=GetMissPath(singerName);
        return File.Exists(path)&&File.GetLastWriteTimeUtc(path)>DateTime.UtcNow-MissCacheDuration;
    }

    public Task<SingerPhotoLookupResult?> LookupAndCacheAsync(string singerName,CancellationToken cancellationToken=default)=>
        LookupAndCacheAsync(singerName,false,cancellationToken);

    public async Task<SingerPhotoLookupResult?> LookupAndCacheAsync(string singerName,bool retryRecentMiss,CancellationToken cancellationToken=default)
    {
        var normalized=singerName.Trim();
        if(string.IsNullOrWhiteSpace(normalized)||IsUnknownArtist(normalized))return null;
        var cached=GetCachedRelativePath(normalized);
        if(cached is not null)return new(normalized,cached,string.Empty,string.Empty,string.Empty,string.Empty);
        if(!retryRecentMiss&&HasRecentMiss(normalized))return null;

        try
        {
            SingerPhotoSource? source=null;
            try{source=await FindWikimediaPhotoAsync(normalized,cancellationToken);}
            catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested){throw;}
            catch(Exception exception) when(exception is HttpRequestException or TaskCanceledException or JsonException){ }
            source??=await FindNetEasePhotoAsync(normalized,cancellationToken);
            if(source is null)return MarkMiss(normalized);

            var extension=ExtensionForMime(source.Mime);
            var target=Path.Combine(_paths.SingerCovers,GetCacheKey(normalized)+extension);
            var temporary=target+".downloading";
            try
            {
                using var request=new HttpRequestMessage(HttpMethod.Get,source.ImageUrl);
                if(source.ImageUrl.Contains("music.126.net",StringComparison.OrdinalIgnoreCase))request.Headers.Referrer=new Uri("https://music.163.com/");
                using var response=await _client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,cancellationToken);
                response.EnsureSuccessStatusCode();
                if(response.Content.Headers.ContentLength is > 10*1024*1024)throw new InvalidDataException("歌手头像超过 10 MB 限制。");
                if(response.Content.Headers.ContentType?.MediaType is { } mediaType&&!mediaType.StartsWith("image/",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("歌手头像地址没有返回图片内容。");
                await using(var input=await response.Content.ReadAsStreamAsync(cancellationToken))
                await using(var output=new FileStream(temporary,FileMode.Create,FileAccess.Write,FileShare.None,81920,true))await input.CopyToAsync(output,cancellationToken);
                if(new FileInfo(temporary).Length==0)throw new InvalidDataException("歌手头像下载结果为空。");
                File.Move(temporary,target,true);
            }
            catch
            {
                TryDelete(temporary);TryDelete(target);throw;
            }

            var relative=_paths.ToRelative(target);
            var result=new SingerPhotoLookupResult(normalized,relative,source.EntityId,source.SourceUrl,source.License,source.Attribution);
            await File.WriteAllTextAsync(Path.ChangeExtension(target,".json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}),cancellationToken);
            TryDelete(GetMissPath(normalized));
            TryDelete(Path.Combine(_paths.SingerCovers,GetCacheKey(normalized)+".miss"));
            return result;
        }
        catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested){throw;}
        catch(HttpRequestException){return null;}
        catch(TaskCanceledException){return null;}
    }

    private async Task<SingerPhotoSource?> FindWikimediaPhotoAsync(string singerName,CancellationToken cancellationToken)
    {
        var entity=await FindArtistEntityAsync(singerName,"zh",cancellationToken)
            ??await FindArtistEntityAsync(singerName,"en",cancellationToken);
        if(entity is null)return null;
        var commonsFile=await GetCommonsFileNameAsync(entity.Value.Id,cancellationToken);
        if(string.IsNullOrWhiteSpace(commonsFile))return null;
        var image=await GetCommonsImageAsync(commonsFile,cancellationToken);
        return image is not null&&IsFreeLicense(image.Value.License)
            ?new(entity.Value.Id,image.Value.Url,image.Value.DescriptionUrl,image.Value.Mime,image.Value.License,image.Value.Attribution)
            :null;
    }

    private async Task<SingerPhotoSource?> FindNetEasePhotoAsync(string singerName,CancellationToken cancellationToken)
    {
        foreach(var searchTerm in BuildSearchTerms(singerName))
        {
            var url="https://music.163.com/api/search/get/web?s="+Uri.EscapeDataString(searchTerm)+"&type=100&limit=8&offset=0";
            using var request=new HttpRequestMessage(HttpMethod.Get,url);request.Headers.Referrer=new Uri("https://music.163.com/");
            using var response=await _client.SendAsync(request,cancellationToken);response.EnsureSuccessStatusCode();
            await using var stream=await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document=await JsonDocument.ParseAsync(stream,cancellationToken:cancellationToken);
            if(!document.RootElement.TryGetProperty("result",out var result)||!result.TryGetProperty("artists",out var artists))continue;
            var matches=new List<(int Score,string Id,string ImageUrl)>();
            foreach(var artist in artists.EnumerateArray())
            {
                var name=artist.TryGetProperty("name",out var nameNode)?nameNode.GetString():null;
                var aliases=new List<string>();
                foreach(var propertyName in new[]{"alias","alia"})if(artist.TryGetProperty(propertyName,out var aliasNode)&&aliasNode.ValueKind==JsonValueKind.Array)aliases.AddRange(aliasNode.EnumerateArray().Select(item=>item.GetString()).OfType<string>());
                var primaryExact=!string.IsNullOrWhiteSpace(name)&&NamesMatch(name,searchTerm);
                var aliasExact=aliases.Any(alias=>NamesMatch(alias,searchTerm));
                var primaryApproximate=!primaryExact&&!string.IsNullOrWhiteSpace(name)&&NamesApproximatelyMatch(name,searchTerm);
                var aliasApproximate=!aliasExact&&aliases.Any(alias=>NamesApproximatelyMatch(alias,searchTerm));
                if(!primaryExact&&!aliasExact&&!primaryApproximate&&!aliasApproximate)continue;
                var imageUrl=artist.TryGetProperty("img1v1Url",out var squareNode)?squareNode.GetString():null;
                if(string.IsNullOrWhiteSpace(imageUrl)&&artist.TryGetProperty("picUrl",out var pictureNode))imageUrl=pictureNode.GetString();
                if(string.IsNullOrWhiteSpace(imageUrl))continue;
                var id=artist.TryGetProperty("id",out var idNode)?idNode.ToString():string.Empty;
                var albumSize=artist.TryGetProperty("albumSize",out var albumNode)&&albumNode.TryGetInt32(out var album)?album:0;
                var musicSize=artist.TryGetProperty("musicSize",out var musicNode)&&musicNode.TryGetInt32(out var music)?music:0;
                var nameScore=primaryExact?120:aliasExact?110:primaryApproximate?90:80;
                matches.Add((nameScore+Math.Min(albumSize,50)*2+Math.Min(musicSize,100)/10,id,imageUrl));
            }
            var selected=matches.OrderByDescending(match=>match.Score).FirstOrDefault();
            if(selected.ImageUrl is not null)return new(selected.Id,selected.ImageUrl,$"https://music.163.com/#/artist?id={selected.Id}","image/jpeg","网易云音乐公开艺人图片","网易云音乐");
        }
        return null;
    }

    private async Task<(string Id,string Label)?> FindArtistEntityAsync(string singerName,string language,CancellationToken cancellationToken)
    {
        var url="https://www.wikidata.org/w/api.php?action=wbsearchentities&type=item&limit=10&format=json&language="+language+"&uselang="+language+"&search="+Uri.EscapeDataString(singerName);
        using var response=await _client.GetAsync(url,cancellationToken);response.EnsureSuccessStatusCode();
        await using var stream=await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document=await JsonDocument.ParseAsync(stream,cancellationToken:cancellationToken);
        if(!document.RootElement.TryGetProperty("search",out var results))return null;
        foreach(var item in results.EnumerateArray())
        {
            var label=item.TryGetProperty("label",out var labelNode)?labelNode.GetString():null;
            var description=item.TryGetProperty("description",out var descriptionNode)?descriptionNode.GetString():null;
            var id=item.TryGetProperty("id",out var idNode)?idNode.GetString():null;
            if(id is null||label is null||!NamesMatch(label,singerName)||!IsArtistDescription(description))continue;
            return(id,label);
        }
        return null;
    }

    private async Task<string?> GetCommonsFileNameAsync(string entityId,CancellationToken cancellationToken)
    {
        var url=$"https://www.wikidata.org/w/api.php?action=wbgetentities&ids={Uri.EscapeDataString(entityId)}&props=claims&format=json";
        using var response=await _client.GetAsync(url,cancellationToken);response.EnsureSuccessStatusCode();
        await using var stream=await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document=await JsonDocument.ParseAsync(stream,cancellationToken:cancellationToken);
        if(!document.RootElement.GetProperty("entities").TryGetProperty(entityId,out var entity)||!entity.TryGetProperty("claims",out var claims)||!claims.TryGetProperty("P18",out var images))return null;
        foreach(var claim in images.EnumerateArray())
        {
            if(claim.TryGetProperty("mainsnak",out var snak)&&snak.TryGetProperty("datavalue",out var data)&&data.TryGetProperty("value",out var value))return value.GetString();
        }
        return null;
    }

    private async Task<(string Url,string DescriptionUrl,string Mime,string License,string Attribution)?> GetCommonsImageAsync(string fileName,CancellationToken cancellationToken)
    {
        var title="File:"+fileName;
        var url="https://commons.wikimedia.org/w/api.php?action=query&format=json&formatversion=2&prop=imageinfo&iiprop=url%7Cmime%7Cextmetadata&iiurlwidth=512&titles="+Uri.EscapeDataString(title);
        using var response=await _client.GetAsync(url,cancellationToken);response.EnsureSuccessStatusCode();
        await using var stream=await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document=await JsonDocument.ParseAsync(stream,cancellationToken:cancellationToken);
        var pages=document.RootElement.GetProperty("query").GetProperty("pages");
        var page=pages.EnumerateArray().FirstOrDefault();
        if(!page.TryGetProperty("imageinfo",out var imageInfos))return null;
        var info=imageInfos.EnumerateArray().FirstOrDefault();
        var imageUrl=info.TryGetProperty("thumburl",out var thumb)?thumb.GetString():info.GetProperty("url").GetString();
        if(string.IsNullOrWhiteSpace(imageUrl))return null;
        var descriptionUrl=info.TryGetProperty("descriptionurl",out var description)?description.GetString()??string.Empty:string.Empty;
        var mime=info.TryGetProperty("mime",out var mimeNode)?mimeNode.GetString()??"image/jpeg":"image/jpeg";
        var metadata=info.TryGetProperty("extmetadata",out var metadataNode)?metadataNode:default;
        var license=GetMetadataValue(metadata,"LicenseShortName");
        var attribution=StripHtml(GetMetadataValue(metadata,"Artist"));
        return(imageUrl,descriptionUrl,mime,license,attribution);
    }

    private SingerPhotoLookupResult? MarkMiss(string singerName)
    {
        File.WriteAllText(GetMissPath(singerName),DateTimeOffset.UtcNow.ToString("O"));
        return null;
    }

    private static string GetMetadataValue(JsonElement metadata,string name)=>metadata.ValueKind==JsonValueKind.Object&&metadata.TryGetProperty(name,out var node)&&node.TryGetProperty("value",out var value)?value.GetString()??string.Empty:string.Empty;
    private static bool IsArtistDescription(string? description)=>!string.IsNullOrWhiteSpace(description)&&ArtistDescriptionMarkers.Any(marker=>description.Contains(marker,StringComparison.OrdinalIgnoreCase));
    private static bool NamesMatch(string left,string right)=>NormalizeName(left)==NormalizeName(right);
    private static bool NamesApproximatelyMatch(string left,string right)
    {
        var normalizedLeft=NormalizeName(left);var normalizedRight=NormalizeName(right);
        if(normalizedLeft.Length<2||normalizedRight.Length<2)return false;
        var shorter=Math.Min(normalizedLeft.Length,normalizedRight.Length);var longer=Math.Max(normalizedLeft.Length,normalizedRight.Length);
        return shorter*2>=longer&&(normalizedLeft.Contains(normalizedRight,StringComparison.Ordinal)||normalizedRight.Contains(normalizedLeft,StringComparison.Ordinal));
    }
    private static IEnumerable<string> BuildSearchTerms(string singerName)
    {
        yield return singerName.Trim();
        foreach(var term in Regex.Split(singerName,@"\s*(?:&|\+|、|/|，|,|\bfeat\.?\b|\bft\.?\b)\s*",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant)
                     .Select(value=>value.Trim()).Where(value=>value.Length>0).Distinct(StringComparer.OrdinalIgnoreCase))
            if(!string.Equals(term,singerName.Trim(),StringComparison.OrdinalIgnoreCase))yield return term;
    }
    private static string NormalizeName(string value)=>new(value.Where(character=>char.IsLetterOrDigit(character)).Select(char.ToUpperInvariant).ToArray());
    private static bool IsUnknownArtist(string value)=>value is "未知歌手" or "未知" or "其他"||value.StartsWith("佚名",StringComparison.Ordinal);
    private static bool IsFreeLicense(string value)=>!string.IsNullOrWhiteSpace(value)&&!value.Contains("fair",StringComparison.OrdinalIgnoreCase)&&!value.Contains("non-free",StringComparison.OrdinalIgnoreCase)&&!value.Contains("copyrighted",StringComparison.OrdinalIgnoreCase);
    private static string ExtensionForMime(string mime)=>mime.ToLowerInvariant() switch{"image/png"=>".png","image/webp"=>".webp","image/gif"=>".gif",_=>".jpg"};
    private static bool IsImageExtension(string extension)=>extension is ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif";
    private static string GetCacheKey(string singerName)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(singerName.Trim().ToUpperInvariant()))).ToLowerInvariant();
    private string GetMissPath(string singerName)=>Path.Combine(_paths.SingerCovers,GetCacheKey(singerName)+".miss-v2");
    private static string StripHtml(string value)=>System.Text.RegularExpressions.Regex.Replace(value,"<[^>]+>",string.Empty).Trim();
    private static void TryDelete(string path){try{if(File.Exists(path))File.Delete(path);}catch(IOException){}}
    private static HttpClient CreateClient()
    {
        var client=new HttpClient(new SocketsHttpHandler{AllowAutoRedirect=true,MaxAutomaticRedirections=5,ConnectTimeout=TimeSpan.FromSeconds(8)}){Timeout=TimeSpan.FromSeconds(20)};
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("HomeKTV","1.0"));
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("(singer-photo-cache; Wikimedia Commons)"));
        return client;
    }

    private sealed record SingerPhotoSource(string EntityId,string ImageUrl,string SourceUrl,string Mime,string License,string Attribution);
}
