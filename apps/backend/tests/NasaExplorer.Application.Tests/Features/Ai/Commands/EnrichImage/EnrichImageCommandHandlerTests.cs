using NasaExplorer.Application.DTOs.Ai;
using NasaExplorer.Application.Features.Ai.Commands.EnrichImage;
using NasaExplorer.Application.Tests.Features.Collections;
using NasaExplorer.Domain.Entities.Collections;
using NasaExplorer.Domain.Entities.Images;
using NasaExplorer.Domain.Interfaces.Repositories;
using NasaExplorer.Domain.Interfaces.Services;
using NasaExplorer.Domain.Models.Ai;
using System.Text.Json;

namespace NasaExplorer.Application.Tests.Features.Ai.Commands.EnrichImage;

public sealed class EnrichImageCommandHandlerTests
{
    [Fact]
    public async Task Handle_returns_cached_enrichment_for_current_user()
    {
        Guid userId = Guid.NewGuid();
        SpaceImage image = CollectionQueryTestData.CreateSpaceImage("mars-1", "Mars 1");
        AiImageEnrichmentResult cachedResult = new("Cached description", ["cached fact"], "Cached context");
        FakeImageEnrichmentRepository enrichmentRepository = new(
            ImageEnrichment.Create(
                image.Id,
                userId,
                "Description",
                "Prompt",
                JsonSerializer.Serialize(cachedResult, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                "gpt-4o-mini",
                "OpenAI",
                DateTimeOffset.UtcNow));
        StubAiEnrichmentService aiService = new(cachedResult);
        EnrichImageCommandHandler handler = new(
            new FakeSpaceImageRepository(image),
            enrichmentRepository,
            aiService,
            new StubCurrentUserService(userId));

        EnrichmentResultDto result = await handler.Handle(CreateCommand(), CancellationToken.None);

        Assert.True(result.FromCache);
        Assert.Equal("Cached description", result.Description);
        Assert.Equal(0, aiService.EnrichCalls);
        Assert.Null(enrichmentRepository.AddedEnrichment);
    }

    [Fact]
    public async Task Handle_creates_space_image_and_caches_generated_enrichment()
    {
        Guid userId = Guid.NewGuid();
        AiImageEnrichmentResult generatedResult = new("Generated description", ["generated fact"], "Generated context");
        FakeSpaceImageRepository spaceImageRepository = new(null);
        FakeImageEnrichmentRepository enrichmentRepository = new(null);
        StubAiEnrichmentService aiService = new(generatedResult);
        EnrichImageCommandHandler handler = new(
            spaceImageRepository,
            enrichmentRepository,
            aiService,
            new StubCurrentUserService(userId));

        EnrichmentResultDto result = await handler.Handle(CreateCommand(), CancellationToken.None);

        Assert.False(result.FromCache);
        Assert.NotNull(spaceImageRepository.AddedImage);
        Assert.Equal("mars-1", spaceImageRepository.AddedImage.NasaId);
        Assert.NotNull(enrichmentRepository.AddedEnrichment);
        Assert.Equal(spaceImageRepository.AddedImage.Id, enrichmentRepository.AddedEnrichment.SpaceImageId);
        Assert.Equal(userId, enrichmentRepository.AddedEnrichment.UserId);
        Assert.Equal("Generated description", result.Description);
        Assert.Equal(1, aiService.EnrichCalls);
    }

    private static EnrichImageCommand CreateCommand()
    {
        return new EnrichImageCommand(
            "mars-1",
            "Mars 1",
            "NASA description",
            "https://images.test/mars-1.jpg",
            "https://images.test/mars-1-thumb.jpg",
            "https://images.nasa.gov/details/mars-1",
            DateTimeOffset.UtcNow);
    }
}

internal sealed class FakeImageEnrichmentRepository : IImageEnrichmentRepository
{
    private readonly ImageEnrichment? _enrichment;

    public FakeImageEnrichmentRepository(ImageEnrichment? enrichment)
    {
        _enrichment = enrichment;
    }

    public ImageEnrichment? AddedEnrichment { get; private set; }

    public Task<ImageEnrichment?> GetBySpaceImageUserAndTypeAsync(
        Guid spaceImageId,
        Guid userId,
        string type,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_enrichment);
    }

    public Task<IReadOnlyCollection<ImageEnrichment>> GetBySpaceImageIdForUserAsync(
        Guid spaceImageId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<ImageEnrichment> enrichments = _enrichment is null ? [] : [_enrichment];

        return Task.FromResult(enrichments);
    }

    public Task AddAsync(ImageEnrichment enrichment, CancellationToken cancellationToken = default)
    {
        AddedEnrichment = enrichment;

        return Task.CompletedTask;
    }
}

internal sealed class StubAiEnrichmentService : IAiEnrichmentService
{
    private readonly AiImageEnrichmentResult _enrichmentResult;
    private readonly string _comparisonResult;
    private readonly IReadOnlyCollection<string> _tags;
    private readonly SemanticSearchPlan _semanticSearchPlan;

    public SemanticSearchPlannerIdentity SemanticSearchIdentity => new(
        _semanticSearchPlan.Model,
        _semanticSearchPlan.PromptVersion);

    public StubAiEnrichmentService(
        AiImageEnrichmentResult? enrichmentResult = null,
        string comparisonResult = "Comparison analysis",
        IReadOnlyCollection<string>? tags = null,
        string semanticSearchResult = "semantic query",
        SemanticSearchPlan? semanticSearchPlan = null)
    {
        _enrichmentResult = enrichmentResult ?? new AiImageEnrichmentResult("Description", ["fact"], "Context");
        _comparisonResult = comparisonResult;
        _tags = tags ?? ["mars"];
        _semanticSearchPlan = semanticSearchPlan ?? new SemanticSearchPlan(
            semanticSearchResult,
            [semanticSearchResult],
            null,
            null,
            null,
            null,
            null,
            "en");
    }

    public int EnrichCalls { get; private set; }

    public int CompareCalls { get; private set; }

    public string? LastComparisonLanguage { get; private set; }

    public int SuggestTagsCalls { get; private set; }

    public int SemanticSearchCalls { get; private set; }

    public Task<AiImageEnrichmentResult> EnrichImageAsync(
        string imageTitle,
        string? imageDescription,
        CancellationToken cancellationToken = default)
    {
        EnrichCalls++;

        return Task.FromResult(_enrichmentResult);
    }

    public Task<string> CompareImagesAsync(IReadOnlyCollection<CollectionImage> images, string language, CancellationToken cancellationToken = default)
    {
        CompareCalls++;
        LastComparisonLanguage = language;

        return Task.FromResult(_comparisonResult);
    }

    public Task<IReadOnlyCollection<string>> SuggestTagsAsync(
        string imageTitle,
        string? imageDescription,
        CancellationToken cancellationToken = default)
    {
        SuggestTagsCalls++;

        return Task.FromResult(_tags);
    }

    public Task<SemanticSearchPlan> CreateSemanticSearchPlanAsync(
        string naturalLanguageQuery,
        string locale,
        CancellationToken cancellationToken = default)
    {
        SemanticSearchCalls++;

        return Task.FromResult(_semanticSearchPlan with { Locale = locale });
    }
}
