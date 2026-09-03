using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using NasaExplorer.Application.Common.Interfaces;
using NasaExplorer.Application.Features.Search;
using NasaExplorer.Domain.Models.Ai;
using NasaExplorer.Domain.Models.Nasa;
using System.Collections.Concurrent;
using System.Text.Json;

namespace NasaExplorer.Infrastructure.ExternalServices;

public sealed class SemanticSearchCache : ISemanticSearchCache
{
    private static readonly TimeSpan PlanTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan PoolTtl = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan SharedFactoryTimeout = TimeSpan.FromSeconds(40);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly ConcurrentDictionary<string, SharedFlight<SemanticSearchCacheResult<SemanticSearchPlan>>> PlanFlights = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, SharedFlight<SemanticSearchCacheResult<SemanticSearchPool>>> PoolFlights = new(StringComparer.Ordinal);

    private readonly IDistributedCache _cache;
    private readonly ILogger<SemanticSearchCache> _logger;

    public SemanticSearchCache(IDistributedCache cache, ILogger<SemanticSearchCache> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task<SemanticSearchCacheResult<SemanticSearchPlan>> GetOrCreatePlanAsync(
        string fingerprint,
        Func<CancellationToken, Task<SemanticSearchPlan>> factory,
        CancellationToken cancellationToken = default)
    {
        string cacheKey = $"semantic:plan:v1:{fingerprint}";
        CacheReadResult<SemanticSearchPlan> cached = await ReadAsync<SemanticSearchPlan>(cacheKey, "plan", cancellationToken);

        if (cached.Value is not null)
        {
            return new SemanticSearchCacheResult<SemanticSearchPlan>(cached.Value, cached.CacheUnavailable, Hit: true);
        }

        SemanticSearchCacheResult<SemanticSearchPlan> result = await RunSingleFlightAsync(
            PlanFlights,
            fingerprint,
            async factoryCancellationToken =>
            {
                CacheReadResult<SemanticSearchPlan> secondRead = await ReadAsync<SemanticSearchPlan>(cacheKey, "plan", factoryCancellationToken);
                if (secondRead.Value is not null)
                {
                    return new SemanticSearchCacheResult<SemanticSearchPlan>(secondRead.Value, secondRead.CacheUnavailable, Hit: true);
                }

                SemanticSearchPlan plan = await factory(factoryCancellationToken);
                bool writeUnavailable = await WriteAsync(cacheKey, plan, PlanTtl, "plan", factoryCancellationToken);

                return new SemanticSearchCacheResult<SemanticSearchPlan>(
                    plan,
                    secondRead.CacheUnavailable || writeUnavailable,
                    Hit: false);
            },
            cancellationToken);

        return result with { CacheUnavailable = result.CacheUnavailable || cached.CacheUnavailable };
    }

    public async Task<SemanticSearchCacheResult<SemanticSearchPool>> GetOrCreatePoolAsync(
        string fingerprint,
        Func<CancellationToken, Task<SemanticSearchPool>> factory,
        CancellationToken cancellationToken = default)
    {
        CacheReadResult<SemanticSearchPool> cached = await ReadPoolAsync(fingerprint, cancellationToken);
        if (cached.Value is not null)
        {
            return new SemanticSearchCacheResult<SemanticSearchPool>(cached.Value, cached.CacheUnavailable, Hit: true);
        }

        SemanticSearchCacheResult<SemanticSearchPool> result = await RunSingleFlightAsync(
            PoolFlights,
            fingerprint,
            async factoryCancellationToken =>
            {
                CacheReadResult<SemanticSearchPool> secondRead = await ReadPoolAsync(fingerprint, factoryCancellationToken);
                if (secondRead.Value is not null)
                {
                    return new SemanticSearchCacheResult<SemanticSearchPool>(secondRead.Value, secondRead.CacheUnavailable, Hit: true);
                }

                SemanticSearchPool pool = await factory(factoryCancellationToken);
                string poolKey = BuildPoolKey(pool.SearchId);
                string indexKey = BuildPoolIndexKey(fingerprint);
                bool poolWriteUnavailable = await WriteAsync(poolKey, pool, PoolTtl, "pool", factoryCancellationToken);
                bool indexWriteUnavailable = await WriteAsync(
                    indexKey,
                    new PoolIndex(pool.SearchId),
                    PoolTtl,
                    "pool_index",
                    factoryCancellationToken);

                return new SemanticSearchCacheResult<SemanticSearchPool>(
                    pool,
                    secondRead.CacheUnavailable || poolWriteUnavailable || indexWriteUnavailable,
                    Hit: false);
            },
            cancellationToken);

        return result with { CacheUnavailable = result.CacheUnavailable || cached.CacheUnavailable };
    }

    public async Task<SemanticSearchCursorLookupResult> ResolveCursorAsync(
        string cursor,
        CancellationToken cancellationToken = default)
    {
        CacheReadResult<SemanticSearchCursorState> cursorRead = await ReadAsync<SemanticSearchCursorState>(
            BuildCursorKey(cursor),
            "cursor",
            cancellationToken);

        if (cursorRead.Value is null)
        {
            return new SemanticSearchCursorLookupResult(null, null, cursorRead.CacheUnavailable);
        }

        CacheReadResult<SemanticSearchPool> poolRead = await ReadAsync<SemanticSearchPool>(
            BuildPoolKey(cursorRead.Value.SearchId),
            "pool",
            cancellationToken);

        if (poolRead.Value is null || poolRead.Value.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            return new SemanticSearchCursorLookupResult(
                cursorRead.Value,
                null,
                cursorRead.CacheUnavailable || poolRead.CacheUnavailable);
        }

        return new SemanticSearchCursorLookupResult(
            cursorRead.Value,
            poolRead.Value,
            cursorRead.CacheUnavailable || poolRead.CacheUnavailable);
    }

    public async Task<SemanticSearchCacheWriteResult> StoreCursorAsync(
        string cursor,
        SemanticSearchCursorState state,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        if (ttl <= TimeSpan.Zero)
        {
            return new SemanticSearchCacheWriteResult(false);
        }

        bool cacheUnavailable = await WriteAsync(
            BuildCursorKey(cursor),
            state,
            ttl,
            "cursor",
            cancellationToken);

        return new SemanticSearchCacheWriteResult(cacheUnavailable);
    }

    private async Task<CacheReadResult<SemanticSearchPool>> ReadPoolAsync(
        string fingerprint,
        CancellationToken cancellationToken)
    {
        CacheReadResult<PoolIndex> indexRead = await ReadAsync<PoolIndex>(
            BuildPoolIndexKey(fingerprint),
            "pool_index",
            cancellationToken);

        if (indexRead.Value is null)
        {
            return new CacheReadResult<SemanticSearchPool>(null, indexRead.CacheUnavailable);
        }

        CacheReadResult<SemanticSearchPool> poolRead = await ReadAsync<SemanticSearchPool>(
            BuildPoolKey(indexRead.Value.SearchId),
            "pool",
            cancellationToken);

        if (poolRead.Value is not null && poolRead.Value.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            return new CacheReadResult<SemanticSearchPool>(
                null,
                indexRead.CacheUnavailable || poolRead.CacheUnavailable);
        }

        return new CacheReadResult<SemanticSearchPool>(
            poolRead.Value,
            indexRead.CacheUnavailable || poolRead.CacheUnavailable);
    }

    private async Task<CacheReadResult<T>> ReadAsync<T>(
        string cacheKey,
        string cacheKind,
        CancellationToken cancellationToken)
        where T : class
    {
        string? cachedJson;
        try
        {
            cachedJson = await _cache.GetStringAsync(cacheKey, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Semantic cache read failed for {CacheKind}.", cacheKind);
            return new CacheReadResult<T>(null, true);
        }

        if (string.IsNullOrWhiteSpace(cachedJson))
        {
            return new CacheReadResult<T>(null, false);
        }

        try
        {
            T? value = JsonSerializer.Deserialize<T>(cachedJson, JsonOptions);
            if (value is null || !IsValidCacheValue(value))
            {
                _logger.LogWarning("Semantic cache entry was invalid for {CacheKind}; treating it as a miss.", cacheKind);
                return new CacheReadResult<T>(null, false);
            }

            return new CacheReadResult<T>(value, false);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            _logger.LogWarning(exception, "Semantic cache entry was corrupt for {CacheKind}; treating it as a miss.", cacheKind);
            return new CacheReadResult<T>(null, false);
        }
    }

    private static bool IsValidCacheValue<T>(T value)
        where T : class
    {
        return value switch
        {
            SemanticSearchPlan plan => IsValidPlan(plan),
            SemanticSearchPool pool => IsValidPool(pool),
            SemanticSearchCursorState cursor => !string.IsNullOrWhiteSpace(cursor.SearchId)
                && cursor.Offset >= 0
                && cursor.Page >= 1,
            PoolIndex index => !string.IsNullOrWhiteSpace(index.SearchId),
            _ => true
        };
    }

    private static bool IsValidPlan(SemanticSearchPlan plan)
    {
        return !string.IsNullOrWhiteSpace(plan.PrimaryQuery)
            && plan.PrimaryQuery.Length <= 240
            && IsValidOptionalText(plan.AlternativeQuery, 240)
            && IsValidStringCollection(plan.RequiredTerms, 8)
            && IsValidStringCollection(plan.ExcludedTerms, 8)
            && (!plan.DateFrom.HasValue || !plan.DateTo.HasValue || plan.DateFrom <= plan.DateTo)
            && IsValidOptionalText(plan.Rover, 120)
            && IsValidOptionalText(plan.Camera, 120)
            && IsValidOptionalText(plan.Mission, 120)
            && (string.Equals(plan.Locale, "en", StringComparison.OrdinalIgnoreCase)
                || string.Equals(plan.Locale, "es", StringComparison.OrdinalIgnoreCase))
            && double.IsFinite(plan.Confidence)
            && plan.Confidence is >= 0 and <= 1
            && !string.IsNullOrWhiteSpace(plan.Model)
            && !string.IsNullOrWhiteSpace(plan.PromptVersion)
            && IsValidOptionalText(plan.DegradationReason, 120);
    }

    private static bool IsValidPool(SemanticSearchPool pool)
    {
        if (string.IsNullOrWhiteSpace(pool.SearchId)
            || pool.InterpretedQuery is null
            || pool.InterpretedQuery.Length > 240
            || !IsValidFilters(pool.AppliedFilters)
            || pool.Images is null
            || pool.Images.Count > 200
            || pool.Images.Any(image => !IsValidRankedImage(image))
            || pool.ExpiresAtUtc <= DateTimeOffset.MinValue
            || !IsValidOptionalText(pool.DegradationReason, 120)
            || string.IsNullOrWhiteSpace(pool.Model)
            || string.IsNullOrWhiteSpace(pool.PromptVersion)
            || pool.NasaCallCount is < 0 or > 6
            || pool.NasaLatencyMs < 0)
        {
            return false;
        }

        return pool.Images
            .Select(image => image.Image.NasaImageId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count() == pool.Images.Count;
    }

    private static bool IsValidFilters(SemanticSearchEffectiveFilters? filters)
    {
        return filters is not null
            && (!filters.DateFrom.HasValue || !filters.DateTo.HasValue || filters.DateFrom <= filters.DateTo)
            && IsValidOptionalText(filters.Rover, 120)
            && IsValidOptionalText(filters.Camera, 120)
            && IsValidOptionalText(filters.Mission, 120)
            && IsValidStringCollection(filters.Inferred, 5)
            && filters.Inferred.Distinct(StringComparer.OrdinalIgnoreCase).Count() == filters.Inferred.Count;
    }

    private static bool IsValidRankedImage(SemanticSearchRankedImage? rankedImage)
    {
        return rankedImage is not null
            && IsValidImage(rankedImage.Image)
            && double.IsFinite(rankedImage.RelevanceScore)
            && rankedImage.RelevanceScore is >= 0 and <= 100
            && IsValidStringCollection(rankedImage.MatchReasons, 5)
            && rankedImage.OriginalPosition >= 0;
    }

    private static bool IsValidImage(NasaImageAsset? image)
    {
        return image is not null
            && !string.IsNullOrWhiteSpace(image.NasaImageId)
            && !string.IsNullOrWhiteSpace(image.Title)
            && !string.IsNullOrWhiteSpace(image.MediaType)
            && !string.IsNullOrWhiteSpace(image.ThumbnailUrl)
            && !string.IsNullOrWhiteSpace(image.ImageUrl)
            && !string.IsNullOrWhiteSpace(image.AspectRatio)
            && IsValidStringCollection(image.Keywords, int.MaxValue);
    }

    private static bool IsValidStringCollection(IReadOnlyCollection<string>? values, int maximumCount)
    {
        return values is not null
            && values.Count <= maximumCount
            && values.All(value => !string.IsNullOrWhiteSpace(value));
    }

    private static bool IsValidOptionalText(string? value, int maximumLength)
    {
        return value is null || (!string.IsNullOrWhiteSpace(value) && value.Length <= maximumLength);
    }

    private async Task<bool> WriteAsync<T>(
        string cacheKey,
        T value,
        TimeSpan ttl,
        string cacheKind,
        CancellationToken cancellationToken)
    {
        try
        {
            await _cache.SetStringAsync(
                cacheKey,
                JsonSerializer.Serialize(value, JsonOptions),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = ttl
                },
                cancellationToken);

            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Semantic cache write failed for {CacheKind}.", cacheKind);
            return true;
        }
    }

    private static async Task<T> RunSingleFlightAsync<T>(
        ConcurrentDictionary<string, SharedFlight<T>> flights,
        string fingerprint,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            SharedFlight<T> candidate = new();
            SharedFlight<T> flight = flights.GetOrAdd(fingerprint, candidate);
            bool ownsFlight = ReferenceEquals(flight, candidate);

            if (!ownsFlight)
            {
                candidate.Dispose();
            }

            if (!flight.TryAddWaiter())
            {
                RemoveFlight(flights, fingerprint, flight);
                continue;
            }

            if (ownsFlight)
            {
                _ = CompleteSharedFlightAsync(flights, fingerprint, flight, factory);
            }

            try
            {
                return await flight.Task.WaitAsync(cancellationToken);
            }
            finally
            {
                if (flight.ReleaseWaiter())
                {
                    RemoveFlight(flights, fingerprint, flight);
                    flight.CancelFactory();
                }
            }
        }
    }

    private static void RemoveFlight<T>(
        ConcurrentDictionary<string, SharedFlight<T>> flights,
        string fingerprint,
        SharedFlight<T> flight)
    {
        ICollection<KeyValuePair<string, SharedFlight<T>>> entries = flights;
        entries.Remove(new KeyValuePair<string, SharedFlight<T>>(fingerprint, flight));
    }

    private static async Task CompleteSharedFlightAsync<T>(
        ConcurrentDictionary<string, SharedFlight<T>> flights,
        string fingerprint,
        SharedFlight<T> flight,
        Func<CancellationToken, Task<T>> factory)
    {
        try
        {
            T result = await factory(flight.FactoryCancellationToken);
            flight.TrySetResult(result);
        }
        catch (OperationCanceledException)
        {
            flight.TrySetCanceled();
        }
        catch (Exception exception)
        {
            flight.TrySetException(exception);
        }
        finally
        {
            RemoveFlight(flights, fingerprint, flight);
            flight.Dispose();
        }
    }

    private static string BuildPoolIndexKey(string fingerprint) => $"semantic:pool-index:v1:{fingerprint}";

    private static string BuildPoolKey(string searchId) => $"semantic:pool:v1:{searchId}";

    private static string BuildCursorKey(string cursor) => $"semantic:cursor:v1:{cursor}";

    private sealed record PoolIndex(string SearchId);

    private sealed class SharedFlight<T> : IDisposable
    {
        private readonly object _waiterLock = new();
        private readonly CancellationTokenSource _factoryCancellation = new(SharedFactoryTimeout);
        private readonly CancellationToken _factoryCancellationToken;
        private readonly TaskCompletionSource<T> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _waiterCount;
        private bool _acceptingWaiters = true;
        private int _disposed;

        public SharedFlight()
        {
            _factoryCancellationToken = _factoryCancellation.Token;
        }

        public Task<T> Task => _completion.Task;

        public CancellationToken FactoryCancellationToken => _factoryCancellationToken;

        public bool TryAddWaiter()
        {
            lock (_waiterLock)
            {
                if (!_acceptingWaiters)
                {
                    return false;
                }

                _waiterCount += 1;
                return true;
            }
        }

        public bool ReleaseWaiter()
        {
            lock (_waiterLock)
            {
                _waiterCount -= 1;
                if (_waiterCount == 0 && !_completion.Task.IsCompleted)
                {
                    _acceptingWaiters = false;
                    return true;
                }

                return false;
            }
        }

        public void CancelFactory()
        {
            try
            {
                _factoryCancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        public void TrySetResult(T result)
        {
            _completion.TrySetResult(result);
        }

        public void TrySetCanceled()
        {
            _completion.TrySetCanceled(_factoryCancellationToken);
        }

        public void TrySetException(Exception exception)
        {
            _completion.TrySetException(exception);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _factoryCancellation.Dispose();
            }
        }
    }

    private sealed record CacheReadResult<T>(T? Value, bool CacheUnavailable)
        where T : class;
}
