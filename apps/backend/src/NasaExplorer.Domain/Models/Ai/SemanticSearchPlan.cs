using System.Text.Json.Serialization;

namespace NasaExplorer.Domain.Models.Ai;

[method: JsonConstructor]
public sealed record SemanticSearchPlan(
    string PrimaryQuery,
    string? AlternativeQuery,
    IReadOnlyCollection<string> RequiredTerms,
    IReadOnlyCollection<string> ExcludedTerms,
    DateOnly? DateFrom,
    DateOnly? DateTo,
    string? Rover,
    string? Camera,
    string? Mission,
    string Locale,
    double Confidence,
    string Model,
    string PromptVersion,
    bool Degraded = false,
    string? DegradationReason = null)
{
    [JsonIgnore]
    public string InterpretedQuery => PrimaryQuery;

    [JsonIgnore]
    public IReadOnlyCollection<string> Keywords => RequiredTerms;

    public SemanticSearchPlan(
        string interpretedQuery,
        IReadOnlyCollection<string> keywords,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        string? rover,
        string? camera,
        string? mission,
        string locale,
        bool degraded = false,
        string? degradationReason = null)
        : this(
            interpretedQuery,
            null,
            keywords,
            [],
            dateFrom,
            dateTo,
            rover,
            camera,
            mission,
            locale,
            degraded ? 0.2 : 0.5,
            "legacy",
            "semantic-plan-v1",
            degraded,
            degradationReason)
    {
    }
}

public sealed record SemanticSearchPlannerIdentity(string Model, string PromptVersion);

public static class SemanticSearchDegradationReasons
{
    public const string AiUnavailable = "ai_unavailable";
    public const string AiInvalidResponse = "ai_invalid_response";
    public const string AiTimeout = "ai_timeout";
    public const string PartialNasaFailure = "partial_nasa_failure";
    public const string CacheUnavailable = "cache_unavailable";
}
