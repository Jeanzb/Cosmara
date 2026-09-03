using System.Net;
using System.Net.Http.Json;

namespace NasaExplorer.API.Tests;

public sealed class SearchEventsApiTests : IClassFixture<NasaExplorerApiFactory>, IDisposable
{
    private readonly HttpClient _client;

    public SearchEventsApiTests(NasaExplorerApiFactory factory)
    {
        _client = factory.CreateHttpsClient();
    }

    public void Dispose()
    {
        _client.Dispose();
    }

    [Fact]
    public async Task Valid_search_event_returns_no_content()
    {
        using HttpResponseMessage response = await _client.PostAsJsonAsync("/api/search/events", new
        {
            eventName = "result_opened",
            searchId = "integration-search-id",
            resultId = "PIA-12345",
            mode = "semantic",
            resultCount = 1
        });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Theory]
    [InlineData("query")]
    [InlineData("text")]
    public async Task Search_event_rejects_payload_with_query_text_field(string forbiddenField)
    {
        Dictionary<string, object?> payload = new()
        {
            ["eventName"] = "search_submitted",
            ["mode"] = "semantic",
            [forbiddenField] = "sensitive search text"
        };

        using HttpResponseMessage response = await _client.PostAsJsonAsync("/api/search/events", payload);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
