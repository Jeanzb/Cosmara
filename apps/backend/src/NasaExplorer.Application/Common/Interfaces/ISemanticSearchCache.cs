using NasaExplorer.Application.Features.Search;
using NasaExplorer.Domain.Models.Ai;

namespace NasaExplorer.Application.Common.Interfaces;

public interface ISemanticSearchCache
{
    Task<SemanticSearchCacheResult<SemanticSearchPlan>> GetOrCreatePlanAsync(
        string fingerprint,
        Func<CancellationToken, Task<SemanticSearchPlan>> factory,
        CancellationToken cancellationToken = default);

    Task<SemanticSearchCacheResult<SemanticSearchPool>> GetOrCreatePoolAsync(
        string fingerprint,
        Func<CancellationToken, Task<SemanticSearchPool>> factory,
        CancellationToken cancellationToken = default);

    Task<SemanticSearchCursorLookupResult> ResolveCursorAsync(
        string cursor,
        CancellationToken cancellationToken = default);

    Task<SemanticSearchCacheWriteResult> StoreCursorAsync(
        string cursor,
        SemanticSearchCursorState state,
        TimeSpan ttl,
        CancellationToken cancellationToken = default);
}

public sealed record SemanticSearchCacheResult<T>(
    T Value,
    bool CacheUnavailable,
    bool Hit = false);

public sealed record SemanticSearchCursorLookupResult(
    SemanticSearchCursorState? Cursor,
    SemanticSearchPool? Pool,
    bool CacheUnavailable);

public sealed record SemanticSearchCacheWriteResult(bool CacheUnavailable);
