using MediatR;
using Microsoft.Extensions.Logging;
using NasaExplorer.Application.Common.Exceptions;
using NasaExplorer.Application.Common.Interfaces;
using NasaExplorer.Application.DTOs.Search;
using NasaExplorer.Domain.Interfaces.Services;
using NasaExplorer.Domain.Models.Ai;
using NasaExplorer.Domain.Models.Nasa;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NasaExplorer.Application.Features.Search.Queries.SemanticSearchNasaImages;

public sealed class SemanticSearchNasaImagesQueryHandler : IRequestHandler<SemanticSearchNasaImagesQuery, NasaSearchResultDto>
{
    private const int MaximumCandidateQueries = 2;
    private const int MaximumNasaPagesPerCandidate = 3;
    private const int NasaFetchPageSize = 100;
    private const int MaximumPoolSize = 200;
    private static readonly TimeSpan PoolTtl = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan NasaRequestBudget = TimeSpan.FromSeconds(25);
    private static readonly IReadOnlySet<string> AllowedSuppressedInferences = new HashSet<string>(
        ["dateFrom", "dateTo", "rover", "camera", "mission"],
        StringComparer.OrdinalIgnoreCase);

    private readonly INasaApiService _nasaApiService;
    private readonly IAiEnrichmentService _aiEnrichmentService;
    private readonly ISemanticSearchCache _semanticSearchCache;
    private readonly ILogger<SemanticSearchNasaImagesQueryHandler> _logger;

    public SemanticSearchNasaImagesQueryHandler(
        INasaApiService nasaApiService,
        IAiEnrichmentService aiEnrichmentService,
        ISemanticSearchCache semanticSearchCache,
        ILogger<SemanticSearchNasaImagesQueryHandler> logger)
    {
        _nasaApiService = nasaApiService;
        _aiEnrichmentService = aiEnrichmentService;
        _semanticSearchCache = semanticSearchCache;
        _logger = logger;
    }

    public async Task<NasaSearchResultDto> Handle(
        SemanticSearchNasaImagesQuery request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.Cursor))
        {
            return await ContinueSearchAsync(request, cancellationToken);
        }

        string originalQuery = request.Query!.Trim();
        string locale = NormalizeLocale(request.Locale);
        IReadOnlySet<string> suppressedInferences = ParseSuppressedInferences(request.SuppressInferred);
        SemanticSearchPlannerIdentity plannerIdentity = _aiEnrichmentService.SemanticSearchIdentity;
        string planFingerprint = CreateFingerprint(
            "plan-v2",
            SemanticSearchQueryNormalizer.Normalize(originalQuery),
            request.DateFrom?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            request.DateTo?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            request.Rover,
            request.Camera,
            request.Mission,
            locale,
            plannerIdentity.Model,
            plannerIdentity.PromptVersion);
        string poolFingerprint = CreateFingerprint(
            "pool-v2",
            planFingerprint,
            string.Join(',', suppressedInferences.OrderBy(value => value, StringComparer.OrdinalIgnoreCase)));

        SemanticSearchCacheResult<SemanticSearchPool> poolResult = await _semanticSearchCache.GetOrCreatePoolAsync(
            poolFingerprint,
            async poolCancellationToken =>
            {
                SemanticSearchCacheResult<SemanticSearchPlan> planResult = await _semanticSearchCache.GetOrCreatePlanAsync(
                    planFingerprint,
                    token => _aiEnrichmentService.CreateSemanticSearchPlanAsync(originalQuery, locale, token),
                    poolCancellationToken);
                SemanticSearchPlan preparedPlan = PreparePlan(planResult.Value, originalQuery, locale);
                SemanticSearchEffectiveFilters effectiveFilters = ApplyFilters(
                    preparedPlan,
                    request,
                    suppressedInferences);

                return await BuildPoolAsync(
                    originalQuery,
                    preparedPlan,
                    effectiveFilters,
                    planResult.CacheUnavailable,
                    planResult.Hit,
                    poolCancellationToken);
            },
            cancellationToken);

        LogSearchMetric(poolResult.Value, poolResult.Hit, poolResult.CacheUnavailable);

        int offset = checked((request.Page - 1) * request.PageSize);

        return await BuildResponseAsync(
            poolResult.Value,
            offset,
            request.Page,
            request.PageSize,
            poolResult.CacheUnavailable,
            cancellationToken);
    }

    private async Task<NasaSearchResultDto> ContinueSearchAsync(
        SemanticSearchNasaImagesQuery request,
        CancellationToken cancellationToken)
    {
        SemanticSearchCursorLookupResult lookup = await _semanticSearchCache.ResolveCursorAsync(
            request.Cursor!.Trim(),
            cancellationToken);

        if (lookup.Cursor is null || lookup.Pool is null)
        {
            throw new ExpiredCursorException();
        }

        if (lookup.Cursor.Offset < 0 || lookup.Cursor.Offset > lookup.Pool.Images.Count || lookup.Cursor.Page < 1)
        {
            throw new ExpiredCursorException();
        }

        LogSearchMetric(lookup.Pool, poolCacheHit: true, lookup.CacheUnavailable);

        return await BuildResponseAsync(
            lookup.Pool,
            lookup.Cursor.Offset,
            lookup.Cursor.Page,
            request.PageSize,
            lookup.CacheUnavailable,
            cancellationToken);
    }

    private async Task<SemanticSearchPool> BuildPoolAsync(
        string originalQuery,
        SemanticSearchPlan plan,
        SemanticSearchEffectiveFilters filters,
        bool planCacheUnavailable,
        bool planCacheHit,
        CancellationToken cancellationToken)
    {
        string searchId = CreateOpaqueId();
        IReadOnlyCollection<string> candidates = BuildCandidateQueries(originalQuery, plan);
        if (candidates.Count == 0)
        {
            candidates = ["nasa"];
        }
        List<SemanticSearchPositionedImage> sourceImages = [];
        int originalPosition = 0;
        int nasaCalls = 0;
        int successfulNasaCalls = 0;
        long nasaLatencyMs = 0;
        bool partialNasaFailure = false;
        bool nasaBudgetExhausted = false;
        using CancellationTokenSource nasaBudgetSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        nasaBudgetSource.CancelAfter(NasaRequestBudget);

        foreach ((string candidate, int candidateIndex) in candidates.Select((value, index) => (value, index)))
        {
            for (int page = 1; page <= MaximumNasaPagesPerCandidate; page += 1)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (nasaBudgetExhausted)
                {
                    break;
                }

                nasaCalls += 1;
                Stopwatch nasaCallStopwatch = Stopwatch.StartNew();

                try
                {
                    NasaSearchResult result = await _nasaApiService.SearchImagesAsync(
                        new NasaSearchCriteria(
                            candidate,
                            filters.DateFrom,
                            filters.DateTo,
                            filters.Rover,
                            filters.Camera,
                            filters.Mission,
                            page,
                            NasaFetchPageSize,
                            PageScanLimit: 1),
                        nasaBudgetSource.Token);
                    successfulNasaCalls += 1;

                    foreach (NasaImageAsset image in result.Images)
                    {
                        sourceImages.Add(new SemanticSearchPositionedImage(image, originalPosition));
                        originalPosition += 1;
                    }

                    bool mustContinueDateScan = filters.DateFrom.HasValue || filters.DateTo.HasValue;
                    if (!mustContinueDateScan && result.Images.Count < NasaFetchPageSize)
                    {
                        break;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException exception) when (nasaBudgetSource.IsCancellationRequested)
                {
                    partialNasaFailure = true;
                    nasaBudgetExhausted = true;
                    _logger.LogWarning(
                        exception,
                        "Semantic NASA budget exhausted for {SearchId} after {NasaCallCount} calls; returning any successful partial results.",
                        searchId,
                        nasaCalls);
                }
                catch (Exception exception) when (IsRecoverableNasaFailure(exception))
                {
                    partialNasaFailure = true;
                    _logger.LogWarning(
                        "Semantic NASA request failed for {SearchId}, candidate {CandidateIndex}, page {Page}, failure type {FailureType}; continuing within the request budget.",
                        searchId,
                        candidateIndex,
                        page,
                        exception.GetType().Name);
                }
                finally
                {
                    nasaCallStopwatch.Stop();
                    nasaLatencyMs += nasaCallStopwatch.ElapsedMilliseconds;
                }
            }

            if (nasaBudgetExhausted)
            {
                break;
            }
        }

        if (successfulNasaCalls == 0)
        {
            _logger.LogWarning(
                "Semantic NASA upstream failed completely for {SearchId} with {AiModel}, {PromptVersion}, planCacheHit={PlanCacheHit}, after {NasaCallCount} calls, {NasaLatencyMs} ms, and {ResultCount} results.",
                searchId,
                plan.Model,
                plan.PromptVersion,
                planCacheHit,
                nasaCalls,
                nasaLatencyMs,
                0);
            throw new UpstreamServiceUnavailableException();
        }

        IReadOnlyCollection<SemanticSearchRankedImage> rankedImages = SemanticSearchRanker
            .Rank(sourceImages, plan, filters)
            .Take(MaximumPoolSize)
            .ToArray();
        string? degradationReason = ResolvePoolDegradationReason(
            plan.DegradationReason,
            planCacheUnavailable,
            partialNasaFailure);
        bool degraded = plan.Degraded || planCacheUnavailable || partialNasaFailure;

        _logger.LogInformation(
            "Semantic search pool {SearchId} built with {AiModel}, {PromptVersion}, planCacheHit={PlanCacheHit}, {CandidateCount} candidates, {NasaCallCount} NASA calls, {NasaLatencyMs} ms NASA latency, {ResultCount} ranked results, and degraded={Degraded}.",
            searchId,
            plan.Model,
            plan.PromptVersion,
            planCacheHit,
            candidates.Count,
            nasaCalls,
            nasaLatencyMs,
            rankedImages.Count,
            degraded);

        return new SemanticSearchPool(
            searchId,
            plan.InterpretedQuery,
            filters,
            rankedImages,
            degraded,
            degradationReason,
            DateTimeOffset.UtcNow.Add(PoolTtl),
            plan.Model,
            plan.PromptVersion,
            planCacheHit,
            nasaCalls,
            nasaLatencyMs);
    }

    private async Task<NasaSearchResultDto> BuildResponseAsync(
        SemanticSearchPool pool,
        int offset,
        int page,
        int pageSize,
        bool cacheUnavailable,
        CancellationToken cancellationToken)
    {
        SemanticSearchRankedImage[] pageImages = pool.Images
            .Skip(offset)
            .Take(pageSize)
            .ToArray();
        int nextOffset = offset + pageImages.Length;
        string? nextCursor = null;

        if (nextOffset < pool.Images.Count && !cacheUnavailable)
        {
            string cursor = CreateOpaqueId();
            TimeSpan remainingTtl = pool.ExpiresAtUtc - DateTimeOffset.UtcNow;
            SemanticSearchCacheWriteResult cursorWrite = await _semanticSearchCache.StoreCursorAsync(
                cursor,
                new SemanticSearchCursorState(pool.SearchId, nextOffset, page + 1),
                remainingTtl,
                cancellationToken);

            cacheUnavailable = cursorWrite.CacheUnavailable;
            if (!cursorWrite.CacheUnavailable)
            {
                nextCursor = cursor;
            }
        }

        NasaSearchResultDto response = NasaSearchResultMapper.ToDto(
            new NasaSearchResult(
                pageImages.Select(item => item.Image).ToArray(),
                pool.Images.Count,
                page,
                pageSize));
        IReadOnlyDictionary<string, SemanticSearchRankedImage> rankedById = pageImages.ToDictionary(
            item => item.Image.NasaImageId,
            StringComparer.OrdinalIgnoreCase);

        foreach (NasaImageDto image in response.Images)
        {
            if (rankedById.TryGetValue(image.NasaImageId, out SemanticSearchRankedImage? rankedImage))
            {
                image.RelevanceScore = rankedImage.RelevanceScore;
                image.MatchReasons = rankedImage.MatchReasons;
            }
        }

        response.SearchId = pool.SearchId;
        response.Mode = "semantic";
        response.InterpretedQuery = pool.InterpretedQuery;
        response.AppliedFilters = ToDto(pool.AppliedFilters);
        response.Degraded = pool.Degraded || cacheUnavailable;
        response.DegradationReason = cacheUnavailable
            ? SemanticSearchDegradationReasons.CacheUnavailable
            : pool.DegradationReason;
        response.NextCursor = nextCursor;
        response.RelaxationSuggestions = pool.Images.Count == 0
            ? BuildRelaxationSuggestions(pool.AppliedFilters)
            : [];

        return response;
    }

    private SemanticSearchPlan PreparePlan(
        SemanticSearchPlan plan,
        string originalQuery,
        string locale)
    {
        string primaryQuery = SemanticSearchQueryNormalizer.Normalize(plan.PrimaryQuery);
        if (string.IsNullOrWhiteSpace(primaryQuery))
        {
            primaryQuery = SemanticSearchQueryNormalizer.Normalize(originalQuery);
        }

        string[] excludedTerms = plan.ExcludedTerms
            .Select(SemanticSearchQueryNormalizer.Normalize)
            .Where(term => !string.IsNullOrWhiteSpace(term))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();
        string[] requiredTerms = plan.RequiredTerms
            .Select(SemanticSearchQueryNormalizer.Normalize)
            .Where(term => !string.IsNullOrWhiteSpace(term))
            .Where(term => !excludedTerms.Contains(term, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();

        if (requiredTerms.Length == 0 && !string.IsNullOrWhiteSpace(primaryQuery))
        {
            requiredTerms = [primaryQuery];
        }

        return plan with
        {
            PrimaryQuery = primaryQuery,
            AlternativeQuery = NormalizeOptionalQuery(plan.AlternativeQuery),
            RequiredTerms = requiredTerms,
            ExcludedTerms = excludedTerms,
            Locale = locale,
            Rover = NormalizeOptional(plan.Rover),
            Camera = NormalizeOptional(plan.Camera),
            Mission = NormalizeOptional(plan.Mission),
            Model = string.IsNullOrWhiteSpace(plan.Model)
                ? _aiEnrichmentService.SemanticSearchIdentity.Model
                : plan.Model,
            PromptVersion = string.IsNullOrWhiteSpace(plan.PromptVersion)
                ? _aiEnrichmentService.SemanticSearchIdentity.PromptVersion
                : plan.PromptVersion
        };
    }

    private static SemanticSearchEffectiveFilters ApplyFilters(
        SemanticSearchPlan plan,
        SemanticSearchNasaImagesQuery request,
        IReadOnlySet<string> suppressedInferences)
    {
        List<string> inferred = [];
        DateOnly? dateFrom = ApplyInferredDate(request.DateFrom, plan.DateFrom, "dateFrom", suppressedInferences, inferred);
        DateOnly? dateTo = ApplyInferredDate(request.DateTo, plan.DateTo, "dateTo", suppressedInferences, inferred);
        string? rover = ApplyInferredText(request.Rover, plan.Rover, "rover", suppressedInferences, inferred);
        string? camera = ApplyInferredText(request.Camera, plan.Camera, "camera", suppressedInferences, inferred);
        string? mission = ApplyInferredText(request.Mission, plan.Mission, "mission", suppressedInferences, inferred);

        if (dateFrom.HasValue && dateTo.HasValue && dateFrom > dateTo)
        {
            if (request.DateFrom.HasValue)
            {
                dateTo = request.DateTo;
                inferred.Remove("dateTo");
            }
            else
            {
                dateFrom = request.DateFrom;
                inferred.Remove("dateFrom");
            }
        }

        return new SemanticSearchEffectiveFilters(dateFrom, dateTo, rover, camera, mission, inferred);
    }

    private static DateOnly? ApplyInferredDate(
        DateOnly? explicitValue,
        DateOnly? inferredValue,
        string key,
        IReadOnlySet<string> suppressedInferences,
        List<string> appliedInferences)
    {
        if (explicitValue.HasValue)
        {
            return explicitValue;
        }

        if (inferredValue.HasValue && !suppressedInferences.Contains(key))
        {
            appliedInferences.Add(key);
            return inferredValue;
        }

        return null;
    }

    private static string? ApplyInferredText(
        string? explicitValue,
        string? inferredValue,
        string key,
        IReadOnlySet<string> suppressedInferences,
        List<string> appliedInferences)
    {
        string? normalizedExplicitValue = NormalizeOptional(explicitValue);
        if (normalizedExplicitValue is not null)
        {
            return normalizedExplicitValue;
        }

        string? normalizedInferredValue = NormalizeOptional(inferredValue);
        if (normalizedInferredValue is not null && !suppressedInferences.Contains(key))
        {
            appliedInferences.Add(key);
            return normalizedInferredValue;
        }

        return null;
    }

    private static IReadOnlyCollection<RelaxationSuggestionDto> BuildRelaxationSuggestions(
        SemanticSearchEffectiveFilters filters)
    {
        List<RelaxationSuggestionDto> suggestions = [];

        if (filters.DateFrom.HasValue || filters.DateTo.HasValue)
        {
            suggestions.Add(new RelaxationSuggestionDto { Code = "remove_date_range", Filter = "dateRange" });
        }

        AddRelaxationSuggestion(suggestions, filters.Mission, "remove_mission", "mission");
        AddRelaxationSuggestion(suggestions, filters.Rover, "remove_rover", "rover");
        AddRelaxationSuggestion(suggestions, filters.Camera, "remove_camera", "camera");

        return suggestions;
    }

    private static void AddRelaxationSuggestion(
        List<RelaxationSuggestionDto> suggestions,
        string? appliedValue,
        string code,
        string filter)
    {
        if (!string.IsNullOrWhiteSpace(appliedValue))
        {
            suggestions.Add(new RelaxationSuggestionDto { Code = code, Filter = filter });
        }
    }

    private static AppliedFiltersDto ToDto(SemanticSearchEffectiveFilters filters)
    {
        return new AppliedFiltersDto
        {
            DateFrom = filters.DateFrom,
            DateTo = filters.DateTo,
            Rover = filters.Rover,
            Camera = filters.Camera,
            Mission = filters.Mission,
            Inferred = filters.Inferred
        };
    }

    private static IReadOnlyCollection<string> BuildCandidateQueries(
        string originalQuery,
        SemanticSearchPlan plan)
    {
        string primary = BuildOptimizedQuery(plan.PrimaryQuery, plan.RequiredTerms, plan.ExcludedTerms);
        string alternativeSource = string.IsNullOrWhiteSpace(plan.AlternativeQuery)
            ? originalQuery
            : plan.AlternativeQuery;
        string alternative = BuildOptimizedQuery(alternativeSource, plan.RequiredTerms, plan.ExcludedTerms);

        return SemanticSearchQueryNormalizer
            .BuildCandidateQueries(alternative, primary)
            .Take(MaximumCandidateQueries)
            .ToArray();
    }

    private static string BuildOptimizedQuery(
        string baseQuery,
        IReadOnlyCollection<string> requiredTerms,
        IReadOnlyCollection<string> excludedTerms)
    {
        string normalized = SemanticSearchQueryNormalizer.Normalize(
            string.Join(' ', requiredTerms.Prepend(baseQuery)));
        HashSet<string> excludedTokens = excludedTerms
            .SelectMany(term => SemanticSearchQueryNormalizer.Normalize(term)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        normalized = string.Join(
            ' ',
            normalized
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(term => !excludedTokens.Contains(term))
                .Distinct(StringComparer.OrdinalIgnoreCase));

        if (normalized.Length <= 240)
        {
            return normalized;
        }

        int boundary = normalized.LastIndexOf(' ', 239);
        return normalized[..(boundary > 0 ? boundary : 240)].Trim();
    }

    private static string? NormalizeOptionalQuery(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string normalized = SemanticSearchQueryNormalizer.Normalize(value);
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private void LogSearchMetric(
        SemanticSearchPool pool,
        bool poolCacheHit,
        bool cacheUnavailable)
    {
        _logger.LogInformation(
            "Semantic search metric: {SearchId} {AiModel} {PromptVersion} planCacheHit={PlanCacheHit}, poolCacheHit={PoolCacheHit}, {NasaCallCount} NASA calls, {NasaLatencyMs} ms NASA latency, {ResultCount} results, cacheUnavailable={CacheUnavailable}.",
            pool.SearchId,
            pool.Model,
            pool.PromptVersion,
            pool.PlanCacheHit,
            poolCacheHit,
            poolCacheHit ? 0 : pool.NasaCallCount,
            poolCacheHit ? 0 : pool.NasaLatencyMs,
            pool.Images.Count,
            cacheUnavailable);
    }

    private static IReadOnlySet<string> ParseSuppressedInferences(string? value)
    {
        return (value ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(AllowedSuppressedInferences.Contains)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeLocale(string? locale)
    {
        return locale?.StartsWith("es", StringComparison.OrdinalIgnoreCase) == true ? "es" : "en";
    }

    private static string? NormalizeOptional(string? value)
    {
        string? trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static bool IsRecoverableNasaFailure(Exception exception)
    {
        return exception is HttpRequestException or JsonException or OperationCanceledException;
    }

    private static string? ResolvePoolDegradationReason(
        string? planReason,
        bool cacheUnavailable,
        bool partialNasaFailure)
    {
        if (!string.IsNullOrWhiteSpace(planReason))
        {
            return planReason;
        }

        if (cacheUnavailable)
        {
            return SemanticSearchDegradationReasons.CacheUnavailable;
        }

        return partialNasaFailure ? SemanticSearchDegradationReasons.PartialNasaFailure : null;
    }

    private static string CreateFingerprint(params string?[] values)
    {
        string payload = string.Join(
            '\u001f',
            values.Select(value => string.Join(
                ' ',
                (value ?? string.Empty)
                    .Trim()
                    .ToLowerInvariant()
                    .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))));

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    private static string CreateOpaqueId()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(18)).ToLowerInvariant();
    }
}
