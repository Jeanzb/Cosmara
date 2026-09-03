using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using NasaExplorer.Domain.Entities.Collections;
using NasaExplorer.Domain.Interfaces.Services;
using NasaExplorer.Domain.Models.Ai;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace NasaExplorer.Infrastructure.ExternalServices.OpenAi;

public sealed class AiEnrichmentService : IAiEnrichmentService
{
    private static readonly string[] SemanticPlanPropertyNames =
    [
        "primaryQuery",
        "alternativeQuery",
        "requiredTerms",
        "excludedTerms",
        "dateFrom",
        "dateTo",
        "rover",
        "camera",
        "mission",
        "confidence"
    ];

    private static readonly string[] KnownMissions =
    [
        "Mars Science Laboratory",
        "DSCOVR EPIC",
        "Mars 2020",
        "Cassini",
        "Hubble",
        "Apollo",
        "Rosetta",
        "JWST",
        "Juno",
        "LRO"
    ];

    private static readonly string[] KnownRovers = ["Perseverance", "Curiosity", "Opportunity", "Spirit"];

    private static readonly string[] KnownCameras =
    [
        "Mastcam-Z",
        "LRO NAC",
        "NIRCam",
        "JunoCam",
        "HiRISE",
        "WATSON",
        "Mastcam",
        "Navcam",
        "MAHLI",
        "WFC3",
        "ACS"
    ];

    private static readonly IReadOnlyDictionary<string, string> MissionAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["cassini huygens"] = "Cassini",
        ["deep space climate observatory"] = "DSCOVR EPIC",
        ["dscovr"] = "DSCOVR EPIC",
        ["hubble space telescope"] = "Hubble",
        ["james webb"] = "JWST",
        ["james webb space telescope"] = "JWST",
        ["lunar reconnaissance orbiter"] = "LRO",
        ["mars 2020 mission"] = "Mars 2020",
        ["mars science laboratory mission"] = "Mars Science Laboratory",
        ["mars science laboratory msl"] = "Mars Science Laboratory",
        ["msl"] = "Mars Science Laboratory"
    };

    private static readonly IReadOnlyDictionary<string, string> RoverAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["curiosity rover"] = "Curiosity",
        ["mars 2020 perseverance"] = "Perseverance",
        ["mars 2020 perseverance rover"] = "Perseverance",
        ["mars exploration rover opportunity"] = "Opportunity",
        ["mars exploration rover spirit"] = "Spirit",
        ["mars science laboratory curiosity"] = "Curiosity",
        ["mars science laboratory curiosity rover"] = "Curiosity",
        ["mer a"] = "Spirit",
        ["mer b"] = "Opportunity",
        ["opportunity rover"] = "Opportunity",
        ["perseverance rover"] = "Perseverance",
        ["spirit rover"] = "Spirit"
    };

    private static readonly IReadOnlyDictionary<string, string> CameraAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["advanced camera for surveys"] = "ACS",
        ["juno camera"] = "JunoCam",
        ["lunar reconnaissance orbiter narrow angle camera"] = "LRO NAC",
        ["mast camera"] = "Mastcam",
        ["mast camera z"] = "Mastcam-Z",
        ["mastcam z camera"] = "Mastcam-Z",
        ["navigation camera"] = "Navcam",
        ["navigation cameras"] = "Navcam",
        ["near infrared camera"] = "NIRCam",
        ["wide field camera 3"] = "WFC3"
    };

    private static readonly HashSet<string> FallbackStopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "about", "all", "and", "con", "de", "del", "desde", "el", "en", "find", "for",
        "from", "images", "imagenes", "imágenes", "la", "las", "los", "me", "of", "para",
        "photos", "pictures", "por", "search", "show", "the", "una", "unas", "unos", "with",
        "without", "exclude", "excluding", "except", "sin", "buscar", "busca", "muestra"
    };

    private static readonly Regex IsoDateRegex = new(
        @"\b(?<date>\d{4}-\d{2}-\d{2})\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex YearRegex = new(
        @"\b(?<year>19\d{2}|20\d{2})\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ExcludedTermRegex = new(
        @"(?:\b(?:without|exclude|excluding|except|sin)\s+|(?<![\p{L}\p{N}])-)(?<term>[\p{L}\p{N}][\p{L}\p{N}_-]*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex SearchTermRegex = new(
        @"[\p{L}\p{N}][\p{L}\p{N}_-]*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex KnownValueSeparatorRegex = new(
        @"[^\p{L}\p{N}]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly JsonSerializerOptions RequestJsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly OpenAiOptions _options;
    private readonly ILogger<AiEnrichmentService> _logger;

    public SemanticSearchPlannerIdentity SemanticSearchIdentity => new(
        string.IsNullOrWhiteSpace(_options.Model) ? "unconfigured" : _options.Model,
        OpenAiPrompts.SemanticSearchVersion);

    public AiEnrichmentService(
        HttpClient httpClient,
        IOptions<OpenAiOptions> options,
        ILogger<AiEnrichmentService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AiImageEnrichmentResult> EnrichImageAsync(
        string imageTitle,
        string? imageDescription,
        CancellationToken cancellationToken = default)
    {
        string fallbackDescription = $"NASA archive image: {imageTitle}.";
        string fallbackContext = $"This NASA archive image, {imageTitle}, can be explored through its mission context, capture date, source center, and related imagery to understand how it fits into the broader history of space exploration.";

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            return new AiImageEnrichmentResult(fallbackDescription, ["space exploration", "NASA archive"], fallbackContext);
        }

        string userPrompt = $"""
            Title: {imageTitle}
            NASA description: {imageDescription ?? "No description provided."}
            Return JSON with string description, string[] funFacts, and string historicalContext.
            """;

        string content = await CreateChatCompletionAsync(OpenAiPrompts.EnrichImage, userPrompt, fallbackDescription, cancellationToken, expectJson: true);
        AiEnrichmentResponse? parsed = TryDeserialize<AiEnrichmentResponse>(content);

        return new AiImageEnrichmentResult(
            string.IsNullOrWhiteSpace(parsed?.Description) ? fallbackDescription : parsed.Description,
            parsed?.FunFacts?.Where(fact => !string.IsNullOrWhiteSpace(fact)).ToArray() ?? ["space exploration", "NASA archive"],
            string.IsNullOrWhiteSpace(parsed?.HistoricalContext) ? fallbackContext : parsed.HistoricalContext);
    }

    public async Task<string> CompareImagesAsync(
        IReadOnlyCollection<CollectionImage> images,
        string language,
        CancellationToken cancellationToken = default)
    {
        string normalizedLanguage = language.Equals("es", StringComparison.OrdinalIgnoreCase) ? "es" : "en";
        string fallback = BuildComparisonFallback(images, normalizedLanguage);

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            return fallback;
        }

        string imageList = string.Join(
            Environment.NewLine,
            images.Select(image => $"- {image.SpaceImage?.Title ?? image.SpaceImageId.ToString()}: {image.SpaceImage?.Description ?? "No description."}"));

        string content = await CreateChatCompletionAsync(
            OpenAiPrompts.CompareImages,
            $"Respond in {(normalizedLanguage == "es" ? "Spanish" : "English")}.\nCompare these images:\n{imageList}",
            fallback,
            cancellationToken,
            expectJson: true);

        return ExtractJsonPayload(content);
    }

    public async Task<IReadOnlyCollection<string>> SuggestTagsAsync(
        string imageTitle,
        string? imageDescription,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            return BuildFallbackTags(imageTitle);
        }

        string content = await CreateChatCompletionAsync(
            OpenAiPrompts.SuggestTags,
            $"Title: {imageTitle}\nDescription: {imageDescription ?? "No description provided."}",
            "[]",
            cancellationToken,
            expectJson: true);
        string[]? parsed = TryDeserialize<string[]>(content);

        return (parsed ?? BuildFallbackTags(imageTitle))
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();
    }

    public async Task<SemanticSearchPlan> CreateSemanticSearchPlanAsync(
        string naturalLanguageQuery,
        string locale,
        CancellationToken cancellationToken = default)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        string normalizedLocale = NormalizeLocale(locale);

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            SemanticSearchPlan fallback = BuildSemanticFallback(
                naturalLanguageQuery,
                normalizedLocale,
                SemanticSearchDegradationReasons.AiUnavailable);
            LogSemanticAiMetric(stopwatch, null, "api_key_unavailable");
            return fallback;
        }

        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(TimeSpan.FromSeconds(8));

        try
        {
            ProviderRequestOptions providerRequestOptions = GetProviderRequestOptions();
            string userPrompt = JsonSerializer.Serialize(
                new
                {
                    locale = normalizedLocale,
                    query = naturalLanguageQuery
                },
                JsonOptions);

            using HttpRequestMessage request = new(HttpMethod.Post, "chat/completions")
            {
                Content = JsonContent.Create(
                    new ChatCompletionRequest(
                        _options.Model,
                        [
                            new ChatMessage("system", OpenAiPrompts.SemanticSearch),
                            new ChatMessage("user", userPrompt)
                        ],
                        new ChatResponseFormat("json_object"),
                        Temperature: 0,
                        MaxTokens: 200,
                        ReasoningEffort: providerRequestOptions.ReasoningEffort,
                        Thinking: providerRequestOptions.Thinking),
                    options: RequestJsonOptions)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

            using HttpResponseMessage response = await _httpClient.SendAsync(request, timeoutSource.Token);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Semantic AI provider returned {StatusCode} for model {Model}; deterministic fallback selected.",
                    (int)response.StatusCode,
                    _options.Model);

                SemanticSearchPlan fallback = BuildSemanticFallback(
                    naturalLanguageQuery,
                    normalizedLocale,
                    SemanticSearchDegradationReasons.AiUnavailable);
                LogSemanticAiMetric(stopwatch, null, "provider_unavailable");
                return fallback;
            }

            await using Stream stream = await response.Content.ReadAsStreamAsync(timeoutSource.Token);
            ChatCompletionResponse? completion = await JsonSerializer.DeserializeAsync<ChatCompletionResponse>(
                stream,
                JsonOptions,
                timeoutSource.Token);
            string? content = completion?.Choices?.FirstOrDefault()?.Message?.Content;

            if (!TryBuildSemanticPlan(content, normalizedLocale, out SemanticSearchPlan? plan))
            {
                _logger.LogWarning(
                    "Semantic AI provider returned an invalid plan for model {Model}; deterministic fallback selected.",
                    _options.Model);

                SemanticSearchPlan fallback = BuildSemanticFallback(
                    naturalLanguageQuery,
                    normalizedLocale,
                    SemanticSearchDegradationReasons.AiInvalidResponse);
                LogSemanticAiMetric(stopwatch, completion?.Usage, "invalid_response");
                return fallback;
            }

            LogSemanticAiMetric(stopwatch, completion?.Usage, "success");
            return plan!;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            LogSemanticAiMetric(stopwatch, null, "cancelled");
            throw;
        }
        catch (OperationCanceledException exception)
        {
            _logger.LogWarning(
                exception,
                "Semantic AI provider timed out for model {Model}; deterministic fallback selected.",
                _options.Model);

            SemanticSearchPlan fallback = BuildSemanticFallback(
                naturalLanguageQuery,
                normalizedLocale,
                SemanticSearchDegradationReasons.AiTimeout);
            LogSemanticAiMetric(stopwatch, null, "timeout");
            return fallback;
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(
                exception,
                "Semantic AI provider returned invalid JSON for model {Model}; deterministic fallback selected.",
                _options.Model);

            SemanticSearchPlan fallback = BuildSemanticFallback(
                naturalLanguageQuery,
                normalizedLocale,
                SemanticSearchDegradationReasons.AiInvalidResponse);
            LogSemanticAiMetric(stopwatch, null, "invalid_json");
            return fallback;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Semantic AI provider was unavailable for model {Model}; deterministic fallback selected.",
                _options.Model);

            SemanticSearchPlan fallback = BuildSemanticFallback(
                naturalLanguageQuery,
                normalizedLocale,
                SemanticSearchDegradationReasons.AiUnavailable);
            LogSemanticAiMetric(stopwatch, null, "provider_unavailable");
            return fallback;
        }
    }

    private ProviderRequestOptions GetProviderRequestOptions()
    {
        string model = _options.Model.Trim();
        string baseUrl = _options.BaseUrl.Trim();
        bool isDeepSeek = model.StartsWith("deepseek-v4", StringComparison.OrdinalIgnoreCase)
            || baseUrl.Contains("api.deepseek.com", StringComparison.OrdinalIgnoreCase);

        if (isDeepSeek)
        {
            return new ProviderRequestOptions(null, new ChatThinking("disabled"));
        }

        bool isGemini = model.StartsWith("gemini-2.5", StringComparison.OrdinalIgnoreCase)
            || baseUrl.Contains("generativelanguage.googleapis.com", StringComparison.OrdinalIgnoreCase);

        return isGemini
            ? new ProviderRequestOptions("none", null)
            : new ProviderRequestOptions(null, null);
    }

    private bool TryBuildSemanticPlan(
        string? content,
        string locale,
        out SemanticSearchPlan? plan)
    {
        plan = null;
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        SemanticSearchPlanResponse? response;
        try
        {
            string payload = ExtractJsonPayload(content);
            using JsonDocument document = JsonDocument.Parse(payload);
            if (!HasExactSemanticPlanShape(document.RootElement))
            {
                return false;
            }

            response = JsonSerializer.Deserialize<SemanticSearchPlanResponse>(payload, JsonOptions);
        }
        catch (JsonException)
        {
            return false;
        }

        string primaryQuery = NormalizeGeneratedValue(response?.PrimaryQuery, 240);
        string? alternativeQuery = NormalizeOptionalGeneratedValue(response?.AlternativeQuery, 240);
        string[] excludedTerms = NormalizeTerms(response?.ExcludedTerms);
        string[] requiredTerms = NormalizeTerms(response?.RequiredTerms)
            .Where(term => !excludedTerms.Contains(term, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();

        if (string.IsNullOrWhiteSpace(primaryQuery) && requiredTerms.Length == 0)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(primaryQuery))
        {
            primaryQuery = string.Join(' ', requiredTerms);
        }

        if (requiredTerms.Length == 0)
        {
            requiredTerms = SearchTermRegex.Matches(primaryQuery)
                .Select(match => NormalizeGeneratedValue(match.Value, 80))
                .Where(term => !string.IsNullOrWhiteSpace(term))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .ToArray();
        }

        if (!TryParseDate(response?.DateFrom, out DateOnly? dateFrom)
            || !TryParseDate(response?.DateTo, out DateOnly? dateTo)
            || (dateFrom.HasValue && dateTo.HasValue && dateFrom > dateTo)
            || response?.Confidence is not double confidence
            || !double.IsFinite(confidence)
            || confidence is < 0 or > 1)
        {
            return false;
        }

        plan = new SemanticSearchPlan(
            primaryQuery,
            alternativeQuery,
            requiredTerms,
            excludedTerms,
            dateFrom,
            dateTo,
            NormalizeKnownGeneratedValue(response?.Rover, KnownRovers, RoverAliases),
            NormalizeKnownGeneratedValue(response?.Camera, KnownCameras, CameraAliases),
            NormalizeKnownGeneratedValue(response?.Mission, KnownMissions, MissionAliases),
            locale,
            confidence,
            SemanticSearchIdentity.Model,
            SemanticSearchIdentity.PromptVersion);

        return true;
    }

    private SemanticSearchPlan BuildSemanticFallback(
        string naturalLanguageQuery,
        string locale,
        string reason)
    {
        string normalizedQuery = NormalizeGeneratedValue(naturalLanguageQuery, 240);
        string[] excludedTerms = ExcludedTermRegex.Matches(normalizedQuery)
            .Select(match => NormalizeGeneratedValue(match.Groups["term"].Value, 80))
            .Where(term => !string.IsNullOrWhiteSpace(term))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();
        string[] requiredTerms = BuildFallbackRequiredTerms(normalizedQuery, excludedTerms);
        (DateOnly? dateFrom, DateOnly? dateTo) = InferFallbackDates(normalizedQuery);
        string? rover = FindKnownValue(normalizedQuery, KnownRovers);
        string? camera = FindKnownValue(normalizedQuery, KnownCameras);
        string? mission = FindKnownValue(normalizedQuery, KnownMissions);
        string? alternativeQuery = requiredTerms.Length == 0
            ? null
            : NormalizeOptionalGeneratedValue(string.Join(' ', requiredTerms), 240);

        if (string.Equals(alternativeQuery, normalizedQuery, StringComparison.OrdinalIgnoreCase))
        {
            alternativeQuery = null;
        }

        bool inferredStructuredFilter = dateFrom.HasValue
            || dateTo.HasValue
            || rover is not null
            || camera is not null
            || mission is not null;

        return new SemanticSearchPlan(
            normalizedQuery,
            alternativeQuery,
            requiredTerms,
            excludedTerms,
            dateFrom,
            dateTo,
            rover,
            camera,
            mission,
            locale,
            inferredStructuredFilter ? 0.35 : 0.2,
            SemanticSearchIdentity.Model,
            SemanticSearchIdentity.PromptVersion,
            true,
            reason);
    }

    private static bool HasExactSemanticPlanShape(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        HashSet<string> actualNames = new(StringComparer.Ordinal);
        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (!actualNames.Add(property.Name)
                || !SemanticPlanPropertyNames.Contains(property.Name, StringComparer.Ordinal))
            {
                return false;
            }
        }

        if (actualNames.Count != SemanticPlanPropertyNames.Length)
        {
            return false;
        }

        return root.GetProperty("primaryQuery").ValueKind == JsonValueKind.String
            && IsStringOrNull(root.GetProperty("alternativeQuery"))
            && IsStringArray(root.GetProperty("requiredTerms"))
            && IsStringArray(root.GetProperty("excludedTerms"))
            && IsStringOrNull(root.GetProperty("dateFrom"))
            && IsStringOrNull(root.GetProperty("dateTo"))
            && IsStringOrNull(root.GetProperty("rover"))
            && IsStringOrNull(root.GetProperty("camera"))
            && IsStringOrNull(root.GetProperty("mission"))
            && root.GetProperty("confidence").ValueKind == JsonValueKind.Number
            && root.GetProperty("confidence").TryGetDouble(out _);
    }

    private static bool IsStringOrNull(JsonElement value)
    {
        return value.ValueKind is JsonValueKind.String or JsonValueKind.Null;
    }

    private static bool IsStringArray(JsonElement value)
    {
        return value.ValueKind == JsonValueKind.Array
            && value.EnumerateArray().All(item => item.ValueKind == JsonValueKind.String);
    }

    private static string[] NormalizeTerms(IReadOnlyCollection<string>? terms)
    {
        return (terms ?? [])
            .Select(term => NormalizeGeneratedValue(term, 80))
            .Where(term => !string.IsNullOrWhiteSpace(term))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();
    }

    private static string[] BuildFallbackRequiredTerms(
        string query,
        IReadOnlyCollection<string> excludedTerms)
    {
        return SearchTermRegex.Matches(query)
            .Select(match => NormalizeGeneratedValue(match.Value, 80))
            .Where(term => term.Length > 2)
            .Where(term => !FallbackStopWords.Contains(term))
            .Where(term => !YearRegex.IsMatch(term))
            .Where(term => !excludedTerms.Contains(term, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();
    }

    private static (DateOnly? DateFrom, DateOnly? DateTo) InferFallbackDates(string query)
    {
        DateOnly[] exactDates = IsoDateRegex.Matches(query)
            .Select(match => match.Groups["date"].Value)
            .Select(value => DateOnly.TryParseExact(
                value,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateOnly parsed)
                    ? (DateOnly?)parsed
                    : null)
            .Where(date => date.HasValue)
            .Select(date => date!.Value)
            .Distinct()
            .OrderBy(date => date)
            .ToArray();

        if (exactDates.Length > 0)
        {
            return (exactDates[0], exactDates[^1]);
        }

        int[] years = YearRegex.Matches(query)
            .Select(match => int.Parse(match.Groups["year"].Value, CultureInfo.InvariantCulture))
            .Distinct()
            .OrderBy(year => year)
            .ToArray();

        return years.Length == 0
            ? (null, null)
            : (new DateOnly(years[0], 1, 1), new DateOnly(years[^1], 12, 31));
    }

    private static string? FindKnownValue(string query, IEnumerable<string> knownValues)
    {
        foreach (string knownValue in knownValues.OrderByDescending(value => value.Length))
        {
            string pattern = $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(knownValue)}(?![\p{{L}}\p{{N}}])";
            if (Regex.IsMatch(query, pattern, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase))
            {
                return knownValue;
            }
        }

        return null;
    }

    private void LogSemanticAiMetric(Stopwatch stopwatch, ChatUsage? usage, string outcome)
    {
        stopwatch.Stop();
        _logger.LogInformation(
            "Semantic AI planner metric: {AiModel} {PromptVersion} {InputTokens} {OutputTokens} {TotalTokens} {AiLatencyMs} {AiOutcome}.",
            SemanticSearchIdentity.Model,
            SemanticSearchIdentity.PromptVersion,
            usage?.PromptTokens,
            usage?.CompletionTokens,
            usage?.TotalTokens,
            stopwatch.ElapsedMilliseconds,
            outcome);
    }

    private static bool TryParseDate(string? value, out DateOnly? date)
    {
        date = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (!DateOnly.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly parsed))
        {
            return false;
        }

        date = parsed;
        return true;
    }

    private static string NormalizeLocale(string locale)
    {
        return locale.StartsWith("es", StringComparison.OrdinalIgnoreCase) ? "es" : "en";
    }

    private static string NormalizeGeneratedValue(string? value, int maximumLength)
    {
        string normalized = string.Join(
            ' ',
            (value ?? string.Empty).Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength].TrimEnd();
    }

    private static string? NormalizeOptionalGeneratedValue(string? value, int maximumLength)
    {
        string normalized = NormalizeGeneratedValue(value, maximumLength);
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string? NormalizeKnownGeneratedValue(
        string? value,
        IReadOnlyCollection<string> knownValues,
        IReadOnlyDictionary<string, string> aliases)
    {
        string? generatedValue = NormalizeOptionalGeneratedValue(value, 120);
        if (generatedValue is null)
        {
            return null;
        }

        string lookupValue = NormalizeKnownLookupValue(generatedValue);
        string? canonicalValue = knownValues.FirstOrDefault(knownValue => string.Equals(
            NormalizeKnownLookupValue(knownValue),
            lookupValue,
            StringComparison.OrdinalIgnoreCase));

        return canonicalValue ?? aliases.GetValueOrDefault(lookupValue);
    }

    private static string NormalizeKnownLookupValue(string value)
    {
        return string.Join(
            ' ',
            KnownValueSeparatorRegex
                .Split(value.Trim().ToLowerInvariant())
                .Where(term => !string.IsNullOrWhiteSpace(term)));
    }

    private async Task<string> CreateChatCompletionAsync(
        string systemPrompt,
        string userPrompt,
        string fallback,
        CancellationToken cancellationToken,
        bool expectJson = false)
    {
        ProviderRequestOptions providerRequestOptions = GetProviderRequestOptions();
        using HttpRequestMessage request = new(HttpMethod.Post, "chat/completions")
        {
            Content = JsonContent.Create(new ChatCompletionRequest(
                _options.Model,
                [
                    new ChatMessage("system", systemPrompt),
                    new ChatMessage("user", userPrompt)
                ],
                expectJson ? new ChatResponseFormat("json_object") : null,
                ReasoningEffort: providerRequestOptions.ReasoningEffort,
                Thinking: providerRequestOptions.Thinking), options: RequestJsonOptions)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return fallback;
        }

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        ChatCompletionResponse? completion = await JsonSerializer.DeserializeAsync<ChatCompletionResponse>(stream, JsonOptions, cancellationToken);

        return completion?.Choices?.FirstOrDefault()?.Message?.Content?.Trim() ?? fallback;
    }

    private static IReadOnlyCollection<string> BuildFallbackTags(string imageTitle)
    {
        return imageTitle
            .Split([' ', ',', '.', ':', ';', '-', '_'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(term => term.Length > 2)
            .Select(term => term.ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .DefaultIfEmpty("nasa")
            .ToArray();
    }

    private static T? TryDeserialize<T>(string content)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(ExtractJsonPayload(content), JsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static string ExtractJsonPayload(string content)
    {
        string trimmed = content.Trim();

        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        int firstLineEnd = trimmed.IndexOf('\n');
        int closingFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);

        if (firstLineEnd < 0 || closingFence <= firstLineEnd)
        {
            return trimmed;
        }

        return trimmed[(firstLineEnd + 1)..closingFence].Trim();
    }

    private static string BuildComparisonFallback(IReadOnlyCollection<CollectionImage> images, string language)
    {
        string titles = string.Join(", ", images.Select(image => image.SpaceImage?.Title ?? image.SpaceImageId.ToString()).Take(4));

        object fallback = language == "es"
            ? new
            {
                title = "Comparación de imágenes NASA",
                summary = $"Estas imágenes guardadas permiten contrastar {titles} usando sus títulos, descripciones y metadatos disponibles.",
                similarities = new[] { "Todas pertenecen al archivo visual de NASA.", "Cada imagen puede analizarse por misión, fecha, centro de origen y contenido visual." },
                differences = new[] { "Las imágenes pueden variar por misión, instrumento, época y objetivo científico.", "El contexto visual depende de los metadatos disponibles para cada registro." },
                historicalContext = "El contexto histórico puede revisarse a partir de la misión, la fecha, el centro de origen y la descripción archivada de cada imagen.",
                scientificValue = "La comparación ayuda a revisar patrones visuales, objetivos de exploración y metadatos científicos disponibles.",
                conclusion = "Estas imágenes ofrecen una base clara para comparar momentos, instrumentos y objetivos dentro del archivo visual de NASA."
            }
            : new
            {
                title = "NASA image comparison",
                summary = $"These saved images can be compared through {titles} using their available titles, descriptions, and metadata.",
                similarities = new[] { "They all belong to NASA's visual archive.", "Each image can be reviewed by mission, date, source center, and visible subject matter." },
                differences = new[] { "The images may differ by mission, instrument, era, and scientific objective.", "Visual context depends on the metadata available for each record." },
                historicalContext = "Historical context can be reviewed through each image's mission, date, source center, and archived description.",
                scientificValue = "The comparison helps review visual patterns, exploration goals, and available scientific metadata.",
                conclusion = "These images provide a clear basis for comparing moments, instruments, and objectives across NASA's visual archive."
            };

        return JsonSerializer.Serialize(fallback, JsonOptions);
    }

    private sealed record ChatCompletionRequest(
        string Model,
        IReadOnlyCollection<ChatMessage> Messages,
        [property: JsonPropertyName("response_format")] ChatResponseFormat? ResponseFormat = null,
        double? Temperature = null,
        [property: JsonPropertyName("max_tokens")] int? MaxTokens = null,
        [property: JsonPropertyName("reasoning_effort")] string? ReasoningEffort = null,
        ChatThinking? Thinking = null);

    private sealed record ProviderRequestOptions(string? ReasoningEffort, ChatThinking? Thinking);

    private sealed record ChatThinking(string Type);

    private sealed record ChatResponseFormat(string Type);

    private sealed record ChatMessage(string Role, string Content);

    private sealed class ChatCompletionResponse
    {
        public IReadOnlyCollection<ChatChoice>? Choices { get; set; }

        public ChatUsage? Usage { get; set; }
    }

    private sealed class ChatUsage
    {
        [JsonPropertyName("prompt_tokens")]
        public int? PromptTokens { get; set; }

        [JsonPropertyName("completion_tokens")]
        public int? CompletionTokens { get; set; }

        [JsonPropertyName("total_tokens")]
        public int? TotalTokens { get; set; }
    }

    private sealed class ChatChoice
    {
        public ChatChoiceMessage? Message { get; set; }
    }

    private sealed class ChatChoiceMessage
    {
        public string? Content { get; set; }
    }

    private sealed class AiEnrichmentResponse
    {
        public string? Description { get; set; }

        [JsonPropertyName("funFacts")]
        public IReadOnlyCollection<string>? FunFacts { get; set; }

        public string? HistoricalContext { get; set; }
    }

    private sealed class SemanticSearchPlanResponse
    {
        public string? PrimaryQuery { get; set; }

        public string? AlternativeQuery { get; set; }

        public IReadOnlyCollection<string>? RequiredTerms { get; set; }

        public IReadOnlyCollection<string>? ExcludedTerms { get; set; }

        public string? DateFrom { get; set; }

        public string? DateTo { get; set; }

        public string? Rover { get; set; }

        public string? Camera { get; set; }

        public string? Mission { get; set; }

        public double? Confidence { get; set; }
    }
}
