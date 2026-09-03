using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NasaExplorer.Domain.Entities.Collections;
using NasaExplorer.Domain.Interfaces.Services;
using NasaExplorer.Domain.Models.Ai;
using NasaExplorer.Domain.Models.Nasa;

namespace NasaExplorer.API.Tests;

public sealed class NasaExplorerApiFactory : WebApplicationFactory<Program>
{
    public StubAiEnrichmentService AiService { get; } = new();

    public StubNasaApiService NasaService { get; } = new();

    public HttpClient CreateHttpsClient()
    {
        return CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting(
            "ConnectionStrings:Default",
            "Server=localhost;Database=NasaExplorerTests;User Id=sa;Password=Testing_password_123!;TrustServerCertificate=True;Encrypt=False");
        builder.UseSetting("Jwt:Secret", "testing-secret-key-with-more-than-thirty-two-characters");
        builder.UseSetting("Jwt:Issuer", "NasaExplorerTests");
        builder.UseSetting("Jwt:Audience", "NasaExplorerTestsClient");
        builder.UseSetting("JWT_SECRET", "testing-secret-key-with-more-than-thirty-two-characters");
        builder.UseSetting("REDIS_CONNECTION_STRING", string.Empty);
        builder.UseSetting("REDIS_URL", string.Empty);
        builder.UseSetting("Redis:ConnectionString", string.Empty);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAiEnrichmentService>();
            services.RemoveAll<INasaApiService>();
            services.AddSingleton<IAiEnrichmentService>(AiService);
            services.AddSingleton<INasaApiService>(NasaService);
        });
    }
}

public sealed class StubAiEnrichmentService : IAiEnrichmentService
{
    private int _semanticSearchCalls;

    public SemanticSearchPlannerIdentity SemanticSearchIdentity { get; } = new(
        "integration-test-model",
        "integration-test-prompt-v2");

    public int SemanticSearchCalls => Volatile.Read(ref _semanticSearchCalls);

    public Task<AiImageEnrichmentResult> EnrichImageAsync(
        string imageTitle,
        string? imageDescription,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new AiImageEnrichmentResult("description", ["fact"], "context"));
    }

    public Task<string> CompareImagesAsync(
        IReadOnlyCollection<CollectionImage> images,
        string language,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult("comparison");
    }

    public Task<IReadOnlyCollection<string>> SuggestTagsAsync(
        string imageTitle,
        string? imageDescription,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyCollection<string>>(["mars"]);
    }

    public Task<SemanticSearchPlan> CreateSemanticSearchPlanAsync(
        string naturalLanguageQuery,
        string locale,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _semanticSearchCalls);

        return Task.FromResult(new SemanticSearchPlan(
            "mars sunset",
            "red planet horizon",
            ["mars", "sunset"],
            [],
            null,
            null,
            null,
            null,
            null,
            locale,
            0.95,
            SemanticSearchIdentity.Model,
            SemanticSearchIdentity.PromptVersion));
    }
}

public sealed class StubNasaApiService : INasaApiService
{
    private readonly IReadOnlyCollection<NasaImageAsset> _images =
    [
        CreateImage("integration-1", "Mars Sunset", ["mars", "sunset"]),
        CreateImage("integration-2", "Mars Horizon", ["mars", "horizon"]),
        CreateImage("integration-3", "Sunset Rover", ["sunset", "rover"]),
        CreateImage("integration-4", "Red Planet", ["mars", "planet"])
    ];
    private int _searchCalls;

    public int SearchCalls => Volatile.Read(ref _searchCalls);

    public Task<NasaSearchResult> SearchImagesAsync(
        NasaSearchCriteria criteria,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _searchCalls);

        return Task.FromResult(new NasaSearchResult(
            _images,
            _images.Count,
            criteria.Page,
            criteria.PageSize));
    }

    public Task<IReadOnlyCollection<NasaAssetFile>> GetAssetFilesAsync(
        string nasaImageId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyCollection<NasaAssetFile>>([]);
    }

    private static NasaImageAsset CreateImage(
        string id,
        string title,
        IReadOnlyCollection<string> keywords)
    {
        return new NasaImageAsset(
            id,
            title,
            $"Description for {title}",
            "JPL",
            "image",
            $"https://images.test/{id}-thumb.jpg",
            $"https://images.test/{id}.jpg",
            $"https://images.nasa.gov/details/{id}",
            new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero),
            keywords,
            null,
            null,
            null);
    }
}
