using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using NasaExplorer.Application.Common.Interfaces;
using NasaExplorer.Application.Features.Search;
using NasaExplorer.Domain.Models.Ai;
using NasaExplorer.Infrastructure.ExternalServices;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace NasaExplorer.Infrastructure.Tests.ExternalServices;

public sealed class SemanticSearchCacheTests
{
    [Fact]
    public async Task GetOrCreatePlanAsync_single_flights_and_caches_plan_for_24_hours()
    {
        RecordingDistributedCache distributedCache = new();
        SemanticSearchCache cache = new(distributedCache, NullLogger<SemanticSearchCache>.Instance);
        int factoryCalls = 0;

        Task<SemanticSearchCacheResult<SemanticSearchPlan>>[] calls = Enumerable.Range(0, 8)
            .Select(_ => cache.GetOrCreatePlanAsync(
                "same-fingerprint",
                async cancellationToken =>
                {
                    Interlocked.Increment(ref factoryCalls);
                    await Task.Delay(50, cancellationToken);
                    return CreatePlan();
                }))
            .ToArray();

        SemanticSearchCacheResult<SemanticSearchPlan>[] results = await Task.WhenAll(calls);

        Assert.Equal(1, factoryCalls);
        Assert.All(results, result => Assert.Equal("mars", result.Value.InterpretedQuery));
        Assert.All(results, result => Assert.False(result.Hit));
        SemanticSearchCacheResult<SemanticSearchPlan> cachedResult = await cache.GetOrCreatePlanAsync(
            "same-fingerprint",
            _ => throw new InvalidOperationException("The cached plan should have been used."));
        Assert.True(cachedResult.Hit);
        DistributedCacheEntryOptions options = distributedCache.OptionsByKey["semantic:plan:v1:same-fingerprint"];
        Assert.Equal(TimeSpan.FromHours(24), options.AbsoluteExpirationRelativeToNow);
    }

    [Fact]
    public async Task Pool_and_opaque_cursor_resolve_from_the_15_minute_session()
    {
        RecordingDistributedCache distributedCache = new();
        SemanticSearchCache cache = new(distributedCache, NullLogger<SemanticSearchCache>.Instance);
        SemanticSearchPool pool = CreatePool("search-id");

        SemanticSearchCacheResult<SemanticSearchPool> poolResult = await cache.GetOrCreatePoolAsync(
            "pool-fingerprint",
            _ => Task.FromResult(pool));
        SemanticSearchCacheResult<SemanticSearchPool> cachedPoolResult = await cache.GetOrCreatePoolAsync(
            "pool-fingerprint",
            _ => throw new InvalidOperationException("The cached pool should have been used."));
        await cache.StoreCursorAsync(
            "opaque-cursor",
            new SemanticSearchCursorState(pool.SearchId, 24, 2),
            TimeSpan.FromMinutes(15));
        SemanticSearchCursorLookupResult cursorResult = await cache.ResolveCursorAsync("opaque-cursor");

        Assert.Equal(pool.SearchId, poolResult.Value.SearchId);
        Assert.False(poolResult.Hit);
        Assert.True(cachedPoolResult.Hit);
        Assert.Equal(24, cursorResult.Cursor?.Offset);
        Assert.Equal(pool.SearchId, cursorResult.Pool?.SearchId);
        Assert.Equal(
            TimeSpan.FromMinutes(15),
            distributedCache.OptionsByKey["semantic:pool:v1:search-id"].AbsoluteExpirationRelativeToNow);
    }

    [Fact]
    public async Task Corrupt_plan_cache_entry_is_treated_as_a_miss()
    {
        RecordingDistributedCache distributedCache = new();
        await distributedCache.SetAsync(
            "semantic:plan:v1:corrupt",
            Encoding.UTF8.GetBytes("not-json"),
            new DistributedCacheEntryOptions());
        SemanticSearchCache cache = new(distributedCache, NullLogger<SemanticSearchCache>.Instance);
        int factoryCalls = 0;

        SemanticSearchCacheResult<SemanticSearchPlan> result = await cache.GetOrCreatePlanAsync(
            "corrupt",
            _ =>
            {
                factoryCalls += 1;
                return Task.FromResult(CreatePlan());
            });

        Assert.Equal(1, factoryCalls);
        Assert.Equal("mars", result.Value.InterpretedQuery);
    }

    [Fact]
    public async Task Structurally_invalid_plan_cache_entry_is_treated_as_a_miss()
    {
        RecordingDistributedCache distributedCache = new();
        await distributedCache.SetAsync(
            "semantic:plan:v1:invalid-shape",
            Encoding.UTF8.GetBytes("{}"),
            new DistributedCacheEntryOptions());
        SemanticSearchCache cache = new(distributedCache, NullLogger<SemanticSearchCache>.Instance);
        int factoryCalls = 0;

        SemanticSearchCacheResult<SemanticSearchPlan> result = await cache.GetOrCreatePlanAsync(
            "invalid-shape",
            _ =>
            {
                factoryCalls += 1;
                return Task.FromResult(CreatePlan());
            });

        Assert.Equal(1, factoryCalls);
        Assert.Equal("mars", result.Value.PrimaryQuery);
        Assert.False(result.Hit);
    }

    [Fact]
    public async Task Plan_with_null_nested_term_is_treated_as_a_cache_miss()
    {
        RecordingDistributedCache distributedCache = new();
        SemanticSearchPlan invalidPlan = CreatePlan() with { RequiredTerms = [null!] };
        await distributedCache.SetAsync(
            "semantic:plan:v1:null-term",
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(invalidPlan)),
            new DistributedCacheEntryOptions());
        SemanticSearchCache cache = new(distributedCache, NullLogger<SemanticSearchCache>.Instance);
        int factoryCalls = 0;

        SemanticSearchCacheResult<SemanticSearchPlan> result = await cache.GetOrCreatePlanAsync(
            "null-term",
            _ =>
            {
                factoryCalls += 1;
                return Task.FromResult(CreatePlan());
            });

        Assert.Equal(1, factoryCalls);
        Assert.False(result.Hit);
        Assert.Equal(["mars"], result.Value.RequiredTerms);
    }

    [Fact]
    public async Task Pool_with_null_nested_image_is_treated_as_a_cache_miss()
    {
        RecordingDistributedCache distributedCache = new();
        SemanticSearchPool invalidPool = CreatePool("invalid-pool") with { Images = [null!] };
        await distributedCache.SetAsync(
            "semantic:pool-index:v1:null-image",
            Encoding.UTF8.GetBytes("{\"searchId\":\"invalid-pool\"}"),
            new DistributedCacheEntryOptions());
        await distributedCache.SetAsync(
            "semantic:pool:v1:invalid-pool",
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(invalidPool)),
            new DistributedCacheEntryOptions());
        SemanticSearchCache cache = new(distributedCache, NullLogger<SemanticSearchCache>.Instance);
        int factoryCalls = 0;

        SemanticSearchCacheResult<SemanticSearchPool> result = await cache.GetOrCreatePoolAsync(
            "null-image",
            _ =>
            {
                factoryCalls += 1;
                return Task.FromResult(CreatePool("replacement-pool"));
            });

        Assert.Equal(1, factoryCalls);
        Assert.False(result.Hit);
        Assert.Equal("replacement-pool", result.Value.SearchId);
    }

    [Fact]
    public async Task Cache_operations_propagate_caller_cancellation()
    {
        SemanticSearchCache cache = new(
            new CancellationAwareDistributedCache(),
            NullLogger<SemanticSearchCache>.Instance);
        using CancellationTokenSource cancellationSource = new();
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.GetOrCreatePlanAsync(
            "cancelled",
            _ => Task.FromResult(CreatePlan()),
            cancellationSource.Token));
    }

    [Fact]
    public async Task Single_flight_survives_first_caller_cancellation_for_followers()
    {
        RecordingDistributedCache distributedCache = new();
        SemanticSearchCache cache = new(distributedCache, NullLogger<SemanticSearchCache>.Instance);
        TaskCompletionSource factoryStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseFactory = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int factoryCalls = 0;
        using CancellationTokenSource firstCallerCancellation = new();

        Task<SemanticSearchCacheResult<SemanticSearchPlan>> firstCaller = cache.GetOrCreatePlanAsync(
            "shared-cancellation",
            async factoryCancellationToken =>
            {
                Interlocked.Increment(ref factoryCalls);
                factoryStarted.TrySetResult();
                await releaseFactory.Task.WaitAsync(factoryCancellationToken);
                return CreatePlan();
            },
            firstCallerCancellation.Token);
        await factoryStarted.Task;
        Task<SemanticSearchCacheResult<SemanticSearchPlan>> follower = cache.GetOrCreatePlanAsync(
            "shared-cancellation",
            _ => throw new InvalidOperationException("A second factory must not start."));

        firstCallerCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => firstCaller);
        releaseFactory.TrySetResult();
        SemanticSearchCacheResult<SemanticSearchPlan> followerResult = await follower;

        Assert.Equal(1, factoryCalls);
        Assert.Equal("mars", followerResult.Value.PrimaryQuery);
    }

    [Fact]
    public async Task Single_flight_cancels_factory_when_last_waiter_cancels()
    {
        RecordingDistributedCache distributedCache = new();
        SemanticSearchCache cache = new(distributedCache, NullLogger<SemanticSearchCache>.Instance);
        TaskCompletionSource factoryStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken observedFactoryCancellationToken = default;
        using CancellationTokenSource callerCancellation = new();

        Task<SemanticSearchCacheResult<SemanticSearchPlan>> caller = cache.GetOrCreatePlanAsync(
            "sole-caller-cancellation",
            async factoryCancellationToken =>
            {
                observedFactoryCancellationToken = factoryCancellationToken;
                factoryStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, factoryCancellationToken);
                return CreatePlan();
            },
            callerCancellation.Token);

        await factoryStarted.Task;
        callerCancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => caller);
        Assert.True(observedFactoryCancellationToken.IsCancellationRequested);
    }

    private static SemanticSearchPlan CreatePlan()
    {
        return new SemanticSearchPlan("mars", ["mars"], null, null, null, null, null, "en");
    }

    private static SemanticSearchPool CreatePool(string searchId)
    {
        return new SemanticSearchPool(
            searchId,
            "mars",
            new SemanticSearchEffectiveFilters(null, null, null, null, null, []),
            [],
            false,
            null,
            DateTimeOffset.UtcNow.AddMinutes(15));
    }

    private sealed class RecordingDistributedCache : IDistributedCache
    {
        private readonly ConcurrentDictionary<string, byte[]> _values = new(StringComparer.Ordinal);

        public ConcurrentDictionary<string, DistributedCacheEntryOptions> OptionsByKey { get; } = new(StringComparer.Ordinal);

        public byte[]? Get(string key) => _values.TryGetValue(key, out byte[]? value) ? value : null;

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(Get(key));
        }

        public void Refresh(string key)
        {
        }

        public Task RefreshAsync(string key, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public void Remove(string key)
        {
            _values.TryRemove(key, out _);
        }

        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            Remove(key);
            return Task.CompletedTask;
        }

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
            _values[key] = value.ToArray();
            OptionsByKey[key] = options;
        }

        public Task SetAsync(
            string key,
            byte[] value,
            DistributedCacheEntryOptions options,
            CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            Set(key, value, options);
            return Task.CompletedTask;
        }
    }

    private sealed class CancellationAwareDistributedCache : IDistributedCache
    {
        public byte[]? Get(string key) => null;

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult<byte[]?>(null);
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
