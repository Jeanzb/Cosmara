using System.Text.Json;
using Xunit.Abstractions;

namespace NasaExplorer.Search.Evaluation;

public sealed class SemanticSearchEvaluationTests
{
    private readonly ITestOutputHelper _output;

    public SemanticSearchEvaluationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Frozen_fixture_contains_balanced_bilingual_queries_and_explicit_category_coverage()
    {
        FrozenEvaluationDataset dataset = FrozenEvaluationDataset.Load();

        Assert.Equal("2026-08-25.v2", dataset.Version);
        Assert.Equal(48, dataset.Queries.Count);
        Assert.Equal(24, dataset.Queries.Count(query => query.Language == "es"));
        Assert.Equal(24, dataset.Queries.Count(query => query.Language == "en"));
        Assert.Equal(FrozenEvaluationDataset.RequiredCategories, dataset.Queries.Select(query => query.Category).ToHashSet(StringComparer.Ordinal));

        foreach (string category in FrozenEvaluationDataset.RequiredCategories)
        {
            EvaluationQuery[] queries = dataset.Queries.Where(query => query.Category == category).ToArray();
            Assert.Equal(8, queries.Length);
            Assert.Equal(4, queries.Count(query => query.Language == "es"));
            Assert.Equal(4, queries.Count(query => query.Language == "en"));
        }

        Assert.All(
            dataset.Queries.Where(query => !query.ExpectsZeroResults),
            query => Assert.Equal(new[] { 0, 1, 2, 3 }, query.Judgments.Values.Order().ToArray()));
        Assert.All(
            dataset.Queries.Where(query => query.ExpectsZeroResults),
            query => Assert.All(query.Judgments.Values, value => Assert.Equal(0, value)));
    }

    [Fact]
    public void Product_adapter_uses_the_compiled_normalizer_and_ranker()
    {
        string normalized = ProductSemanticAdapter.Normalize("atardecer en Marte");

        Assert.Equal("sunset mars", normalized);

        FrozenEvaluationDataset dataset = FrozenEvaluationDataset.Load();
        EvaluationReport report = SemanticSearchEvaluator.Evaluate(dataset, new NewSemanticStrategy());

        Assert.Equal("NewSemantic", report.Strategy);
        Assert.Equal(0, report.FilterViolationCount);
    }

    [Fact]
    public void Frozen_benchmark_reports_all_three_strategies_and_discloses_scope()
    {
        FrozenEvaluationDataset dataset = FrozenEvaluationDataset.Load();
        EvaluationComparisonReport comparison = SemanticSearchEvaluator.Compare(dataset);

        _output.WriteLine(JsonSerializer.Serialize(new
        {
            comparison.EvaluationScope,
            Standard = ToSummary(comparison.Standard),
            CurrentSemantic = ToSummary(comparison.CurrentSemantic),
            NewSemantic = ToSummary(comparison.NewSemantic)
        }, new JsonSerializerOptions
        {
            WriteIndented = true
        }));

        Assert.Contains("synthetic", comparison.EvaluationScope, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("offline", comparison.EvaluationScope, StringComparison.OrdinalIgnoreCase);
        AssertMetricsAreValid(comparison.Standard);
        AssertMetricsAreValid(comparison.CurrentSemantic);
        AssertMetricsAreValid(comparison.NewSemantic);
    }

    [Fact]
    public void New_semantic_meets_quality_language_filter_page_and_consistency_gates()
    {
        FrozenEvaluationDataset dataset = FrozenEvaluationDataset.Load();
        EvaluationComparisonReport comparison = SemanticSearchEvaluator.Compare(dataset);
        EvaluationReport standard = comparison.Standard;
        EvaluationReport current = comparison.CurrentSemantic;
        EvaluationReport updated = comparison.NewSemantic;

        Assert.True(
            updated.NdcgAt10 >= standard.NdcgAt10 * 1.10,
            $"Expected NewSemantic nDCG@10 ({updated.NdcgAt10:F4}) to improve Standard ({standard.NdcgAt10:F4}) by at least 10%.");

        foreach (string language in new[] { "es", "en" })
        {
            Assert.True(
                updated.LanguageSlices[language].NdcgAt10 >= standard.LanguageSlices[language].NdcgAt10 * 0.95,
                $"NewSemantic {language} nDCG@10 regressed by more than 5% versus Standard.");
        }

        Assert.True(updated.NdcgAt10 >= current.NdcgAt10);
        Assert.Equal(1, updated.ZeroResultAccuracy);
        Assert.Equal(1, updated.ConsistencyRate);

        foreach (EvaluationReport report in new[] { standard, current, updated })
        {
            Assert.Equal(0, report.FilterViolationCount);
            Assert.Equal(0, report.PageDuplicateIdCount);
        }
    }

    [Fact]
    public void Frozen_strategy_comparison_is_repeatable()
    {
        FrozenEvaluationDataset dataset = FrozenEvaluationDataset.Load();

        EvaluationComparisonReport first = SemanticSearchEvaluator.Compare(dataset);
        EvaluationComparisonReport second = SemanticSearchEvaluator.Compare(dataset);

        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
    }

    [Fact]
    public void Evaluation_harness_detects_filter_violations_and_cross_page_duplicates()
    {
        FrozenEvaluationDataset dataset = FrozenEvaluationDataset.Load();

        EvaluationReport report = SemanticSearchEvaluator.Evaluate(dataset, new DeliberatelyInvalidStrategy());

        Assert.True(report.FilterViolationCount > 0);
        Assert.True(report.PageDuplicateIdCount > 0);
        Assert.True(report.ConsistencyRate < 1);
    }

    [Fact]
    public void Filter_audit_rejects_textual_mentions_that_conflict_with_structured_metadata()
    {
        EvaluationDocument document = new()
        {
            Id = "conflicting-rover",
            Title = "Curiosity comparison",
            Description = "A Curiosity image mentioned for comparison",
            Keywords = ["Curiosity"],
            Rover = "Perseverance"
        };

        Assert.False(EvaluationFilterMatcher.Matches(
            document,
            new EvaluationFilters(Rover: "Curiosity")));

        Assert.False(EvaluationFilterMatcher.Matches(
            new EvaluationDocument
            {
                Id = "mastcam-z",
                Title = "Mars terrain",
                Description = "Mars terrain captured with Mastcam-Z",
                Keywords = ["Mars", "Mastcam-Z"],
                Camera = "Mastcam-Z"
            },
            new EvaluationFilters(Camera: "Mastcam")));
    }

    private static void AssertMetricsAreValid(EvaluationReport report)
    {
        Assert.Equal(48, report.QueryCount);
        Assert.Equal(40, report.RankingQueryCount);
        Assert.InRange(report.NdcgAt10, 0, 1);
        Assert.InRange(report.PrecisionAt10, 0, 1);
        Assert.InRange(report.MeanReciprocalRank, 0, 1);
        Assert.InRange(report.ZeroResultRate, 0, 1);
        Assert.InRange(report.ZeroResultAccuracy, 0, 1);
        Assert.InRange(report.ConsistencyRate, 0, 1);
        Assert.Equal(24, report.LanguageSlices["es"].QueryCount);
        Assert.Equal(24, report.LanguageSlices["en"].QueryCount);
        Assert.Equal(FrozenEvaluationDataset.RequiredCategories, report.CategorySlices.Keys.ToHashSet(StringComparer.Ordinal));
    }

    private static object ToSummary(EvaluationReport report)
    {
        return new
        {
            report.Strategy,
            report.QueryCount,
            report.RankingQueryCount,
            report.NdcgAt10,
            report.PrecisionAt10,
            report.MeanReciprocalRank,
            report.ZeroResultRate,
            report.ZeroResultAccuracy,
            report.ConsistencyRate,
            report.FilterViolationCount,
            report.PageDuplicateIdCount,
            report.LanguageSlices,
            report.CategorySlices
        };
    }

    private sealed class DeliberatelyInvalidStrategy : IEvaluationStrategy
    {
        public string Name => "DeliberatelyInvalid";

        public EvaluationFilters GetFilters(EvaluationQuery query, FrozenEvaluationDataset dataset)
        {
            return dataset.SemanticPlans[query.Intent].ToFilters();
        }

        public IReadOnlyList<string> Search(
            EvaluationQuery query,
            FrozenEvaluationDataset dataset,
            int take)
        {
            return Enumerable.Repeat(dataset.Corpus[0].Id, take).ToArray();
        }
    }
}
