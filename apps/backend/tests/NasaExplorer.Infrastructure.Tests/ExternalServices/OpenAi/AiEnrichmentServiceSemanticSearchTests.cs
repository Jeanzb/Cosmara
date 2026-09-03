using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NasaExplorer.Domain.Models.Ai;
using NasaExplorer.Infrastructure.ExternalServices.OpenAi;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace NasaExplorer.Infrastructure.Tests.ExternalServices.OpenAi;

public sealed class AiEnrichmentServiceSemanticSearchTests
{
    [Fact]
    public async Task CreateSemanticSearchPlanAsync_requests_bounded_deterministic_json_and_maps_plan()
    {
        string planJson = JsonSerializer.Serialize(new
        {
            primaryQuery = "mars sunset",
            alternativeQuery = "martian horizon",
            requiredTerms = new[] { "mars", "sunset" },
            excludedTerms = new[] { "illustration" },
            dateFrom = "2024-01-01",
            dateTo = "2024-12-31",
            rover = "Curiosity",
            camera = "Navcam",
            mission = "Mars Science Laboratory",
            confidence = 0.91
        });
        CapturingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                choices = new[]
                {
                    new { message = new { content = planJson } }
                },
                usage = new { prompt_tokens = 30, completion_tokens = 42, total_tokens = 72 }
            })
        });
        CapturingLogger<AiEnrichmentService> logger = new();
        AiEnrichmentService service = CreateService(
            handler,
            logger: logger,
            baseUrl: "https://generativelanguage.googleapis.com/v1beta/openai/",
            model: "gemini-2.5-flash");

        SemanticSearchPlan result = await service.CreateSemanticSearchPlanAsync("atardecer en marte", "es");

        Assert.False(result.Degraded);
        Assert.Equal("mars sunset", result.PrimaryQuery);
        Assert.Equal("martian horizon", result.AlternativeQuery);
        Assert.Equal(["mars", "sunset"], result.RequiredTerms);
        Assert.Equal(["illustration"], result.ExcludedTerms);
        Assert.Equal(new DateOnly(2024, 1, 1), result.DateFrom);
        Assert.Equal("Curiosity", result.Rover);
        Assert.Equal(0.91, result.Confidence);
        Assert.Equal("gemini-2.5-flash", result.Model);
        Assert.Equal(OpenAiPrompts.SemanticSearchVersion, result.PromptVersion);
        using JsonDocument requestJson = JsonDocument.Parse(handler.RequestBody!);
        JsonElement root = requestJson.RootElement;
        Assert.Equal(0, root.GetProperty("temperature").GetDouble());
        Assert.Equal(200, root.GetProperty("max_tokens").GetInt32());
        Assert.Equal("none", root.GetProperty("reasoning_effort").GetString());
        Assert.Equal("json_object", root.GetProperty("response_format").GetProperty("type").GetString());
        IReadOnlyDictionary<string, object?> metric = Assert.Single(logger.Events);
        Assert.Equal("gemini-2.5-flash", metric["AiModel"]);
        Assert.Equal(OpenAiPrompts.SemanticSearchVersion, metric["PromptVersion"]);
        Assert.Equal(30, metric["InputTokens"]);
        Assert.Equal(42, metric["OutputTokens"]);
        Assert.Equal(72, metric["TotalTokens"]);
        Assert.Equal("success", metric["AiOutcome"]);
        Assert.DoesNotContain(metric.Keys, key => key.Contains("query", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CreateSemanticSearchPlanAsync_disables_deepseek_thinking_without_gemini_reasoning_effort()
    {
        string planJson = JsonSerializer.Serialize(new
        {
            primaryQuery = "mars sunset",
            alternativeQuery = (string?)null,
            requiredTerms = new[] { "mars", "sunset" },
            excludedTerms = Array.Empty<string>(),
            dateFrom = (string?)null,
            dateTo = (string?)null,
            rover = (string?)null,
            camera = (string?)null,
            mission = (string?)null,
            confidence = 0.9
        });
        CapturingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                choices = new[] { new { message = new { content = planJson } } }
            })
        });
        AiEnrichmentService service = CreateService(
            handler,
            baseUrl: "https://api.deepseek.com/",
            model: "deepseek-v4-flash");

        SemanticSearchPlan result = await service.CreateSemanticSearchPlanAsync("atardecer en marte", "es");

        Assert.False(result.Degraded);
        Assert.Equal("deepseek-v4-flash", result.Model);
        using JsonDocument requestJson = JsonDocument.Parse(handler.RequestBody!);
        JsonElement root = requestJson.RootElement;
        Assert.Equal("disabled", root.GetProperty("thinking").GetProperty("type").GetString());
        Assert.False(root.TryGetProperty("reasoning_effort", out _));
        Assert.Equal("json_object", root.GetProperty("response_format").GetProperty("type").GetString());
        Assert.Equal(200, root.GetProperty("max_tokens").GetInt32());
    }

    [Fact]
    public async Task CreateSemanticSearchPlanAsync_discards_invalid_inferred_metadata_without_degrading_query()
    {
        string planJson = JsonSerializer.Serialize(new
        {
            primaryQuery = "sunset on mars",
            alternativeQuery = "martian sunset",
            requiredTerms = new[] { "mars", "sunset" },
            excludedTerms = Array.Empty<string>(),
            dateFrom = (string?)null,
            dateTo = (string?)null,
            rover = "Mars",
            camera = "Mars Camera",
            mission = "Mars",
            confidence = 0.88
        });
        CapturingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                choices = new[] { new { message = new { content = planJson } } }
            })
        });
        AiEnrichmentService service = CreateService(handler);

        SemanticSearchPlan result = await service.CreateSemanticSearchPlanAsync("atardecer en marte", "es");

        Assert.False(result.Degraded);
        Assert.Equal("sunset on mars", result.PrimaryQuery);
        Assert.Equal("martian sunset", result.AlternativeQuery);
        Assert.Equal(["mars", "sunset"], result.RequiredTerms);
        Assert.Null(result.Rover);
        Assert.Null(result.Camera);
        Assert.Null(result.Mission);
    }

    [Fact]
    public async Task CreateSemanticSearchPlanAsync_maps_safe_inferred_metadata_aliases_to_canonical_values()
    {
        string planJson = JsonSerializer.Serialize(new
        {
            primaryQuery = "curiosity navigation camera james webb",
            alternativeQuery = (string?)null,
            requiredTerms = new[] { "curiosity" },
            excludedTerms = Array.Empty<string>(),
            dateFrom = (string?)null,
            dateTo = (string?)null,
            rover = "Curiosity rover",
            camera = "Navigation Camera",
            mission = "James Webb Space Telescope",
            confidence = 0.8
        });
        CapturingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                choices = new[] { new { message = new { content = planJson } } }
            })
        });
        AiEnrichmentService service = CreateService(handler);

        SemanticSearchPlan result = await service.CreateSemanticSearchPlanAsync("curiosity navcam james webb", "en");

        Assert.False(result.Degraded);
        Assert.Equal("Curiosity", result.Rover);
        Assert.Equal("Navcam", result.Camera);
        Assert.Equal("JWST", result.Mission);
    }

    [Fact]
    public async Task CreateSemanticSearchPlanAsync_rejects_additional_json_properties()
    {
        string planJson = """
            {
              "primaryQuery":"mars",
              "alternativeQuery":null,
              "requiredTerms":["mars"],
              "excludedTerms":[],
              "dateFrom":null,
              "dateTo":null,
              "rover":null,
              "camera":null,
              "mission":null,
              "confidence":0.8,
              "unexpected":"value"
            }
            """;
        CapturingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                choices = new[] { new { message = new { content = planJson } } }
            })
        });

        SemanticSearchPlan result = await CreateService(handler).CreateSemanticSearchPlanAsync("mars", "en");

        Assert.True(result.Degraded);
        Assert.Equal(SemanticSearchDegradationReasons.AiInvalidResponse, result.DegradationReason);
    }

    [Fact]
    public async Task CreateSemanticSearchPlanAsync_deterministic_fallback_infers_known_filters_and_year()
    {
        CapturingHandler handler = new(_ => throw new InvalidOperationException("No HTTP call was expected."));
        AiEnrichmentService service = CreateService(handler, apiKey: string.Empty);

        SemanticSearchPlan result = await service.CreateSemanticSearchPlanAsync(
            "Curiosity Navcam photos from 2021 without dust",
            "en");

        Assert.True(result.Degraded);
        Assert.Equal(new DateOnly(2021, 1, 1), result.DateFrom);
        Assert.Equal(new DateOnly(2021, 12, 31), result.DateTo);
        Assert.Equal("Curiosity", result.Rover);
        Assert.Equal("Navcam", result.Camera);
        Assert.Contains("dust", result.ExcludedTerms, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("dust", result.RequiredTerms, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("test-model", result.Model);
        Assert.Equal(OpenAiPrompts.SemanticSearchVersion, result.PromptVersion);
        Assert.Null(handler.RequestBody);
    }

    [Fact]
    public async Task CreateSemanticSearchPlanAsync_falls_back_for_invalid_json()
    {
        CapturingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                choices = new[]
                {
                    new { message = new { content = "not-json" } }
                }
            })
        });
        AiEnrichmentService service = CreateService(handler);

        SemanticSearchPlan result = await service.CreateSemanticSearchPlanAsync("mars sunset", "en");

        Assert.True(result.Degraded);
        Assert.Equal(SemanticSearchDegradationReasons.AiInvalidResponse, result.DegradationReason);
        Assert.Equal("mars sunset", result.InterpretedQuery);
    }

    [Fact]
    public async Task CreateSemanticSearchPlanAsync_falls_back_when_provider_is_unavailable()
    {
        CapturingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        AiEnrichmentService service = CreateService(handler);

        SemanticSearchPlan result = await service.CreateSemanticSearchPlanAsync("mars", "en");

        Assert.True(result.Degraded);
        Assert.Equal(SemanticSearchDegradationReasons.AiUnavailable, result.DegradationReason);
    }

    [Fact]
    public async Task CreateSemanticSearchPlanAsync_uses_eight_second_timeout_fallback()
    {
        CapturingHandler handler = new(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        AiEnrichmentService service = CreateService(handler);

        SemanticSearchPlan result = await service.CreateSemanticSearchPlanAsync("mars", "en");

        Assert.True(result.Degraded);
        Assert.Equal(SemanticSearchDegradationReasons.AiTimeout, result.DegradationReason);
    }

    [Fact]
    public async Task CreateSemanticSearchPlanAsync_propagates_caller_cancellation()
    {
        CapturingHandler handler = new(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        AiEnrichmentService service = CreateService(handler);
        using CancellationTokenSource cancellationSource = new();
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CreateSemanticSearchPlanAsync(
            "mars",
            "en",
            cancellationSource.Token));
    }

    private static AiEnrichmentService CreateService(
        HttpMessageHandler handler,
        string apiKey = "test-key",
        ILogger<AiEnrichmentService>? logger = null,
        string baseUrl = "https://ai.test/v1/",
        string model = "test-model")
    {
        HttpClient httpClient = new(handler)
        {
            BaseAddress = new Uri(baseUrl)
        };

        return new AiEnrichmentService(
            httpClient,
            Options.Create(new OpenAiOptions
            {
                ApiKey = apiKey,
                BaseUrl = baseUrl,
                Model = model
            }),
            logger ?? NullLogger<AiEnrichmentService>.Instance);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
            : this((request, _) => Task.FromResult(handler(request)))
        {
        }

        public CapturingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return await _handler(request, cancellationToken);
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<IReadOnlyDictionary<string, object?>> Events { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Dictionary<string, object?> values = ((IEnumerable<KeyValuePair<string, object?>>)(object)state!)
                .Where(pair => pair.Key != "{OriginalFormat}")
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            Events.Add(values);
        }
    }
}
