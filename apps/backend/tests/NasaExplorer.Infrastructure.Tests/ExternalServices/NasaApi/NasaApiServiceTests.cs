using NasaExplorer.Domain.Models.Nasa;
using NasaExplorer.Infrastructure.ExternalServices.NasaApi;
using System.Net;
using System.Text;
using System.Text.Json;

namespace NasaExplorer.Infrastructure.Tests.ExternalServices.NasaApi;

public sealed class NasaApiServiceTests
{
    [Fact]
    public async Task SearchImagesAsync_maps_collection_response_and_applies_date_filter()
    {
        StubHttpMessageHandler handler = new(SearchResponseJson);
        NasaApiService service = new(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://images-api.nasa.gov/")
        });

        NasaSearchResult result = await service.SearchImagesAsync(new NasaSearchCriteria(
            "mars",
            new DateOnly(2019, 6, 1),
            new DateOnly(2019, 6, 1),
            "perseverance",
            "mastcam",
            null,
            2,
            2,
            PageScanLimit: 1));

        NasaImageAsset image = Assert.Single(result.Images);

        Assert.Equal("NHQ201906010007", image.NasaImageId);
        Assert.Equal("Mars Celebration", image.Title);
        Assert.Equal("HQ", image.Center);
        Assert.Equal("image", image.MediaType);
        Assert.Equal("https://images-assets.nasa.gov/image/NHQ201906010007/NHQ201906010007~large.jpg", image.ImageUrl);
        Assert.Equal("https://images-assets.nasa.gov/image/NHQ201906010007/NHQ201906010007~thumb.jpg", image.ThumbnailUrl);
        Assert.Equal("https://images.nasa.gov/details/NHQ201906010007", image.SourceUrl);
        Assert.Equal("Curiosity", image.Rover);
        Assert.Equal("Mastcam", image.Camera);
        Assert.Equal("https://images-assets.nasa.gov/image/NHQ201906010007/NHQ201906010007~small.jpg", image.CardUrl);
        Assert.Equal("https://images-assets.nasa.gov/image/NHQ201906010007/NHQ201906010007~medium.jpg", image.PreviewUrl);
        Assert.Equal("https://images-assets.nasa.gov/image/NHQ201906010007/NHQ201906010007~large.jpg", image.FullUrl);
        Assert.Equal(1, result.TotalHits);
        Assert.Equal(2, result.Page);
        Assert.Equal(2, result.PageSize);
        Uri firstRequestUri = handler.RequestUris[0];
        Assert.Contains("q=mars%20perseverance%20mastcam", firstRequestUri.Query);
        Assert.Contains("media_type=image", firstRequestUri.Query);
        Assert.Contains("year_start=2019", firstRequestUri.Query);
        Assert.Contains("year_end=2019", firstRequestUri.Query);
        Assert.Contains("page=2", firstRequestUri.Query);
        Assert.Contains("page_size=2", firstRequestUri.Query);
    }

    [Fact]
    public async Task SearchImagesAsync_uses_nasa_total_hits_when_date_filter_is_not_applied()
    {
        StubHttpMessageHandler handler = new(SearchResponseJson);
        NasaApiService service = new(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://images-api.nasa.gov/")
        });

        NasaSearchResult result = await service.SearchImagesAsync(new NasaSearchCriteria(
            "mars",
            null,
            null,
            null,
            null,
            null,
            1,
            1));

        Assert.Equal(2, result.Images.Count);
        Assert.Equal(26719, result.TotalHits);
    }

    [Fact]
    public async Task SearchImagesAsync_omits_query_parameter_for_general_image_search()
    {
        StubHttpMessageHandler handler = new(SearchResponseJson);
        NasaApiService service = new(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://images-api.nasa.gov/")
        });

        await service.SearchImagesAsync(new NasaSearchCriteria(
            "",
            null,
            null,
            null,
            null,
            null,
            7,
            24));

        Assert.DoesNotContain("q=", handler.RequestUri!.Query);
        Assert.Contains("media_type=image", handler.RequestUri.Query);
        Assert.Contains("page=7", handler.RequestUri.Query);
        Assert.Contains("page_size=24", handler.RequestUri.Query);
    }

    [Fact]
    public async Task SearchImagesAsync_scans_additional_pages_for_exact_date_filters()
    {
        StubHttpMessageHandler handler = new(
            SearchResponseWithOlderDateJson,
            SearchResponseJson,
            BuildSearchPageJson(0));
        NasaApiService service = new(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://images-api.nasa.gov/")
        });

        NasaSearchResult result = await service.SearchImagesAsync(new NasaSearchCriteria(
            "",
            new DateOnly(2019, 6, 1),
            new DateOnly(2019, 6, 30),
            null,
            null,
            null,
            1,
            1));

        NasaImageAsset image = Assert.Single(result.Images);

        Assert.Equal("NHQ201906010007", image.NasaImageId);
        Assert.Equal(3, handler.RequestUris.Count);
        Assert.Contains("page=1", handler.RequestUris[0].Query);
        Assert.Contains("page=2", handler.RequestUris[1].Query);
    }

    [Fact]
    public async Task SearchImagesAsync_keeps_recent_date_ranges_strict_when_exact_matches_are_empty()
    {
        StubHttpMessageHandler handler = new(SearchResponseWithCurrentYearDateJson);
        NasaApiService service = new(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://images-api.nasa.gov/")
        });
        DateOnly today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);

        NasaSearchResult result = await service.SearchImagesAsync(new NasaSearchCriteria(
            "",
            today.AddDays(-30),
            today,
            null,
            null,
            null,
            1,
            1,
            PageScanLimit: 1));

        Assert.Empty(result.Images);
        Assert.Equal(0, result.TotalHits);
        Assert.Single(handler.RequestUris);
        Assert.Contains($"year_start={today.Year}", handler.RequestUri!.Query);
        Assert.Contains($"year_end={today.Year}", handler.RequestUri.Query);
    }

    [Fact]
    public async Task SearchImagesAsync_date_filtered_pages_use_stable_filtered_offsets_without_duplicates()
    {
        string sourcePageOne = BuildSearchPageJson(6, "DATE-A", "DATE-B");
        string sourcePageTwo = BuildSearchPageJson(6, "DATE-C", "DATE-D");
        string sourcePageThree = BuildSearchPageJson(6, "DATE-E", "DATE-F");
        NasaSearchCriteria firstPageCriteria = new(
            "mars",
            new DateOnly(2024, 1, 1),
            new DateOnly(2024, 12, 31),
            null,
            null,
            null,
            1,
            2);
        StubHttpMessageHandler firstHandler = new(sourcePageOne, sourcePageTwo, sourcePageThree);
        NasaApiService firstService = CreateService(firstHandler);
        StubHttpMessageHandler secondHandler = new(sourcePageOne, sourcePageTwo, sourcePageThree);
        NasaApiService secondService = CreateService(secondHandler);

        NasaSearchResult firstPage = await firstService.SearchImagesAsync(firstPageCriteria);
        NasaSearchResult secondPage = await secondService.SearchImagesAsync(firstPageCriteria with { Page = 2 });

        Assert.Equal(["DATE-A", "DATE-B"], firstPage.Images.Select(image => image.NasaImageId));
        Assert.Equal(["DATE-C", "DATE-D"], secondPage.Images.Select(image => image.NasaImageId));
        Assert.Empty(firstPage.Images.Select(image => image.NasaImageId).Intersect(
            secondPage.Images.Select(image => image.NasaImageId),
            StringComparer.OrdinalIgnoreCase));
        Assert.Equal(6, firstPage.TotalHits);
        Assert.Equal(6, secondPage.TotalHits);
        Assert.Equal(3, secondHandler.RequestUris.Count);
        Assert.Contains("page=1", secondHandler.RequestUris[0].Query);
        Assert.Contains("page=2", secondHandler.RequestUris[1].Query);
    }

    [Fact]
    public async Task SearchImagesAsync_date_filtered_total_excludes_unscanned_upstream_hits()
    {
        StubHttpMessageHandler handler = new(SearchResponseWithOlderDateJson);
        NasaApiService service = CreateService(handler);

        NasaSearchResult result = await service.SearchImagesAsync(new NasaSearchCriteria(
            "mars",
            new DateOnly(2019, 6, 1),
            new DateOnly(2019, 6, 30),
            null,
            null,
            null,
            1,
            1));

        Assert.Empty(result.Images);
        Assert.Equal(0, result.TotalHits);
        Assert.Equal(8, handler.RequestUris.Count);
    }


    [Fact]
    public async Task GetAssetFilesAsync_maps_asset_file_metadata()
    {
        StubHttpMessageHandler handler = new(AssetResponseJson);
        NasaApiService service = new(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://images-api.nasa.gov/")
        });

        IReadOnlyCollection<NasaAssetFile> files = await service.GetAssetFilesAsync("NHQ201906010007");

        Assert.Equal("https://images-api.nasa.gov/asset/NHQ201906010007", handler.RequestUri!.ToString());
        Assert.Collection(
            files,
            file =>
            {
                Assert.Equal("canonical", file.Rel);
                Assert.Equal("image", file.Render);
            },
            file =>
            {
                Assert.Equal("alternate", file.Rel);
                Assert.Equal("image", file.Render);
            },
            file =>
            {
                Assert.Equal("metadata", file.Rel);
                Assert.Equal("metadata", file.Render);
            });
    }

    private const string SearchResponseJson = """
        {
          "collection": {
            "items": [
              {
                "data": [
                  {
                    "center": "HQ",
                    "date_created": "2019-06-01T00:00:00Z",
                    "description": "The Mars celebration captured by Curiosity Mastcam.",
                    "keywords": ["Mars", "Mars", "Celebration", "Curiosity", "Mastcam"],
                    "media_type": "image",
                    "nasa_id": "NHQ201906010007",
                    "title": "Mars Celebration"
                  }
                ],
                "links": [
                  {
                    "href": "https://images-assets.nasa.gov/image/NHQ201906010007/NHQ201906010007~medium.jpg",
                    "rel": "alternate",
                    "render": "image"
                  },
                  {
                    "href": "https://images-assets.nasa.gov/image/NHQ201906010007/NHQ201906010007~small.jpg",
                    "rel": "alternate",
                    "render": "image"
                  },
                  {
                    "href": "https://images-assets.nasa.gov/image/NHQ201906010007/NHQ201906010007~thumb.jpg",
                    "rel": "preview",
                    "render": "image"
                  },
                  {
                    "href": "https://images-assets.nasa.gov/image/NHQ201906010007/NHQ201906010007~large.jpg",
                    "rel": "alternate",
                    "render": "image"
                  }
                ]
              },
              {
                "data": [
                  {
                    "center": "HQ",
                    "date_created": "2019-05-31T00:00:00Z",
                    "description": "Older Mars celebration.",
                    "keywords": ["Mars"],
                    "media_type": "image",
                    "nasa_id": "NHQ201905310033",
                    "title": "Mars Celebration"
                  }
                ],
                "links": [
                  {
                    "href": "https://images-assets.nasa.gov/image/NHQ201905310033/NHQ201905310033~large.jpg",
                    "rel": "alternate",
                    "render": "image"
                  }
                ]
              }
            ],
            "metadata": {
              "total_hits": 26719
            }
          }
        }
        """;

    private const string AssetResponseJson = """
        {
          "collection": {
            "items": [
              {
                "href": "http://images-assets.nasa.gov/image/NHQ201906010007/NHQ201906010007~orig.tif"
              },
              {
                "href": "http://images-assets.nasa.gov/image/NHQ201906010007/NHQ201906010007~large.jpg"
              },
              {
                "href": "http://images-assets.nasa.gov/image/NHQ201906010007/metadata.json"
              }
            ]
          }
        }
        """;

    private const string SearchResponseWithOlderDateJson = """
        {
          "collection": {
            "items": [
              {
                "data": [
                  {
                    "center": "HQ",
                    "date_created": "2019-05-01T00:00:00Z",
                    "description": "Older image outside the exact date window.",
                    "keywords": ["Mars"],
                    "media_type": "image",
                    "nasa_id": "NHQ201905010001",
                    "title": "Older Mars Image"
                  }
                ],
                "links": [
                  {
                    "href": "https://images-assets.nasa.gov/image/NHQ201905010001/NHQ201905010001~large.jpg",
                    "rel": "alternate",
                    "render": "image"
                  }
                ]
              }
            ],
            "metadata": {
              "total_hits": 26719
            }
          }
        }
        """;

    private const string SearchResponseWithCurrentYearDateJson = """
        {
          "collection": {
            "items": [
              {
                "data": [
                  {
                    "center": "JPL",
                    "date_created": "2025-01-15T00:00:00Z",
                    "description": "Current year image outside the last thirty days.",
                    "keywords": ["Mars", "Curiosity", "Navcam"],
                    "media_type": "image",
                    "nasa_id": "RECENT-YEAR-IMAGE",
                    "title": "Recent Year Mars Image"
                  }
                ],
                "links": [
                  {
                    "href": "https://images-assets.nasa.gov/image/RECENT-YEAR-IMAGE/RECENT-YEAR-IMAGE~large.jpg",
                    "rel": "alternate",
                    "render": "image"
                  }
                ]
              }
            ],
            "metadata": {
              "total_hits": 43
            }
          }
        }
        """;

    private static NasaApiService CreateService(HttpMessageHandler handler)
    {
        return new NasaApiService(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://images-api.nasa.gov/")
        });
    }

    private static string BuildSearchPageJson(int totalHits, params string[] ids)
    {
        return JsonSerializer.Serialize(new
        {
            collection = new
            {
                items = ids.Select(id => new
                {
                    data = new[]
                    {
                        new
                        {
                            center = "JPL",
                            date_created = "2024-06-01T00:00:00Z",
                            description = $"Mars image {id}",
                            keywords = new[] { "Mars" },
                            media_type = "image",
                            nasa_id = id,
                            title = $"Mars {id}"
                        }
                    },
                    links = new[]
                    {
                        new
                        {
                            href = $"https://images-assets.nasa.gov/image/{id}/{id}~large.jpg",
                            rel = "preview",
                            render = "image"
                        }
                    }
                }),
                metadata = new { total_hits = totalHits }
            }
        });
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<string> _responseJsonQueue;

        public StubHttpMessageHandler(params string[] responseJson)
        {
            _responseJsonQueue = new Queue<string>(responseJson);
        }

        public Uri? RequestUri { get; private set; }

        public List<Uri> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            RequestUris.Add(request.RequestUri!);
            string responseJson = _responseJsonQueue.Count > 1
                ? _responseJsonQueue.Dequeue()
                : _responseJsonQueue.Peek();

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            });
        }
    }
}
