using NasaExplorer.Domain.Models.Ai;
using NasaExplorer.Domain.Models.Nasa;

namespace NasaExplorer.Application.Features.Search;

internal static class SemanticSearchRanker
{
    private const double TitleWeight = 35;
    private const double KeywordsWeight = 30;
    private const double MetadataWeight = 20;
    private const double DescriptionWeight = 10;
    private const double NasaPositionWeight = 5;

    public static IReadOnlyCollection<SemanticSearchRankedImage> Rank(
        IReadOnlyCollection<SemanticSearchPositionedImage> sourceImages,
        SemanticSearchPlan plan,
        SemanticSearchEffectiveFilters filters)
    {
        SemanticSearchPositionedImage[] uniqueImages = sourceImages
            .GroupBy(item => item.Image.NasaImageId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(item => item.OriginalPosition).First())
            .Where(item => MatchesStrictFilters(item.Image, filters))
            .Where(item => !MatchesExcludedTerms(item.Image, plan.ExcludedTerms))
            .ToArray();

        string[] queryTokens = BuildQueryTokens(plan);
        double applicableWeight = TitleWeight
            + KeywordsWeight
            + MetadataWeight
            + DescriptionWeight
            + NasaPositionWeight;
        int maximumPosition = sourceImages.Count == 0
            ? 0
            : sourceImages.Max(item => item.OriginalPosition);

        return uniqueImages
            .Select(item => ScoreImage(
                item,
                queryTokens,
                filters,
                applicableWeight,
                maximumPosition))
            .OrderByDescending(item => item.RelevanceScore)
            .ThenBy(item => item.OriginalPosition)
            .ThenBy(item => item.Image.NasaImageId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static SemanticSearchRankedImage ScoreImage(
        SemanticSearchPositionedImage positionedImage,
        IReadOnlyCollection<string> queryTokens,
        SemanticSearchEffectiveFilters filters,
        double applicableWeight,
        int maximumPosition)
    {
        NasaImageAsset image = positionedImage.Image;
        double titleScore = CalculateCoverage(image.Title, queryTokens);
        double keywordScore = CalculateCoverage(string.Join(' ', image.Keywords), queryTokens);
        double descriptionScore = CalculateCoverage(image.Description, queryTokens);
        double metadataScore = CalculateMetadataScore(image, filters, queryTokens);
        double positionScore = maximumPosition <= 0
            ? 1
            : (maximumPosition - positionedImage.OriginalPosition + 1d) / (maximumPosition + 1d);
        double weightedScore = (titleScore * TitleWeight)
            + (keywordScore * KeywordsWeight)
            + (descriptionScore * DescriptionWeight)
            + (metadataScore * MetadataWeight)
            + (positionScore * NasaPositionWeight);
        List<string> reasons = [];

        AddReason(reasons, "title", titleScore);
        AddReason(reasons, "keywords", keywordScore);
        AddReason(reasons, "metadata", metadataScore);
        AddReason(reasons, "description", descriptionScore);
        AddReason(reasons, "nasa_position", positionScore);

        return new SemanticSearchRankedImage(
            image,
            Math.Round((weightedScore / applicableWeight) * 100, 2, MidpointRounding.AwayFromZero),
            reasons,
            positionedImage.OriginalPosition);
    }

    private static bool MatchesStrictFilters(NasaImageAsset image, SemanticSearchEffectiveFilters filters)
    {
        if (filters.DateFrom.HasValue || filters.DateTo.HasValue)
        {
            if (!image.DateCreated.HasValue)
            {
                return false;
            }

            DateOnly imageDate = DateOnly.FromDateTime(image.DateCreated.Value.UtcDateTime);
            if ((filters.DateFrom.HasValue && imageDate < filters.DateFrom.Value)
                || (filters.DateTo.HasValue && imageDate > filters.DateTo.Value))
            {
                return false;
            }
        }

        return MatchesMetadataFilter(image.Mission, filters.Mission)
            && MatchesMetadataFilter(image.Rover, filters.Rover)
            && MatchesMetadataFilter(image.Camera, filters.Camera);
    }

    private static double CalculateMetadataScore(
        NasaImageAsset image,
        SemanticSearchEffectiveFilters filters,
        IReadOnlyCollection<string> queryTokens)
    {
        List<bool> matches = [];
        AddMetadataMatch(matches, image.Mission, filters.Mission);
        AddMetadataMatch(matches, image.Rover, filters.Rover);
        AddMetadataMatch(matches, image.Camera, filters.Camera);

        if (matches.Count > 0)
        {
            return matches.Count(match => match) / (double)matches.Count;
        }

        string metadata = string.Join(' ', [image.Mission, image.Rover, image.Camera]);
        return CalculateCoverage(metadata, queryTokens);
    }

    private static void AddMetadataMatch(
        List<bool> matches,
        string? imageMetadata,
        string? filter)
    {
        if (!string.IsNullOrWhiteSpace(filter))
        {
            matches.Add(MatchesMetadataFilter(imageMetadata, filter));
        }
    }

    private static bool MatchesMetadataFilter(
        string? imageMetadata,
        string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(imageMetadata))
        {
            return false;
        }

        string normalizedMetadata = SemanticSearchQueryNormalizer.Normalize(imageMetadata);
        string normalizedFilter = SemanticSearchQueryNormalizer.Normalize(filter);

        return !string.IsNullOrWhiteSpace(normalizedMetadata)
            && !string.IsNullOrWhiteSpace(normalizedFilter)
            && string.Equals(normalizedMetadata, normalizedFilter, StringComparison.OrdinalIgnoreCase);
    }

    private static double CalculateCoverage(string? searchableText, IReadOnlyCollection<string> queryTokens)
    {
        if (string.IsNullOrWhiteSpace(searchableText) || queryTokens.Count == 0)
        {
            return 0;
        }

        string normalizedText = $" {SemanticSearchQueryNormalizer.Normalize(searchableText)} ";
        int matches = queryTokens.Count(token => normalizedText.Contains($" {token} ", StringComparison.OrdinalIgnoreCase));

        return matches / (double)queryTokens.Count;
    }

    private static string[] BuildQueryTokens(SemanticSearchPlan plan)
    {
        HashSet<string> excludedTokens = plan.ExcludedTerms
            .SelectMany(value => SemanticSearchQueryNormalizer.Normalize(value)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return plan.RequiredTerms
            .Append(plan.PrimaryQuery)
            .SelectMany(value => SemanticSearchQueryNormalizer.Normalize(value)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(token => !excludedTokens.Contains(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool MatchesExcludedTerms(
        NasaImageAsset image,
        IReadOnlyCollection<string> excludedTerms)
    {
        if (excludedTerms.Count == 0)
        {
            return false;
        }

        string searchableText = string.Join(
            ' ',
            [
                image.Title,
                image.Description ?? string.Empty,
                image.Mission ?? string.Empty,
                image.Rover ?? string.Empty,
                image.Camera ?? string.Empty,
                .. image.Keywords
            ]);

        return excludedTerms.Any(term => ContainsNormalizedPhrase(searchableText, term));
    }

    private static bool ContainsNormalizedPhrase(string searchableText, string filter)
    {
        string normalizedText = $" {SemanticSearchQueryNormalizer.Normalize(searchableText)} ";
        string normalizedFilter = SemanticSearchQueryNormalizer.Normalize(filter);

        return !string.IsNullOrWhiteSpace(normalizedFilter)
            && normalizedText.Contains($" {normalizedFilter} ", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddReason(List<string> reasons, string reason, double componentScore)
    {
        if (componentScore > 0)
        {
            reasons.Add(reason);
        }
    }
}

internal sealed record SemanticSearchPositionedImage(NasaImageAsset Image, int OriginalPosition);
