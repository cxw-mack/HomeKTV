using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using HomeKTV.Core.Portable;
using HomeKTV.Infrastructure.Media;

namespace HomeKTV.IntegrationTests;

public sealed class SingerPhotoLookupFeatureTests
{
    [Fact]
    public async Task ForcedRetryBypassesRecentMissAndCachesDownloadedPhoto()
    {
        var root=CreateRoot();
        try
        {
            var handler=new SingerPhotoHandler();
            using var client=new HttpClient(handler);
            var service=new SingerPhotoLookupService(new PortablePaths(root),client);

            Assert.Null(await service.LookupAndCacheAsync("周杰伦"));
            Assert.True(service.HasRecentMiss("周杰伦"));
            var requestsAfterMiss=handler.ArtistSearchRequests;

            handler.ReturnArtist=true;
            Assert.Null(await service.LookupAndCacheAsync("周杰伦"));
            Assert.Equal(requestsAfterMiss,handler.ArtistSearchRequests);

            var result=Assert.IsType<SingerPhotoLookupResult>(await service.LookupAndCacheAsync("周杰伦",true));
            Assert.True(File.Exists(new PortablePaths(root).Resolve(result.RelativePath)));
            Assert.False(service.HasRecentMiss("周杰伦"));
            Assert.True(handler.ArtistSearchRequests>requestsAfterMiss);
        }
        finally{TryDelete(root);}
    }

    [Fact]
    public async Task CollaborationNameFallsBackToIndividualSingerSearch()
    {
        var root=CreateRoot();
        try
        {
            var handler=new SingerPhotoHandler{ReturnArtist=true};
            using var client=new HttpClient(handler);
            var service=new SingerPhotoLookupService(new PortablePaths(root),client);

            var result=await service.LookupAndCacheAsync("周杰伦&五月天",true);

            Assert.NotNull(result);
            Assert.True(handler.ArtistSearchRequests>=2);
        }
        finally{TryDelete(root);}
    }

    [Fact]
    public async Task LegacyThirtyDayMissFileDoesNotBlockCurrentLookup()
    {
        var root=CreateRoot();
        try
        {
            var paths=new PortablePaths(root);paths.EnsureDirectories();
            var key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("林俊杰".ToUpperInvariant()))).ToLowerInvariant();
            await File.WriteAllTextAsync(Path.Combine(paths.SingerCovers,key+".miss"),DateTimeOffset.UtcNow.ToString("O"));
            var handler=new SingerPhotoHandler{ReturnArtist=true};
            using var client=new HttpClient(handler);
            var service=new SingerPhotoLookupService(paths,client);

            Assert.False(service.HasRecentMiss("林俊杰"));
            Assert.NotNull(await service.LookupAndCacheAsync("林俊杰"));
        }
        finally{TryDelete(root);}
    }

    private static string CreateRoot()=>Path.Combine(Path.GetTempPath(),"HomeKTV-SingerPhotos-"+Guid.NewGuid().ToString("N"));
    private static void TryDelete(string root){try{if(Directory.Exists(root))Directory.Delete(root,true);}catch(IOException){}catch(UnauthorizedAccessException){}}

    private sealed class SingerPhotoHandler:HttpMessageHandler
    {
        public bool ReturnArtist{get;set;}
        public int ArtistSearchRequests{get;private set;}

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        {
            var uri=request.RequestUri??throw new InvalidOperationException("测试请求缺少 URL。");
            if(uri.Host.Contains("wikidata.org",StringComparison.OrdinalIgnoreCase))return Task.FromResult(Json("{\"search\":[]}"));
            if(uri.Host.Contains("music.163.com",StringComparison.OrdinalIgnoreCase))
            {
                ArtistSearchRequests++;
                var requestedName=Uri.UnescapeDataString(uri.Query.Split('&',StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault(item=>item.StartsWith("?s=",StringComparison.OrdinalIgnoreCase) || item.StartsWith("s=",StringComparison.OrdinalIgnoreCase))?
                    .Split('=',2).ElementAtOrDefault(1)??string.Empty);
                var returnedName=requestedName.Contains('&')?"无关歌手":
                    requestedName.Contains("林俊杰",StringComparison.Ordinal)?"林俊杰":"周杰伦";
                var json=ReturnArtist
                    ?$"{{\"result\":{{\"artists\":[{{\"id\":6452,\"name\":\"{returnedName}\",\"alias\":[\"Jay Chou\"],\"albumSize\":41,\"musicSize\":568,\"img1v1Url\":\"https://images.test/jay.jpg\"}}]}}}}"
                    :"{\"result\":{\"artists\":[]}}";
                return Task.FromResult(Json(json));
            }
            if(uri.Host.Equals("images.test",StringComparison.OrdinalIgnoreCase))
            {
                var content=new ByteArrayContent([0xff,0xd8,0xff,0xd9]);
                content.Headers.ContentType=new MediaTypeHeaderValue("image/jpeg");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=content});
            }
            throw new InvalidOperationException("测试遇到未配置的 URL："+uri);
        }

        private static HttpResponseMessage Json(string value)=>new(HttpStatusCode.OK){Content=new StringContent(value,Encoding.UTF8,"application/json")};
    }
}
