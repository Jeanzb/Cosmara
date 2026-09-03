using Microsoft.Extensions.Logging.Abstractions;
using NasaExplorer.Application.Common.Exceptions;
using NasaExplorer.Application.Common.Interfaces;
using NasaExplorer.Application.DTOs.Search;
using NasaExplorer.Application.Features.Search;
using NasaExplorer.Application.Features.Search.Queries.SemanticSearchNasaImages;
using NasaExplorer.Application.Tests.Features.Ai.Commands.EnrichImage;
using NasaExplorer.Domain.Interfaces.Services;
using NasaExplorer.Domain.Models.Ai;
using NasaExplorer.Domain.Models.Nasa;

namespace NasaExplorer.Application.Tests.Features.Search.Queries.SemanticSearchNasaImages;

public sealed class SemanticSearchNasaImagesQueryHandlerTests
{
    [Fact]
    public async Task Handle_uses_structured_plan_and_explicit_filters_override_inferences()
    {
        SemanticSearchPlan aiPlan = new(
            "mars dust storm",
            ["mars", "dust storm"],
            new DateOnly(2023, 1, 1),
            new DateOnly(2024, 12, 31),
            "Curiosity",
            "Navcam",
            "Mars 2020",
            "en");
        StubAiEnrichmentService aiService = new(semanticSearchPlan: aiPlan);
        StubNasaApiService nasaApiService = new(new NasaSearchResult(
            [CreateImage("mars-1", "Mars Dust Storm", ["mars", "dust"], "Mars", "Perseverance", "Mastcam")],
            1,
            1,
            100));
        SemanticSearchNasaImagesQueryHandler handler = CreateHandler(nasaApiService, aiService);

        NasaSearchResultDto result = await handler.Handle(
            new SemanticSearchNasaImagesQuery(
                "show me red planet storms",
                new DateOnly(2024, 1, 1),
                null,
                "Perseverance",
                "Mastcam",
                "Mars",
                1,
                12,
                "en"),
            CancellationToken.None);

        Assert.All(nasaApiService.CriteriaCalls, criteria =>
        {
            Assert.Equal("Perseverance", criteria.Rover);
            Assert.Equal("Mastcam", criteria.Camera);
            Assert.Equal("Mars", criteria.Mission);
            Assert.Equal(1, criteria.PageScanLimit);
        });
        Assert.Equal(new DateOnly(2024, 1, 1), result.AppliedFilters.DateFrom);
        Assert.Equal(new DateOnly(2024, 12, 31), result.AppliedFilters.DateTo);
        Assert.Equal(["dateTo"], result.AppliedFilters.Inferred);
        Assert.Equal("semantic", result.Mode);
        Assert.Equal(1, aiService.SemanticSearchCalls);
        Assert.NotNull(Assert.Single(result.Images).RelevanceScore);
    }

    [Fact]
    public async Task Handle_suppresses_only_requested_inferred_filters_and_ignores_unknown_keys()
    {
        SemanticSearchPlan aiPlan = new(
            "mars rover",
            ["mars", "rover"],
            null,
            null,
            "Curiosity",
            null,
            "Mars 2020",
            "en");
        StubAiEnrichmentService aiService = new(semanticSearchPlan: aiPlan);
        StubNasaApiService nasaApiService = new(new NasaSearchResult(
            [CreateImage("mars-2", "Mars landscape", ["mars"])],
            1,
            1,
            100));
        SemanticSearchNasaImagesQueryHandler handler = CreateHandler(nasaApiService, aiService);

        NasaSearchResultDto result = await handler.Handle(
            new SemanticSearchNasaImagesQuery(
                "mars rover",
                null,
                null,
                null,
                null,
                "Explicit Mission",
                1,
                24,
                SuppressInferred: "rover,mission,unknown"),
            CancellationToken.None);

        Assert.All(nasaApiService.CriteriaCalls, criteria => Assert.Null(criteria.Rover));
        Assert.All(nasaApiService.CriteriaCalls, criteria => Assert.Equal("Explicit Mission", criteria.Mission));
        Assert.Equal("Explicit Mission", result.AppliedFilters.Mission);
        Assert.Null(result.AppliedFilters.Rover);
        Assert.Empty(result.AppliedFilters.Inferred);
    }

    [Fact]
    public async Task Handle_plan_fingerprint_changes_with_explicit_filters_and_planner_identity()
    {
        SemanticSearchPlan firstPlan = new(
            "mars",
            null,
            ["mars"],
            [],
            null,
            null,
            null,
            null,
            null,
            "en",
            0.9,
            "model-a",
            "prompt-a");
        SemanticSearchPlan secondPlan = firstPlan with { Model = "model-b", PromptVersion = "prompt-b" };
        StubAiEnrichmentService firstAi = new(semanticSearchPlan: firstPlan);
        StubAiEnrichmentService secondAi = new(semanticSearchPlan: secondPlan);
        StubNasaApiService nasaApiService = new(new NasaSearchResult([], 0, 1, 100));
        StubSemanticSearchCache cache = new();

        await CreateHandler(nasaApiService, firstAi, cache).Handle(
            new SemanticSearchNasaImagesQuery("  MARS  ", null, null, "Curiosity", null, null, 1, 24, "en"),
            CancellationToken.None);
        await CreateHandler(nasaApiService, firstAi, cache).Handle(
            new SemanticSearchNasaImagesQuery("mars", null, null, "Perseverance", null, null, 1, 24, "en"),
            CancellationToken.None);
        await CreateHandler(nasaApiService, secondAi, cache).Handle(
            new SemanticSearchNasaImagesQuery("mars", null, null, "Curiosity", null, null, 1, 24, "en"),
            CancellationToken.None);

        Assert.Equal(3, cache.PlanFingerprints.Count);
        Assert.Equal(3, cache.PlanFingerprints.Distinct(StringComparer.Ordinal).Count());
        Assert.All(cache.PlanFingerprints, fingerprint =>
        {
            Assert.Equal(64, fingerprint.Length);
            Assert.DoesNotContain("mars", fingerprint, StringComparison.OrdinalIgnoreCase);
        });
        Assert.Equal(2, firstAi.SemanticSearchCalls);
        Assert.Equal(1, secondAi.SemanticSearchCalls);
    }

    [Fact]
    public async Task Handle_limits_upstream_work_to_two_candidates_and_six_nasa_calls()
    {
        StubAiEnrichmentService aiService = new(semanticSearchResult: "mars dust storm");
        StubNasaApiService nasaApiService = new(criteria => new NasaSearchResult(
            Enumerable.Range(0, 100)
                .Select(index => CreateImage(
                    $"{criteria.Query}-{criteria.Page}-{index}",
                    "Mars dust storm",
                    ["mars", "dust", "storm"]))
                .ToArray(),
            600,
            criteria.Page,
            criteria.PageSize));
        SemanticSearchNasaImagesQueryHandler handler = CreateHandler(nasaApiService, aiService);

        NasaSearchResultDto result = await handler.Handle(
            new SemanticSearchNasaImagesQuery(
                "red planet weather",
                null,
                null,
                null,
                null,
                null,
                1,
                24),
            CancellationToken.None);

        Assert.Equal(6, nasaApiService.CriteriaCalls.Count);
        Assert.Equal(2, nasaApiService.CriteriaCalls.Select(criteria => criteria.Query).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(nasaApiService.CriteriaCalls, criteria => Assert.Equal(1, criteria.PageScanLimit));
        Assert.Equal(200, result.TotalHits);
    }

    [Fact]
    public async Task Handle_merges_deduplicates_and_ranks_results()
    {
        SemanticSearchPlan plan = new("mars sunset", ["mars", "sunset"], null, null, null, null, null, "en");
        StubAiEnrichmentService aiService = new(semanticSearchPlan: plan);
        StubNasaApiService nasaApiService = new(criteria => criteria.Query == "mars sunset"
            ? new NasaSearchResult(
                [
                    CreateImage("exact", "Mars Sunset", ["mars", "sunset"]),
                    CreateImage("duplicate", "Mars horizon", ["mars"])
                ],
                2,
                criteria.Page,
                criteria.PageSize)
            : new NasaSearchResult(
                [
                    CreateImage("duplicate", "Mars horizon", ["mars"]),
                    CreateImage("weak", "Planet archive", ["planet"])
                ],
                2,
                criteria.Page,
                criteria.PageSize));
        SemanticSearchNasaImagesQueryHandler handler = CreateHandler(nasaApiService, aiService);

        NasaSearchResultDto result = await handler.Handle(
            new SemanticSearchNasaImagesQuery("sunset on mars", null, null, null, null, null, 1, 24),
            CancellationToken.None);

        Assert.Equal(3, result.TotalHits);
        Assert.Equal("exact", result.Images.First().NasaImageId);
        Assert.Equal(1, result.Images.Count(image => image.NasaImageId == "duplicate"));
        Assert.Contains("title", result.Images.First().MatchReasons);
        Assert.Contains("keywords", result.Images.First().MatchReasons);
        Assert.True(result.Images.First().RelevanceScore > result.Images.Last().RelevanceScore);
    }

    [Fact]
    public async Task Handle_cursor_continues_cached_pool_without_repeating_ai_or_nasa_calls()
    {
        StubAiEnrichmentService aiService = new(semanticSearchResult: "mars");
        StubNasaApiService nasaApiService = new(new NasaSearchResult(
            [
                CreateImage("mars-1", "Mars one", ["mars"]),
                CreateImage("mars-2", "Mars two", ["mars"]),
                CreateImage("mars-3", "Mars three", ["mars"])
            ],
            3,
            1,
            100));
        StubSemanticSearchCache cache = new();
        SemanticSearchNasaImagesQueryHandler handler = CreateHandler(nasaApiService, aiService, cache);

        NasaSearchResultDto firstPage = await handler.Handle(
            new SemanticSearchNasaImagesQuery("mars", null, null, null, null, null, 1, 1),
            CancellationToken.None);
        int nasaCallsAfterFirstPage = nasaApiService.CriteriaCalls.Count;
        NasaSearchResultDto secondPage = await handler.Handle(
            new SemanticSearchNasaImagesQuery(null, null, null, null, null, null, 1, 1, Cursor: firstPage.NextCursor),
            CancellationToken.None);

        Assert.NotNull(firstPage.NextCursor);
        Assert.Equal(firstPage.SearchId, secondPage.SearchId);
        Assert.Equal(2, secondPage.Page);
        Assert.Equal(1, aiService.SemanticSearchCalls);
        Assert.Equal(nasaCallsAfterFirstPage, nasaApiService.CriteriaCalls.Count);
        Assert.NotEqual(firstPage.Images.Single().NasaImageId, secondPage.Images.Single().NasaImageId);
    }

    [Fact]
    public async Task Handle_unknown_or_expired_cursor_throws_gone_exception()
    {
        SemanticSearchNasaImagesQueryHandler handler = CreateHandler(
            new StubNasaApiService(new NasaSearchResult([], 0, 1, 100)),
            new StubAiEnrichmentService());

        await Assert.ThrowsAsync<ExpiredCursorException>(() => handler.Handle(
            new SemanticSearchNasaImagesQuery(null, null, null, null, null, null, 1, 24, Cursor: "expiredcursor"),
            CancellationToken.None));
    }

    [Fact]
    public async Task Handle_returns_relaxation_suggestions_without_relaxing_filters()
    {
        StubAiEnrichmentService aiService = new(semanticSearchPlan: new SemanticSearchPlan(
            "mars",
            ["mars"],
            null,
            null,
            null,
            null,
            null,
            "en"));
        StubNasaApiService nasaApiService = new(new NasaSearchResult([], 0, 1, 100));
        SemanticSearchNasaImagesQueryHandler handler = CreateHandler(nasaApiService, aiService);

        NasaSearchResultDto result = await handler.Handle(
            new SemanticSearchNasaImagesQuery(
                "mars",
                new DateOnly(2024, 1, 1),
                new DateOnly(2024, 1, 2),
                "Curiosity",
                "Navcam",
                "Mars 2020",
                1,
                24),
            CancellationToken.None);

        Assert.Empty(result.Images);
        Assert.Equal(
            ["remove_date_range", "remove_mission", "remove_rover", "remove_camera"],
            result.RelaxationSuggestions.Select(suggestion => suggestion.Code));
        Assert.All(nasaApiService.CriteriaCalls, criteria =>
        {
            Assert.Equal(new DateOnly(2024, 1, 1), criteria.DateFrom);
            Assert.Equal(new DateOnly(2024, 1, 2), criteria.DateTo);
        });
    }

    [Fact]
    public async Task Handle_rejects_conflicting_structured_metadata_even_when_text_mentions_requested_filters()
    {
        StubNasaApiService nasaApiService = new(new NasaSearchResult(
            [
                CreateImage(
                    "matching",
                    "Curiosity Navcam view",
                    ["Curiosity", "Navcam", "Mars Science Laboratory"],
                    "Mars Science Laboratory",
                    "Curiosity",
                    "Navcam"),
                CreateImage(
                    "conflicting",
                    "Curiosity Navcam comparison",
                    ["Curiosity", "Navcam", "Mars Science Laboratory"],
                    "Mars 2020",
                    "Perseverance",
                    "Mastcam-Z",
                    "A comparison with Curiosity Navcam from Mars Science Laboratory")
            ],
            2,
            1,
            100));
        SemanticSearchNasaImagesQueryHandler handler = CreateHandler(
            nasaApiService,
            new StubAiEnrichmentService(semanticSearchResult: "mars rover"));

        NasaSearchResultDto result = await handler.Handle(
            new SemanticSearchNasaImagesQuery(
                "mars rover",
                null,
                null,
                "Curiosity",
                "Navcam",
                "Mars Science Laboratory",
                1,
                24),
            CancellationToken.None);

        Assert.Equal("matching", Assert.Single(result.Images).NasaImageId);
    }

    [Fact]
    public async Task Handle_does_not_treat_mastcam_z_as_an_exact_mastcam_filter_match()
    {
        StubNasaApiService nasaApiService = new(new NasaSearchResult(
            [
                CreateImage("mastcam", "Mastcam image", ["Mars"], camera: "Mastcam"),
                CreateImage("mastcam-z", "Mastcam-Z image", ["Mars"], camera: "Mastcam-Z")
            ],
            2,
            1,
            100));
        SemanticSearchNasaImagesQueryHandler handler = CreateHandler(
            nasaApiService,
            new StubAiEnrichmentService(semanticSearchResult: "mars terrain"));

        NasaSearchResultDto result = await handler.Handle(
            new SemanticSearchNasaImagesQuery(
                "mars terrain",
                null,
                null,
                null,
                "Mastcam",
                null,
                1,
                24),
            CancellationToken.None);

        Assert.Equal("mastcam", Assert.Single(result.Images).NasaImageId);
    }

    [Fact]
    public async Task Handle_continues_after_recoverable_nasa_failure_and_marks_partial_degradation()
    {
        int nasaAttempts = 0;
        StubNasaApiService nasaApiService = new(criteria =>
        {
            nasaAttempts += 1;
            if (nasaAttempts == 1)
            {
                throw new HttpRequestException("temporary NASA failure");
            }

            return new NasaSearchResult(
                [CreateImage("mars-recovered", "Mars", ["mars"])],
                1,
                criteria.Page,
                criteria.PageSize);
        });
        SemanticSearchNasaImagesQueryHandler handler = CreateHandler(
            nasaApiService,
            new StubAiEnrichmentService(semanticSearchResult: "mars"));

        NasaSearchResultDto result = await handler.Handle(
            new SemanticSearchNasaImagesQuery("mars", null, null, null, null, null, 1, 24),
            CancellationToken.None);

        Assert.True(result.Degraded);
        Assert.Equal(SemanticSearchDegradationReasons.PartialNasaFailure, result.DegradationReason);
        Assert.Single(result.Images);
        Assert.Equal(2, nasaAttempts);
    }

    [Fact]
    public async Task Handle_throws_typed_upstream_error_when_every_nasa_call_fails()
    {
        StubNasaApiService nasaApiService = new(_ => throw new HttpRequestException("NASA unavailable"));
        SemanticSearchPlan plan = new(
            "mars horizon",
            "red planet landscape",
            ["mars", "horizon"],
            [],
            null,
            null,
            null,
            null,
            null,
            "en",
            0.8,
            "test-model",
            "prompt-v2");
        SemanticSearchNasaImagesQueryHandler handler = CreateHandler(
            nasaApiService,
            new StubAiEnrichmentService(semanticSearchPlan: plan));

        await Assert.ThrowsAsync<UpstreamServiceUnavailableException>(() => handler.Handle(
            new SemanticSearchNasaImagesQuery("mars", null, null, null, null, null, 1, 24),
            CancellationToken.None));

        Assert.Equal(6, nasaApiService.CriteriaCalls.Count);
    }

    [Fact]
    public async Task Handle_uses_alternative_required_and_excluded_terms_for_candidates_and_results()
    {
        SemanticSearchPlan plan = new(
            "mars sunset dust",
            "martian horizon dust",
            ["mars", "sunset"],
            ["dust"],
            null,
            null,
            null,
            null,
            null,
            "en",
            0.9,
            "test-model",
            "prompt-v2");
        StubNasaApiService nasaApiService = new(new NasaSearchResult(
            [
                CreateImage("clear", "Mars sunset", ["mars", "sunset"], description: "Clear horizon"),
                CreateImage("excluded", "Mars dust sunset", ["mars", "dust"], description: "Dust storm")
            ],
            2,
            1,
            100));
        SemanticSearchNasaImagesQueryHandler handler = CreateHandler(
            nasaApiService,
            new StubAiEnrichmentService(semanticSearchPlan: plan));

        NasaSearchResultDto result = await handler.Handle(
            new SemanticSearchNasaImagesQuery("mars pictures", null, null, null, null, null, 1, 24),
            CancellationToken.None);

        Assert.Equal(2, nasaApiService.CriteriaCalls.Select(criteria => criteria.Query).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(nasaApiService.CriteriaCalls, criteria =>
            Assert.DoesNotContain("dust", criteria.Query, StringComparison.OrdinalIgnoreCase));
        Assert.Equal("clear", Assert.Single(result.Images).NasaImageId);
    }

    [Fact]
    public async Task Handle_ranking_keeps_literal_component_weights_without_metadata_filters()
    {
        SemanticSearchPlan plan = new(
            "mars",
            null,
            ["mars"],
            [],
            null,
            null,
            null,
            null,
            null,
            "en",
            1,
            "test-model",
            "prompt-v2");
        StubNasaApiService nasaApiService = new(new NasaSearchResult(
            [
                CreateImage("title", "Mars", [], description: "unrelated"),
                CreateImage("keyword", "unrelated", ["mars"], description: "unrelated")
            ],
            2,
            1,
            100));
        SemanticSearchNasaImagesQueryHandler handler = CreateHandler(
            nasaApiService,
            new StubAiEnrichmentService(semanticSearchPlan: plan));

        NasaSearchResultDto result = await handler.Handle(
            new SemanticSearchNasaImagesQuery("mars", null, null, null, null, null, 1, 24),
            CancellationToken.None);

        NasaImageDto titleMatch = Assert.Single(result.Images, image => image.NasaImageId == "title");
        NasaImageDto keywordMatch = Assert.Single(result.Images, image => image.NasaImageId == "keyword");
        Assert.Equal(40, titleMatch.RelevanceScore);
        Assert.Equal(32.5, keywordMatch.RelevanceScore);
        Assert.Equal(["title", "nasa_position"], titleMatch.MatchReasons);
    }

    [Fact]
    public async Task Handle_reports_cache_degradation_and_does_not_issue_unusable_cursor()
    {
        StubSemanticSearchCache cache = new() { CacheUnavailable = true };
        SemanticSearchNasaImagesQueryHandler handler = CreateHandler(
            new StubNasaApiService(new NasaSearchResult(
                [
                    CreateImage("mars-1", "Mars one", ["mars"]),
                    CreateImage("mars-2", "Mars two", ["mars"])
                ],
                2,
                1,
                100)),
            new StubAiEnrichmentService(semanticSearchResult: "mars"),
            cache);

        NasaSearchResultDto result = await handler.Handle(
            new SemanticSearchNasaImagesQuery("mars", null, null, null, null, null, 1, 1),
            CancellationToken.None);

        Assert.True(result.Degraded);
        Assert.Equal(SemanticSearchDegradationReasons.CacheUnavailable, result.DegradationReason);
        Assert.Null(result.NextCursor);
    }

    private static SemanticSearchNasaImagesQueryHandler CreateHandler(
        StubNasaApiService nasaApiService,
        StubAiEnrichmentService aiService,
        StubSemanticSearchCache? cache = null)
    {
        return new SemanticSearchNasaImagesQueryHandler(
            nasaApiService,
            aiService,
            cache ?? new StubSemanticSearchCache(),
            NullLogger<SemanticSearchNasaImagesQueryHandler>.Instance);
    }

    private static NasaImageAsset CreateImage(
        string id,
        string title,
        IReadOnlyCollection<string> keywords,
        string? mission = null,
        string? rover = null,
        string? camera = null,
        string? description = null)
    {
        return new NasaImageAsset(
            id,
            title,
            description ?? $"Description for {title}",
            "JPL",
            "image",
            $"https://images.test/{id}-thumb.jpg",
            $"https://images.test/{id}.jpg",
            $"https://images.nasa.gov/details/{id}",
            new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero),
            keywords,
            mission,
            rover,
            camera);
    }
}

internal sealed class StubNasaApiService : INasaApiService
{
    private readonly Func<NasaSearchCriteria, NasaSearchResult> _resultFactory;

    public StubNasaApiService(NasaSearchResult searchResult)
        : this(_ => searchResult)
    {
    }

    public StubNasaApiService(Func<NasaSearchCriteria, NasaSearchResult> resultFactory)
    {
        _resultFactory = resultFactory;
    }

    public List<NasaSearchCriteria> CriteriaCalls { get; } = [];

    public Task<NasaSearchResult> SearchImagesAsync(
        NasaSearchCriteria criteria,
        CancellationToken cancellationToken = default)
    {
        CriteriaCalls.Add(criteria);
        return Task.FromResult(_resultFactory(criteria));
    }

    public Task<IReadOnlyCollection<NasaAssetFile>> GetAssetFilesAsync(
        string nasaImageId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyCollection<NasaAssetFile>>([]);
    }
}

internal sealed class StubSemanticSearchCache : ISemanticSearchCache
{
    private readonly Dictionary<string, SemanticSearchPlan> _plans = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SemanticSearchPool> _poolsByFingerprint = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SemanticSearchPool> _poolsBySearchId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SemanticSearchCursorState> _cursors = new(StringComparer.Ordinal);

    public bool CacheUnavailable { get; init; }

    public List<string> PlanFingerprints { get; } = [];

    public async Task<SemanticSearchCacheResult<SemanticSearchPlan>> GetOrCreatePlanAsync(
        string fingerprint,
        Func<CancellationToken, Task<SemanticSearchPlan>> factory,
        CancellationToken cancellationToken = default)
    {
        PlanFingerprints.Add(fingerprint);
        bool hit = _plans.TryGetValue(fingerprint, out SemanticSearchPlan? plan);
        if (!hit)
        {
            plan = await factory(cancellationToken);
            _plans[fingerprint] = plan;
        }

        return new SemanticSearchCacheResult<SemanticSearchPlan>(plan!, CacheUnavailable, hit);
    }

    public async Task<SemanticSearchCacheResult<SemanticSearchPool>> GetOrCreatePoolAsync(
        string fingerprint,
        Func<CancellationToken, Task<SemanticSearchPool>> factory,
        CancellationToken cancellationToken = default)
    {
        bool hit = _poolsByFingerprint.TryGetValue(fingerprint, out SemanticSearchPool? pool);
        if (!hit)
        {
            pool = await factory(cancellationToken);
            _poolsByFingerprint[fingerprint] = pool;
            _poolsBySearchId[pool.SearchId] = pool;
        }

        return new SemanticSearchCacheResult<SemanticSearchPool>(pool!, CacheUnavailable, hit);
    }

    public Task<SemanticSearchCursorLookupResult> ResolveCursorAsync(
        string cursor,
        CancellationToken cancellationToken = default)
    {
        if (_cursors.TryGetValue(cursor, out SemanticSearchCursorState? state)
            && _poolsBySearchId.TryGetValue(state.SearchId, out SemanticSearchPool? pool))
        {
            return Task.FromResult(new SemanticSearchCursorLookupResult(state, pool, CacheUnavailable));
        }

        return Task.FromResult(new SemanticSearchCursorLookupResult(null, null, CacheUnavailable));
    }

    public Task<SemanticSearchCacheWriteResult> StoreCursorAsync(
        string cursor,
        SemanticSearchCursorState state,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        _cursors[cursor] = state;
        return Task.FromResult(new SemanticSearchCacheWriteResult(CacheUnavailable));
    }
}
