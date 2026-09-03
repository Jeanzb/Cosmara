using NasaExplorer.Domain.Models.Nasa;

namespace NasaExplorer.Application.Features.Search;

public sealed record SemanticSearchEffectiveFilters(
    DateOnly? DateFrom,
    DateOnly? DateTo,
    string? Rover,
    string? Camera,
    string? Mission,
    IReadOnlyCollection<string> Inferred);

public sealed record SemanticSearchRankedImage(
    NasaImageAsset Image,
    double RelevanceScore,
    IReadOnlyCollection<string> MatchReasons,
    int OriginalPosition);

public sealed record SemanticSearchPool(
    string SearchId,
    string InterpretedQuery,
    SemanticSearchEffectiveFilters AppliedFilters,
    IReadOnlyCollection<SemanticSearchRankedImage> Images,
    bool Degraded,
    string? DegradationReason,
    DateTimeOffset ExpiresAtUtc,
    string Model = "unknown",
    string PromptVersion = "unknown",
    bool PlanCacheHit = false,
    int NasaCallCount = 0,
    long NasaLatencyMs = 0);

public sealed record SemanticSearchCursorState(
    string SearchId,
    int Offset,
    int Page);
