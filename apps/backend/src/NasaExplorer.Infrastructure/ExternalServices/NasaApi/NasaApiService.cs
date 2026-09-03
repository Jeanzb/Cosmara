using NasaExplorer.Domain.Interfaces.Services;
using NasaExplorer.Domain.Models.Nasa;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NasaExplorer.Infrastructure.ExternalServices.NasaApi;

public sealed class NasaApiService : INasaApiService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const int MaximumDateFilteredPageScanLimit = 8;
    private static readonly string[] KnownMissions =
    [
        "JWST",
        "Hubble",
        "Cassini",
        "Juno",
        "DSCOVR EPIC",
        "Rosetta",
        "Apollo",
        "LRO",
        "Mars 2020",
        "Mars Science Laboratory"
    ];
    private static readonly string[] KnownRovers =
    [
        "Perseverance",
        "Curiosity",
        "Opportunity",
        "Spirit"
    ];
    private static readonly string[] KnownCameras =
    [
        "NIRCam",
        "Mastcam-Z",
        "Mastcam",
        "Navcam",
        "JunoCam",
        "HiRISE",
        "WATSON",
        "MAHLI",
        "LRO NAC",
        "WFC3",
        "ACS"
    ];

    private readonly HttpClient _httpClient;

    public NasaApiService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<NasaSearchResult> SearchImagesAsync(NasaSearchCriteria criteria, CancellationToken cancellationToken = default)
    {
        if (HasLocalDateFilter(criteria))
        {
            return await SearchDateFilteredImagesAsync(criteria, cancellationToken);
        }

        NasaSearchResponse? nasaResponse = await FetchSearchResponseAsync(criteria, cancellationToken);

        IReadOnlyCollection<NasaImageAsset> images = (nasaResponse?.Collection?.Items ?? [])
            .Select(ToImageAsset)
            .OfType<NasaImageAsset>()
            .ToArray();

        return new NasaSearchResult(
            images,
            nasaResponse?.Collection?.Metadata?.TotalHits ?? images.Count,
            criteria.Page,
            criteria.PageSize);
    }

    private async Task<NasaSearchResult> SearchDateFilteredImagesAsync(
        NasaSearchCriteria criteria,
        CancellationToken cancellationToken)
    {
        int requestedPage = Math.Max(1, criteria.Page);
        int pageSize = Math.Max(1, criteria.PageSize);
        int pageScanLimit = Math.Clamp(criteria.PageScanLimit, 1, MaximumDateFilteredPageScanLimit);

        if (pageScanLimit == 1)
        {
            NasaSearchResponse? response = await FetchSearchResponseAsync(
                criteria with { Page = requestedPage, PageSize = pageSize },
                cancellationToken);
            NasaImageAsset[] directImages = (response?.Collection?.Items ?? [])
                .Select(ToImageAsset)
                .OfType<NasaImageAsset>()
                .Where(image => MatchesDateRange(image, criteria))
                .DistinctBy(image => image.NasaImageId, StringComparer.OrdinalIgnoreCase)
                .Take(pageSize)
                .ToArray();

            return new NasaSearchResult(
                directImages,
                directImages.Length,
                requestedPage,
                pageSize);
        }

        int requestedOffset = checked((requestedPage - 1) * pageSize);
        List<NasaImageAsset> matchedImages = [];
        HashSet<string> seenIds = new(StringComparer.OrdinalIgnoreCase);
        int upstreamTotalHits = 0;

        for (int sourcePage = 1; sourcePage <= pageScanLimit; sourcePage += 1)
        {
            NasaSearchCriteria pageCriteria = criteria with
            {
                Page = sourcePage,
                PageSize = pageSize
            };
            NasaSearchResponse? nasaResponse = await FetchSearchResponseAsync(pageCriteria, cancellationToken);
            IReadOnlyCollection<NasaSearchItem> items = nasaResponse?.Collection?.Items ?? [];
            upstreamTotalHits = Math.Max(
                upstreamTotalHits,
                nasaResponse?.Collection?.Metadata?.TotalHits ?? 0);

            if (items.Count == 0)
            {
                break;
            }

            NasaImageAsset[] pageImages = items
                .Select(ToImageAsset)
                .OfType<NasaImageAsset>()
                .ToArray();

            foreach (NasaImageAsset image in pageImages.Where(image => MatchesDateRange(image, criteria)))
            {
                if (seenIds.Add(image.NasaImageId))
                {
                    matchedImages.Add(image);
                }
            }

            if (items.Count < pageSize
                || (upstreamTotalHits > 0 && sourcePage * pageSize >= upstreamTotalHits))
            {
                break;
            }
        }

        NasaImageAsset[] images = matchedImages
            .Skip(requestedOffset)
            .Take(pageSize)
            .ToArray();

        return new NasaSearchResult(
            images,
            matchedImages.Count,
            requestedPage,
            pageSize);
    }

    private async Task<NasaSearchResponse?> FetchSearchResponseAsync(
        NasaSearchCriteria criteria,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient.GetAsync(BuildSearchUri(criteria), cancellationToken);
        response.EnsureSuccessStatusCode();

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);

        return await JsonSerializer.DeserializeAsync<NasaSearchResponse>(stream, JsonOptions, cancellationToken);
    }

    public async Task<IReadOnlyCollection<NasaAssetFile>> GetAssetFilesAsync(string nasaImageId, CancellationToken cancellationToken = default)
    {
        string requestUri = $"asset/{Uri.EscapeDataString(nasaImageId)}";

        using HttpResponseMessage response = await _httpClient.GetAsync(requestUri, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        NasaAssetResponse? assetResponse = await JsonSerializer.DeserializeAsync<NasaAssetResponse>(stream, JsonOptions, cancellationToken);

        return (assetResponse?.Collection?.Items ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item.Href))
            .Select(item => new NasaAssetFile(
                item.Href,
                ResolveAssetRel(item.Href),
                ResolveAssetRender(item.Href),
                null,
                null,
                null))
            .ToArray();
    }

    private static string BuildSearchUri(NasaSearchCriteria criteria)
    {
        string searchTerm = string.Join(' ', BuildSearchTerms(criteria));
        List<KeyValuePair<string, string>> parameters =
        [
            new("media_type", "image"),
            new("page", criteria.Page.ToString(CultureInfo.InvariantCulture)),
            new("page_size", criteria.PageSize.ToString(CultureInfo.InvariantCulture))
        ];

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            parameters.Insert(0, new KeyValuePair<string, string>("q", searchTerm));
        }

        if (criteria.DateFrom.HasValue)
        {
            parameters.Add(new KeyValuePair<string, string>("year_start", criteria.DateFrom.Value.Year.ToString(CultureInfo.InvariantCulture)));
        }

        if (criteria.DateTo.HasValue)
        {
            parameters.Add(new KeyValuePair<string, string>("year_end", criteria.DateTo.Value.Year.ToString(CultureInfo.InvariantCulture)));
        }

        string queryString = string.Join(
            '&',
            parameters.Select(parameter => $"{Uri.EscapeDataString(parameter.Key)}={Uri.EscapeDataString(parameter.Value)}"));

        return $"search?{queryString}";
    }

    private static IEnumerable<string> BuildSearchTerms(NasaSearchCriteria criteria)
    {
        string[] terms =
        [
            criteria.Query ?? string.Empty,
            criteria.Rover ?? string.Empty,
            criteria.Camera ?? string.Empty,
            criteria.Mission ?? string.Empty
        ];

        var distinctTerms = terms
            .Select(term => term.Trim())
            .Where(term => !string.IsNullOrWhiteSpace(term))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return distinctTerms;
    }

    private static NasaImageAsset? ToImageAsset(NasaSearchItem item)
    {
        NasaSearchItemData? data = item.Data?.FirstOrDefault();

        if (data is null || string.IsNullOrWhiteSpace(data.NasaId))
        {
            return null;
        }

        NasaSearchLink[] links = (item.Links ?? []).ToArray();
        string? imageUrl = SelectImageUrl(links);
        string? thumbnailUrl = SelectThumbnailUrl(links, imageUrl);
        string? cardUrl = SelectCardUrl(links, thumbnailUrl);
        string? previewUrl = SelectPreviewUrl(links, imageUrl);
        string? fullUrl = SelectFullUrl(links, imageUrl);

        if (string.IsNullOrWhiteSpace(imageUrl) || string.IsNullOrWhiteSpace(thumbnailUrl))
        {
            return null;
        }

        string[] keywords = (data.Keywords ?? [])
            .Where(keyword => !string.IsNullOrWhiteSpace(keyword))
            .Select(keyword => keyword.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new NasaImageAsset(
            data.NasaId,
            string.IsNullOrWhiteSpace(data.Title) ? data.NasaId : data.Title,
            data.Description,
            data.Center,
            string.IsNullOrWhiteSpace(data.MediaType) ? "image" : data.MediaType,
            thumbnailUrl,
            imageUrl,
            $"https://images.nasa.gov/details/{Uri.EscapeDataString(data.NasaId)}",
            data.DateCreated,
            keywords,
            ResolveKnownMetadata(KnownMissions, data, keywords),
            ResolveKnownMetadata(KnownRovers, data, keywords),
            ResolveKnownMetadata(KnownCameras, data, keywords),
            cardUrl,
            previewUrl,
            fullUrl);
    }

    private static string? SelectImageUrl(IReadOnlyCollection<NasaSearchLink> links)
    {
        NasaSearchLink[] webImageLinks = links
            .Where(link => IsWebImageLink(link.Href))
            .ToArray();

        return SelectPreferredLink(webImageLinks, "~large", "~medium", "~small", "~thumb")
            ?? SelectPreferredLink(links.Where(link => IsImageRender(link)).ToArray(), "~large", "~medium", "~small", "~thumb");
    }

    private static string? SelectThumbnailUrl(IReadOnlyCollection<NasaSearchLink> links, string? imageUrl)
    {
        string? preview = links.FirstOrDefault(link =>
            string.Equals(link.Rel, "preview", StringComparison.OrdinalIgnoreCase)
            && IsWebImageLink(link.Href))?.Href;

        return preview
            ?? SelectPreferredLink(links.Where(link => IsWebImageLink(link.Href)).ToArray(), "~thumb")
            ?? imageUrl;
    }

    private static string? SelectCardUrl(IReadOnlyCollection<NasaSearchLink> links, string? thumbnailUrl)
    {
        return SelectPreferredLink(links.Where(link => IsWebImageLink(link.Href)).ToArray(), "~small", "~medium", "~thumb", "~large")
            ?? thumbnailUrl;
    }

    private static string? SelectPreviewUrl(IReadOnlyCollection<NasaSearchLink> links, string? imageUrl)
    {
        return SelectPreferredLink(links.Where(link => IsWebImageLink(link.Href)).ToArray(), "~medium", "~large", "~small", "~thumb")
            ?? imageUrl;
    }

    private static string? SelectFullUrl(IReadOnlyCollection<NasaSearchLink> links, string? imageUrl)
    {
        return SelectPreferredLink(links.Where(link => IsWebImageLink(link.Href)).ToArray(), "~orig", "~large", "~medium", "~small", "~thumb")
            ?? imageUrl;
    }

    private static string? SelectPreferredLink(IReadOnlyCollection<NasaSearchLink> links, params string[] priorities)
    {
        foreach (string priority in priorities)
        {
            string? link = links.FirstOrDefault(item =>
                item.Href.Contains(priority, StringComparison.OrdinalIgnoreCase))?.Href;

            if (!string.IsNullOrWhiteSpace(link))
            {
                return link;
            }
        }

        return links.FirstOrDefault(link => !string.IsNullOrWhiteSpace(link.Href))?.Href;
    }

    private static string? ResolveKnownMetadata(
        IReadOnlyCollection<string> candidates,
        NasaSearchItemData data,
        IReadOnlyCollection<string> keywords)
    {
        string searchableText = string.Join(
            ' ',
            [
                data.Title ?? string.Empty,
                data.Description ?? string.Empty,
                data.Center ?? string.Empty,
                .. keywords
            ]);

        return candidates.FirstOrDefault(candidate =>
            searchableText.Contains(candidate, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesDateRange(NasaImageAsset image, NasaSearchCriteria criteria)
    {
        if (!HasLocalDateFilter(criteria))
        {
            return true;
        }

        if (!image.DateCreated.HasValue)
        {
            return false;
        }

        DateOnly imageDate = DateOnly.FromDateTime(image.DateCreated.Value.UtcDateTime);

        return (!criteria.DateFrom.HasValue || imageDate >= criteria.DateFrom.Value)
            && (!criteria.DateTo.HasValue || imageDate <= criteria.DateTo.Value);
    }

    private static bool HasLocalDateFilter(NasaSearchCriteria criteria)
    {
        return criteria.DateFrom.HasValue || criteria.DateTo.HasValue;
    }

    private static bool IsImageRender(NasaSearchLink link)
    {
        return !string.IsNullOrWhiteSpace(link.Href)
            && string.Equals(link.Render, "image", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWebImageLink(string href)
    {
        return href.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
            || href.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
            || href.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            || href.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)
            || href.EndsWith(".webp", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveAssetRel(string href)
    {
        if (href.EndsWith("metadata.json", StringComparison.OrdinalIgnoreCase))
        {
            return "metadata";
        }

        if (href.Contains("~thumb", StringComparison.OrdinalIgnoreCase))
        {
            return "preview";
        }

        if (href.Contains("~orig", StringComparison.OrdinalIgnoreCase))
        {
            return "canonical";
        }

        return "alternate";
    }

    private static string? ResolveAssetRender(string href)
    {
        if (href.EndsWith("metadata.json", StringComparison.OrdinalIgnoreCase))
        {
            return "metadata";
        }

        return IsWebImageLink(href) || href.EndsWith(".tif", StringComparison.OrdinalIgnoreCase)
            ? "image"
            : null;
    }

    private sealed class NasaSearchResponse
    {
        public NasaSearchCollection? Collection { get; set; }
    }

    private sealed class NasaSearchCollection
    {
        public IReadOnlyCollection<NasaSearchItem>? Items { get; set; }

        public NasaSearchMetadata? Metadata { get; set; }
    }

    private sealed class NasaSearchMetadata
    {
        [JsonPropertyName("total_hits")]
        public int TotalHits { get; set; }
    }

    private sealed class NasaSearchItem
    {
        public IReadOnlyCollection<NasaSearchItemData>? Data { get; set; }

        public IReadOnlyCollection<NasaSearchLink>? Links { get; set; }
    }

    private sealed class NasaSearchItemData
    {
        public string? Center { get; set; }

        [JsonPropertyName("date_created")]
        public DateTimeOffset? DateCreated { get; set; }

        public string? Description { get; set; }

        public IReadOnlyCollection<string>? Keywords { get; set; }

        [JsonPropertyName("media_type")]
        public string? MediaType { get; set; }

        [JsonPropertyName("nasa_id")]
        public string? NasaId { get; set; }

        public string? Title { get; set; }
    }

    private sealed class NasaSearchLink
    {
        public string Href { get; set; } = string.Empty;

        public string? Rel { get; set; }

        public string? Render { get; set; }
    }

    private sealed class NasaAssetResponse
    {
        public NasaAssetCollection? Collection { get; set; }
    }

    private sealed class NasaAssetCollection
    {
        public IReadOnlyCollection<NasaAssetItem>? Items { get; set; }
    }

    private sealed class NasaAssetItem
    {
        public string Href { get; set; } = string.Empty;
    }
}
