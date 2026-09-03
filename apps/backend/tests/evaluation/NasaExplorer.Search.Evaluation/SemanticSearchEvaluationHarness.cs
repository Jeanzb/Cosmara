using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using NasaExplorer.Application.Features.Search;
using NasaExplorer.Domain.Models.Ai;
using NasaExplorer.Domain.Models.Nasa;

namespace NasaExplorer.Search.Evaluation;

public sealed class FrozenEvaluationDataset
{
    public static readonly IReadOnlySet<string> RequiredCategories = new HashSet<string>(StringComparer.Ordinal)
    {
        "visual_concept",
        "mission_instrument",
        "date",
        "ambiguity",
        "typo",
        "zero_result"
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public required string Version { get; init; }

    public required string CorpusVersion { get; init; }

    public required string RelevanceScale { get; init; }

    public required string EvaluationScope { get; init; }

    public required IReadOnlyDictionary<string, EvaluationSemanticPlan> SemanticPlans { get; init; }

    public required IReadOnlyList<EvaluationDocument> Corpus { get; init; }

    public required IReadOnlyList<EvaluationQuery> Queries { get; init; }

    public static FrozenEvaluationDataset Load(string? path = null)
    {
        string fixturePath = path ?? Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "nasa-semantic-evaluation.v1.json");
        string json = File.ReadAllText(fixturePath);
        FrozenEvaluationDataset dataset = JsonSerializer.Deserialize<FrozenEvaluationDataset>(json, JsonOptions)
            ?? throw new InvalidDataException("The semantic-search evaluation fixture is empty or invalid.");

        dataset.Validate();
        return dataset;
    }

    private void Validate()
    {
        if (Version != "2026-08-25.v2" || CorpusVersion != "synthetic-nasa-metadata.v1")
        {
            throw new InvalidDataException("The frozen evaluation fixture has an unexpected version.");
        }

        if (!EvaluationScope.Contains("synthetic", StringComparison.OrdinalIgnoreCase)
            || !EvaluationScope.Contains("offline", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The fixture must disclose its synthetic offline scope.");
        }

        if (Corpus.Count == 0 || Corpus.Select(document => document.Id).Distinct(StringComparer.Ordinal).Count() != Corpus.Count)
        {
            throw new InvalidDataException("Corpus document IDs must be present and unique.");
        }

        if (Queries.Count != 48
            || Queries.Count(query => query.Language == "es") != 24
            || Queries.Count(query => query.Language == "en") != 24
            || Queries.Select(query => query.Id).Distinct(StringComparer.Ordinal).Count() != Queries.Count)
        {
            throw new InvalidDataException("The fixture must contain 48 unique queries split evenly between Spanish and English.");
        }

        HashSet<string> categories = Queries.Select(query => query.Category).ToHashSet(StringComparer.Ordinal);
        if (!categories.SetEquals(RequiredCategories)
            || RequiredCategories.Any(category => Queries.Count(query => query.Category == category) != 8)
            || RequiredCategories.Any(category => Queries.Count(query => query.Category == category && query.Language == "es") != 4)
            || RequiredCategories.Any(category => Queries.Count(query => query.Category == category && query.Language == "en") != 4))
        {
            throw new InvalidDataException("Every required category must contain four Spanish and four English queries.");
        }

        HashSet<string> documentIds = Corpus.Select(document => document.Id).ToHashSet(StringComparer.Ordinal);
        HashSet<string> intents = Queries.Select(query => query.Intent).ToHashSet(StringComparer.Ordinal);
        if (!SemanticPlans.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(intents))
        {
            throw new InvalidDataException("Every intent must have exactly one frozen semantic plan.");
        }

        foreach (EvaluationQuery query in Queries)
        {
            if (string.IsNullOrWhiteSpace(query.Text)
                || query.Judgments.Count == 0
                || query.Judgments.Any(judgment => !documentIds.Contains(judgment.Key))
                || query.Judgments.Any(judgment => judgment.Value is < 0 or > 3))
            {
                throw new InvalidDataException($"Query '{query.Id}' is incomplete or contains invalid relevance judgments.");
            }

            bool isZeroResultCategory = query.Category == "zero_result";
            if (query.ExpectsZeroResults != isZeroResultCategory
                || (isZeroResultCategory && query.Judgments.Values.Any(value => value != 0))
                || (!isZeroResultCategory && !new[] { 0, 1, 2, 3 }.All(query.Judgments.Values.Contains)))
            {
                throw new InvalidDataException($"Query '{query.Id}' has judgments inconsistent with its category.");
            }
        }
    }
}

public sealed class EvaluationSemanticPlan
{
    public required string PrimaryQuery { get; init; }

    public string? AlternativeQuery { get; init; }

    public required IReadOnlyList<string> RequiredTerms { get; init; }

    public required IReadOnlyList<string> ExcludedTerms { get; init; }

    public DateOnly? DateFrom { get; init; }

    public DateOnly? DateTo { get; init; }

    public string? Mission { get; init; }

    public string? Rover { get; init; }

    public string? Camera { get; init; }

    public SemanticSearchPlan ToProductionPlan(string locale)
    {
        return new SemanticSearchPlan(
            PrimaryQuery,
            AlternativeQuery,
            RequiredTerms,
            ExcludedTerms,
            DateFrom,
            DateTo,
            Rover,
            Camera,
            Mission,
            locale,
            1,
            "frozen-offline-plan",
            "evaluation-v2");
    }

    public EvaluationFilters ToFilters()
    {
        return new EvaluationFilters(DateFrom, DateTo, Mission, Rover, Camera);
    }
}

public sealed record EvaluationFilters(
    DateOnly? DateFrom = null,
    DateOnly? DateTo = null,
    string? Mission = null,
    string? Rover = null,
    string? Camera = null)
{
    public static EvaluationFilters Empty { get; } = new();

    public SemanticSearchEffectiveFilters ToProductionFilters()
    {
        List<string> inferred = [];
        if (DateFrom.HasValue)
        {
            inferred.Add("dateFrom");
        }

        if (DateTo.HasValue)
        {
            inferred.Add("dateTo");
        }

        if (!string.IsNullOrWhiteSpace(Rover))
        {
            inferred.Add("rover");
        }

        if (!string.IsNullOrWhiteSpace(Camera))
        {
            inferred.Add("camera");
        }

        if (!string.IsNullOrWhiteSpace(Mission))
        {
            inferred.Add("mission");
        }

        return new SemanticSearchEffectiveFilters(DateFrom, DateTo, Rover, Camera, Mission, inferred);
    }
}

public sealed class EvaluationDocument
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public required string Description { get; init; }

    public required IReadOnlyList<string> Keywords { get; init; }

    public DateTimeOffset? DateCreated { get; init; }

    public string? Mission { get; init; }

    public string? Rover { get; init; }

    public string? Camera { get; init; }

    public NasaImageAsset ToProductionImage()
    {
        return new NasaImageAsset(
            Id,
            Title,
            Description,
            "Synthetic evaluation",
            "image",
            $"https://example.invalid/{Id}/thumbnail.jpg",
            $"https://example.invalid/{Id}/image.jpg",
            $"https://example.invalid/{Id}",
            DateCreated,
            Keywords,
            Mission,
            Rover,
            Camera);
    }
}

public sealed class EvaluationQuery
{
    public required string Id { get; init; }

    public required string Language { get; init; }

    public required string Intent { get; init; }

    public required string Category { get; init; }

    public required string Text { get; init; }

    public required bool ExpectsZeroResults { get; init; }

    public required IReadOnlyDictionary<string, int> Judgments { get; init; }
}

public interface IEvaluationStrategy
{
    string Name { get; }

    EvaluationFilters GetFilters(EvaluationQuery query, FrozenEvaluationDataset dataset);

    IReadOnlyList<string> Search(
        EvaluationQuery query,
        FrozenEvaluationDataset dataset,
        int take);
}

public sealed class StandardStrategy : IEvaluationStrategy
{
    private static readonly IReadOnlySet<string> StopWords = new HashSet<string>(StringComparer.Ordinal)
    {
        "a", "al", "an", "and", "by", "de", "del", "el", "en", "from", "la", "las", "los", "me",
        "muestra", "muestrame", "of", "on", "por", "show", "the", "un", "una", "unas", "unos", "visto", "vista"
    };

    public string Name => "Standard";

    public EvaluationFilters GetFilters(EvaluationQuery query, FrozenEvaluationDataset dataset)
    {
        return EvaluationFilters.Empty;
    }

    public IReadOnlyList<string> Search(
        EvaluationQuery query,
        FrozenEvaluationDataset dataset,
        int take)
    {
        HashSet<string> queryTokens = EvaluationText.Tokenize(query.Text);
        queryTokens.ExceptWith(StopWords);

        return dataset.Corpus
            .Where(document => EvaluationText.DocumentTokens(document).Overlaps(queryTokens))
            .Take(take)
            .Select(document => document.Id)
            .ToArray();
    }
}

public sealed class CurrentSemanticStrategy : IEvaluationStrategy
{
    public string Name => "CurrentSemantic";

    public EvaluationFilters GetFilters(EvaluationQuery query, FrozenEvaluationDataset dataset)
    {
        return EvaluationFilters.Empty;
    }

    public IReadOnlyList<string> Search(
        EvaluationQuery query,
        FrozenEvaluationDataset dataset,
        int take)
    {
        string normalizedQuery = ProductSemanticAdapter.Normalize(query.Text);
        IReadOnlyCollection<string> candidates = ProductSemanticAdapter.BuildCandidateQueries(query.Text, query.Text);
        IReadOnlyList<EvaluationDocument> pool = EvaluationCandidatePool.Build(dataset.Corpus, candidates);
        SemanticSearchPlan plan = new(
            normalizedQuery,
            null,
            [],
            [],
            null,
            null,
            null,
            null,
            null,
            query.Language,
            0.5,
            "product-normalizer",
            "source-query-only");

        return ProductSemanticAdapter
            .Rank(pool, plan, EvaluationFilters.Empty)
            .Take(take)
            .Select(result => result.Image.NasaImageId)
            .ToArray();
    }
}

public sealed class NewSemanticStrategy : IEvaluationStrategy
{
    public string Name => "NewSemantic";

    public EvaluationFilters GetFilters(EvaluationQuery query, FrozenEvaluationDataset dataset)
    {
        return dataset.SemanticPlans[query.Intent].ToFilters();
    }

    public IReadOnlyList<string> Search(
        EvaluationQuery query,
        FrozenEvaluationDataset dataset,
        int take)
    {
        EvaluationSemanticPlan frozenPlan = dataset.SemanticPlans[query.Intent];
        SemanticSearchPlan productionPlan = frozenPlan.ToProductionPlan(query.Language);
        IReadOnlyCollection<string> candidates = ProductSemanticAdapter.BuildProductionCandidates(query.Text, productionPlan);
        IReadOnlyList<EvaluationDocument> pool = EvaluationCandidatePool.Build(dataset.Corpus, candidates);

        return ProductSemanticAdapter
            .Rank(pool, productionPlan, frozenPlan.ToFilters())
            .Take(take)
            .Select(result => result.Image.NasaImageId)
            .ToArray();
    }
}

internal static class EvaluationCandidatePool
{
    public static IReadOnlyList<EvaluationDocument> Build(
        IReadOnlyList<EvaluationDocument> corpus,
        IReadOnlyCollection<string> candidates)
    {
        List<EvaluationDocument> pool = [];
        HashSet<string> seenIds = new(StringComparer.Ordinal);

        foreach (string candidate in candidates)
        {
            HashSet<string> queryTokens = ProductSemanticAdapter.Normalize(candidate)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (EvaluationDocument document in corpus)
            {
                if (seenIds.Contains(document.Id))
                {
                    continue;
                }

                HashSet<string> documentTokens = ProductSemanticAdapter.Normalize(EvaluationText.DocumentText(document))
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                if (documentTokens.Overlaps(queryTokens))
                {
                    seenIds.Add(document.Id);
                    pool.Add(document);
                }
            }
        }

        return pool;
    }
}

internal static class ProductSemanticAdapter
{
    private const string NormalizerTypeName = "NasaExplorer.Application.Features.Search.SemanticSearchQueryNormalizer";
    private const string RankerTypeName = "NasaExplorer.Application.Features.Search.SemanticSearchRanker";
    private const string PositionedImageTypeName = "NasaExplorer.Application.Features.Search.SemanticSearchPositionedImage";
    private static readonly Assembly ApplicationAssembly = typeof(SemanticSearchEffectiveFilters).Assembly;
    private static readonly Type NormalizerType = ApplicationAssembly.GetType(NormalizerTypeName, true)!;
    private static readonly Type RankerType = ApplicationAssembly.GetType(RankerTypeName, true)!;
    private static readonly Type PositionedImageType = ApplicationAssembly.GetType(PositionedImageTypeName, true)!;
    private static readonly MethodInfo NormalizeMethod = NormalizerType.GetMethod(
        "Normalize",
        BindingFlags.Public | BindingFlags.Static,
        [typeof(string)]) ?? throw new MissingMethodException(NormalizerTypeName, "Normalize");
    private static readonly MethodInfo CandidateMethod = NormalizerType.GetMethod(
        "BuildCandidateQueries",
        BindingFlags.Public | BindingFlags.Static,
        [typeof(string), typeof(string)]) ?? throw new MissingMethodException(NormalizerTypeName, "BuildCandidateQueries");
    private static readonly MethodInfo RankMethod = RankerType.GetMethod(
        "Rank",
        BindingFlags.Public | BindingFlags.Static) ?? throw new MissingMethodException(RankerTypeName, "Rank");

    public static string Normalize(string value)
    {
        return (string)(NormalizeMethod.Invoke(null, [value])
            ?? throw new InvalidOperationException("The product normalizer returned null."));
    }

    public static IReadOnlyCollection<string> BuildCandidateQueries(string original, string optimized)
    {
        return (IReadOnlyCollection<string>)(CandidateMethod.Invoke(null, [original, optimized])
            ?? throw new InvalidOperationException("The product candidate builder returned null."));
    }

    public static IReadOnlyCollection<string> BuildProductionCandidates(
        string originalQuery,
        SemanticSearchPlan plan)
    {
        string primary = BuildOptimizedQuery(plan.PrimaryQuery, plan.RequiredTerms, plan.ExcludedTerms);
        string alternativeSource = string.IsNullOrWhiteSpace(plan.AlternativeQuery)
            ? originalQuery
            : plan.AlternativeQuery;
        string alternative = BuildOptimizedQuery(alternativeSource, plan.RequiredTerms, plan.ExcludedTerms);
        return BuildCandidateQueries(alternative, primary).Take(2).ToArray();
    }

    public static IReadOnlyCollection<SemanticSearchRankedImage> Rank(
        IReadOnlyList<EvaluationDocument> source,
        SemanticSearchPlan plan,
        EvaluationFilters filters)
    {
        IList positionedImages = (IList)(Activator.CreateInstance(typeof(List<>).MakeGenericType(PositionedImageType))
            ?? throw new InvalidOperationException("Could not create the product positioned-image list."));

        for (int index = 0; index < source.Count; index++)
        {
            object positionedImage = Activator.CreateInstance(
                PositionedImageType,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                [source[index].ToProductionImage(), index],
                CultureInfo.InvariantCulture) ?? throw new InvalidOperationException("Could not create a product positioned image.");
            positionedImages.Add(positionedImage);
        }

        object ranked = RankMethod.Invoke(null, [positionedImages, plan, filters.ToProductionFilters()])
            ?? throw new InvalidOperationException("The product ranker returned null.");
        return ((IEnumerable)ranked).Cast<SemanticSearchRankedImage>().ToArray();
    }

    private static string BuildOptimizedQuery(
        string baseQuery,
        IReadOnlyCollection<string> requiredTerms,
        IReadOnlyCollection<string> excludedTerms)
    {
        string normalized = Normalize(string.Join(' ', requiredTerms.Prepend(baseQuery)));
        HashSet<string> excludedTokens = excludedTerms
            .SelectMany(term => Normalize(term)
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
}

internal static partial class EvaluationText
{
    public static string DocumentText(EvaluationDocument document)
    {
        return string.Join(' ',
        [
            document.Title,
            document.Description,
            .. document.Keywords,
            document.Mission ?? string.Empty,
            document.Rover ?? string.Empty,
            document.Camera ?? string.Empty
        ]);
    }

    public static HashSet<string> DocumentTokens(EvaluationDocument document)
    {
        return Tokenize(DocumentText(document));
    }

    public static HashSet<string> Tokenize(string value)
    {
        string decomposed = value.Normalize(NormalizationForm.FormD);
        StringBuilder normalized = new(decomposed.Length);

        foreach (char character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                normalized.Append(char.ToLowerInvariant(character));
            }
        }

        return TokenSeparator()
            .Split(normalized.ToString().Normalize(NormalizationForm.FormC))
            .Where(token => token.Length > 1)
            .ToHashSet(StringComparer.Ordinal);
    }

    [GeneratedRegex(@"[^\p{L}\p{Nd}]+", RegexOptions.CultureInvariant)]
    private static partial Regex TokenSeparator();
}

internal static class EvaluationFilterMatcher
{
    public static bool Matches(EvaluationDocument document, EvaluationFilters filters)
    {
        if (filters.DateFrom.HasValue || filters.DateTo.HasValue)
        {
            if (!document.DateCreated.HasValue)
            {
                return false;
            }

            DateOnly date = DateOnly.FromDateTime(document.DateCreated.Value.UtcDateTime);
            if ((filters.DateFrom.HasValue && date < filters.DateFrom.Value)
                || (filters.DateTo.HasValue && date > filters.DateTo.Value))
            {
                return false;
            }
        }

        return MatchesMetadata(document.Mission, filters.Mission)
            && MatchesMetadata(document.Rover, filters.Rover)
            && MatchesMetadata(document.Camera, filters.Camera);
    }

    private static bool MatchesMetadata(
        string? documentMetadata,
        string? requiredValue)
    {
        if (string.IsNullOrWhiteSpace(requiredValue))
        {
            return true;
        }

        string searchable = ProductSemanticAdapter.Normalize(documentMetadata ?? string.Empty);
        string required = ProductSemanticAdapter.Normalize(requiredValue);
        return !string.IsNullOrWhiteSpace(searchable)
            && !string.IsNullOrWhiteSpace(required)
            && string.Equals(searchable, required, StringComparison.OrdinalIgnoreCase);
    }
}

public static class SemanticSearchEvaluator
{
    public const int RankingDepth = 10;

    public static EvaluationComparisonReport Compare(FrozenEvaluationDataset dataset)
    {
        return new EvaluationComparisonReport(
            dataset.EvaluationScope,
            Evaluate(dataset, new StandardStrategy()),
            Evaluate(dataset, new CurrentSemanticStrategy()),
            Evaluate(dataset, new NewSemanticStrategy()));
    }

    public static EvaluationReport Evaluate(
        FrozenEvaluationDataset dataset,
        IEvaluationStrategy strategy)
    {
        EvaluationQueryResult[] queryResults = dataset.Queries
            .Select(query => EvaluateQuery(query, dataset, strategy))
            .ToArray();
        Dictionary<string, EvaluationSlice> languageSlices = queryResults
            .GroupBy(result => result.Language, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => BuildSlice(group.ToArray()), StringComparer.Ordinal);
        Dictionary<string, EvaluationSlice> categorySlices = queryResults
            .GroupBy(result => result.Category, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => BuildSlice(group.ToArray()), StringComparer.Ordinal);
        EvaluationSlice overall = BuildSlice(queryResults);

        return new EvaluationReport(
            strategy.Name,
            dataset.Version,
            dataset.CorpusVersion,
            dataset.EvaluationScope,
            overall.QueryCount,
            overall.RankingQueryCount,
            overall.NdcgAt10,
            overall.PrecisionAt10,
            overall.MeanReciprocalRank,
            overall.ZeroResultRate,
            overall.ZeroResultAccuracy,
            overall.ConsistencyRate,
            overall.FilterViolationCount,
            overall.PageDuplicateIdCount,
            languageSlices,
            categorySlices,
            queryResults);
    }

    private static EvaluationQueryResult EvaluateQuery(
        EvaluationQuery query,
        FrozenEvaluationDataset dataset,
        IEvaluationStrategy strategy)
    {
        EvaluationFilters filters = strategy.GetFilters(query, dataset);
        IReadOnlyDictionary<string, EvaluationDocument> documentsById = dataset.Corpus.ToDictionary(document => document.Id, StringComparer.Ordinal);
        HashSet<string> eligibleIds = dataset.Corpus
            .Where(document => EvaluationFilterMatcher.Matches(document, filters))
            .Select(document => document.Id)
            .ToHashSet(StringComparer.Ordinal);
        int retrievalDepth = RankingDepth * 2;
        IReadOnlyList<string> firstRun = strategy.Search(query, dataset, retrievalDepth);
        IReadOnlyList<string> secondRun = strategy.Search(query, dataset, retrievalDepth);
        string[] rankedIds = firstRun.Take(RankingDepth).ToArray();
        bool isConsistent = firstRun.SequenceEqual(secondRun, StringComparer.Ordinal)
            && firstRun.Distinct(StringComparer.Ordinal).Count() == firstRun.Count;
        int pageDuplicateIdCount = firstRun
            .Take(RankingDepth)
            .Intersect(firstRun.Skip(RankingDepth).Take(RankingDepth), StringComparer.Ordinal)
            .Count();
        int filterViolationCount = firstRun.Count(id =>
            !documentsById.TryGetValue(id, out EvaluationDocument? document)
            || !EvaluationFilterMatcher.Matches(document, filters));
        double dcg = DiscountedCumulativeGain(rankedIds, query.Judgments);
        double idealDcg = IdealDiscountedCumulativeGain(query.Judgments, eligibleIds);
        double ndcg = idealDcg == 0 ? 0 : dcg / idealDcg;
        int relevantCount = rankedIds.Count(id => query.Judgments.GetValueOrDefault(id) > 0);
        int firstRelevantIndex = rankedIds
            .Select((id, index) => new { id, index })
            .Where(item => query.Judgments.GetValueOrDefault(item.id) > 0)
            .Select(item => item.index)
            .DefaultIfEmpty(-1)
            .First();

        return new EvaluationQueryResult(
            query.Id,
            query.Language,
            query.Intent,
            query.Category,
            query.ExpectsZeroResults,
            ndcg,
            relevantCount / (double)RankingDepth,
            firstRelevantIndex < 0 ? 0 : 1d / (firstRelevantIndex + 1),
            rankedIds.Length == 0,
            isConsistent,
            filterViolationCount,
            pageDuplicateIdCount,
            rankedIds);
    }

    private static double DiscountedCumulativeGain(
        IReadOnlyList<string> rankedIds,
        IReadOnlyDictionary<string, int> judgments)
    {
        return rankedIds
            .Take(RankingDepth)
            .Select((id, index) => Gain(judgments.GetValueOrDefault(id), index))
            .Sum();
    }

    private static double IdealDiscountedCumulativeGain(
        IReadOnlyDictionary<string, int> judgments,
        IReadOnlySet<string> eligibleIds)
    {
        return judgments
            .Where(judgment => eligibleIds.Contains(judgment.Key))
            .Select(judgment => judgment.Value)
            .OrderByDescending(relevance => relevance)
            .Take(RankingDepth)
            .Select(Gain)
            .Sum();
    }

    private static double Gain(int relevance, int index)
    {
        return (Math.Pow(2, relevance) - 1) / Math.Log2(index + 2);
    }

    private static EvaluationSlice BuildSlice(IReadOnlyCollection<EvaluationQueryResult> results)
    {
        EvaluationQueryResult[] rankingResults = results.Where(result => !result.ExpectsZeroResults).ToArray();
        return new EvaluationSlice(
            results.Count,
            rankingResults.Length,
            AverageOrZero(rankingResults.Select(result => result.NdcgAt10)),
            AverageOrZero(rankingResults.Select(result => result.PrecisionAt10)),
            AverageOrZero(rankingResults.Select(result => result.MeanReciprocalRank)),
            results.Count(result => result.IsZeroResult) / (double)results.Count,
            results.Count(result => result.IsZeroResult == result.ExpectsZeroResults) / (double)results.Count,
            results.Count(result => result.IsConsistent) / (double)results.Count,
            results.Sum(result => result.FilterViolationCount),
            results.Sum(result => result.PageDuplicateIdCount));
    }

    private static double AverageOrZero(IEnumerable<double> values)
    {
        double[] materialized = values.ToArray();
        return materialized.Length == 0 ? 0 : materialized.Average();
    }
}

public sealed record EvaluationComparisonReport(
    string EvaluationScope,
    EvaluationReport Standard,
    EvaluationReport CurrentSemantic,
    EvaluationReport NewSemantic);

public sealed record EvaluationReport(
    string Strategy,
    string DatasetVersion,
    string CorpusVersion,
    string EvaluationScope,
    int QueryCount,
    int RankingQueryCount,
    double NdcgAt10,
    double PrecisionAt10,
    double MeanReciprocalRank,
    double ZeroResultRate,
    double ZeroResultAccuracy,
    double ConsistencyRate,
    int FilterViolationCount,
    int PageDuplicateIdCount,
    IReadOnlyDictionary<string, EvaluationSlice> LanguageSlices,
    IReadOnlyDictionary<string, EvaluationSlice> CategorySlices,
    IReadOnlyList<EvaluationQueryResult> Queries);

public sealed record EvaluationSlice(
    int QueryCount,
    int RankingQueryCount,
    double NdcgAt10,
    double PrecisionAt10,
    double MeanReciprocalRank,
    double ZeroResultRate,
    double ZeroResultAccuracy,
    double ConsistencyRate,
    int FilterViolationCount,
    int PageDuplicateIdCount);

public sealed record EvaluationQueryResult(
    string QueryId,
    string Language,
    string Intent,
    string Category,
    bool ExpectsZeroResults,
    double NdcgAt10,
    double PrecisionAt10,
    double MeanReciprocalRank,
    bool IsZeroResult,
    bool IsConsistent,
    int FilterViolationCount,
    int PageDuplicateIdCount,
    IReadOnlyList<string> RankedDocumentIds);
