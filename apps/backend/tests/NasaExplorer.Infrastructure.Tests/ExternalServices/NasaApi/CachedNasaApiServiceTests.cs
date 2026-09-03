using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NasaExplorer.Domain.Models.Nasa;
using NasaExplorer.Infrastructure.ExternalServices.NasaApi;
using System.Net;
using System.Text;

namespace NasaExplorer.Infrastructure.Tests.ExternalServices.NasaApi;

public sealed class CachedNasaApiServiceTests
{
    [Fact]
    public async Task SearchImagesAsync_returns_cached_result_for_repeated_criteria()
    {
        CountingHttpMessageHandler handler = new(SearchResponseJson);
        CachedNasaApiService service = CreateService(handler);
        NasaSearchCriteria criteria = new(
            "mars",
            null,
            null,
            "Curiosity",
            "Mastcam",
            null,
            1,
            24);

        NasaSearchResult firstResult = await service.SearchImagesAsync(criteria);
        NasaSearchResult secondResult = await service.SearchImagesAsync(criteria);

        Assert.Equal(firstResult.TotalHits, secondResult.TotalHits);
        Assert.Equal(firstResult.Page, secondResult.Page);
        Assert.Equal(firstResult.PageSize, secondResult.PageSize);
        Assert.Equal(
            firstResult.Images.Select(image => image.NasaImageId),
            secondResult.Images.Select(image => image.NasaImageId));
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task GetAssetFilesAsync_returns_cached_manifest_for_repeated_image()
    {
        CountingHttpMessageHandler handler = new(AssetResponseJson);
        CachedNasaApiService service = CreateService(handler);

        IReadOnlyCollection<NasaAssetFile> firstResult = await service.GetAssetFilesAsync("NHQ201906010007");
        IReadOnlyCollection<NasaAssetFile> secondResult = await service.GetAssetFilesAsync("NHQ201906010007");

        Assert.Equal(firstResult, secondResult);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task SearchImagesAsync_treats_corrupt_cache_json_as_a_miss()
    {
        CountingHttpMessageHandler handler = new(SearchResponseJson);
        CachedNasaApiService service = CreateService(handler, new CorruptDistributedCache());

        NasaSearchResult result = await service.SearchImagesAsync(new NasaSearchCriteria(
            "mars",
            null,
            null,
            null,
            null,
            null,
            1,
            24));

        Assert.Single(result.Images);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task SearchImagesAsync_propagates_caller_cancellation_from_cache()
    {
        CountingHttpMessageHandler handler = new(SearchResponseJson);
        CachedNasaApiService service = CreateService(handler, new CorruptDistributedCache());
        using CancellationTokenSource cancellationSource = new();
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SearchImagesAsync(
            new NasaSearchCriteria("mars", null, null, null, null, null, 1, 24),
            cancellationSource.Token));
        Assert.Equal(0, handler.RequestCount);
    }

    private static CachedNasaApiService CreateService(
        CountingHttpMessageHandler handler,
        IDistributedCache? cache = null)
    {
        NasaApiService innerService = new(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://images-api.nasa.gov/")
        });

        return new CachedNasaApiService(
            innerService,
            cache ?? new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
            Options.Create(new NasaApiOptions
            {
                SearchCacheMinutes = 15,
                AssetManifestCacheHours = 12
            }),
            NullLogger<CachedNasaApiService>.Instance);
    }

    private const string SearchResponseJson = """
        {
          "collection": {
            "items": [
              {
                "data": [
                  {
                    "center": "JPL",
                    "date_created": "2024-05-17T00:00:00Z",
                    "description": "Curiosity Mastcam terrain.",
                    "keywords": ["Mars", "Curiosity", "Mastcam"],
                    "media_type": "image",
                    "nasa_id": "mars-1",
                    "title": "Mars Terrain"
                  }
                ],
                "links": [
                  {
                    "href": "https://images-assets.nasa.gov/image/mars-1/mars-1~thumb.jpg",
                    "rel": "preview",
                    "render": "image"
                  }
                ]
              }
            ],
            "metadata": {
              "total_hits": 1
            }
          }
        }
        """;

    private const string AssetResponseJson = """
        {
          "collection": {
            "items": [
              {
                "href": "http://images-assets.nasa.gov/image/NHQ201906010007/NHQ201906010007~large.jpg"
              }
            ]
          }
        }
        """;

    private sealed class CountingHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _responseJson;

        public CountingHttpMessageHandler(string responseJson)
        {
            _responseJson = responseJson;
        }

        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseJson, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class CorruptDistributedCache : IDistributedCache
    {
        private static readonly byte[] CorruptJson = Encoding.UTF8.GetBytes("not-json");

        public byte[]? Get(string key) => CorruptJson;

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult<byte[]?>(CorruptJson);
        }

        public void Refresh(string key)
        {
        }

        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;

        public void Remove(string key)
        {
        }

        public Task RemoveAsync(string key, CancellationToken token = default) => Task.CompletedTask;

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
        }

        public Task SetAsync(
            string key,
            byte[] value,
            DistributedCacheEntryOptions options,
            CancellationToken token = default) => Task.CompletedTask;
    }
}
