using Microsoft.Extensions.DependencyInjection;
using NasaExplorer.Application.Common.Interfaces;
using NasaExplorer.Application.DTOs.Search;
using NasaExplorer.Application.Features.Search;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace NasaExplorer.API.Tests;

public sealed class SearchApiTests : IClassFixture<NasaExplorerApiFactory>, IDisposable
{
    private readonly NasaExplorerApiFactory _factory;
    private readonly HttpClient _client;

    public SearchApiTests(NasaExplorerApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateHttpsClient();
    }

    public void Dispose()
    {
        _client.Dispose();
    }

    [Fact]
    public async Task Semantic_search_requires_query_without_cursor()
    {
        using HttpResponseMessage response = await _client.GetAsync("/api/search/semantic");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Semantic_search_response_is_compatible_and_cursor_has_no_duplicates_or_second_ai_call()
    {
        int aiCallsBefore = _factory.AiService.SemanticSearchCalls;
        int nasaCallsBefore = _factory.NasaService.SearchCalls;

        using HttpResponseMessage firstResponse = await _client.GetAsync(
            "/api/search/semantic?q=integration-cursor-contract&pageSize=2&locale=en");

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        NasaSearchResultDto firstPage = await firstResponse.Content.ReadFromJsonAsync<NasaSearchResultDto>()
            ?? throw new InvalidOperationException("The semantic response body was empty.");
        Assert.Equal("semantic", firstPage.Mode);
        Assert.Equal("mars sunset", firstPage.InterpretedQuery);
        Assert.False(string.IsNullOrWhiteSpace(firstPage.SearchId));
        Assert.False(firstPage.Degraded);
        Assert.Equal(4, firstPage.TotalHits);
        Assert.Equal(1, firstPage.Page);
        Assert.Equal(2, firstPage.PageSize);
        Assert.Equal(2, firstPage.Images.Count);
        Assert.NotNull(firstPage.AppliedFilters);
        Assert.NotNull(firstPage.RelaxationSuggestions);
        Assert.False(string.IsNullOrWhiteSpace(firstPage.NextCursor));
        Assert.All(firstPage.Images, image =>
        {
            Assert.NotNull(image.RelevanceScore);
            Assert.NotEmpty(image.MatchReasons);
        });
        int aiCallsAfterFirstPage = _factory.AiService.SemanticSearchCalls;
        int nasaCallsAfterFirstPage = _factory.NasaService.SearchCalls;
        Assert.Equal(aiCallsBefore + 1, aiCallsAfterFirstPage);
        Assert.True(nasaCallsAfterFirstPage > nasaCallsBefore);

        using HttpResponseMessage secondResponse = await _client.GetAsync(
            $"/api/search/semantic?cursor={Uri.EscapeDataString(firstPage.NextCursor!)}&pageSize=2");

        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        NasaSearchResultDto secondPage = await secondResponse.Content.ReadFromJsonAsync<NasaSearchResultDto>()
            ?? throw new InvalidOperationException("The cursor response body was empty.");
        Assert.Equal(firstPage.SearchId, secondPage.SearchId);
        Assert.Equal(2, secondPage.Page);
        Assert.Equal(aiCallsAfterFirstPage, _factory.AiService.SemanticSearchCalls);
        Assert.Equal(nasaCallsAfterFirstPage, _factory.NasaService.SearchCalls);
        Assert.Empty(firstPage.Images.Select(image => image.NasaImageId).Intersect(
            secondPage.Images.Select(image => image.NasaImageId),
            StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Invalid_cursor_returns_search_session_expired_gone_response()
    {
        using HttpResponseMessage response = await _client.GetAsync(
            "/api/search/semantic?cursor=invalid-integration-cursor&pageSize=2");

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("search_session_expired", body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Expired_cursor_returns_search_session_expired_gone_response()
    {
        string searchId = Guid.NewGuid().ToString("N");
        string cursor = Guid.NewGuid().ToString("N");
        ISemanticSearchCache cache = _factory.Services.GetRequiredService<ISemanticSearchCache>();
        SemanticSearchPool expiredPool = new(
            searchId,
            "mars",
            new SemanticSearchEffectiveFilters(null, null, null, null, null, []),
            [],
            false,
            null,
            DateTimeOffset.UtcNow.AddMinutes(-1));
        await cache.GetOrCreatePoolAsync(
            Guid.NewGuid().ToString("N"),
            _ => Task.FromResult(expiredPool));
        await cache.StoreCursorAsync(
            cursor,
            new SemanticSearchCursorState(searchId, 0, 1),
            TimeSpan.FromMinutes(1));

        using HttpResponseMessage response = await _client.GetAsync(
            $"/api/search/semantic?cursor={cursor}&pageSize=2");

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("search_session_expired", body.RootElement.GetProperty("code").GetString());
    }
}
