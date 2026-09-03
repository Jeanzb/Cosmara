using NasaExplorer.Domain.Entities.Collections;
using NasaExplorer.Domain.Models.Ai;

namespace NasaExplorer.Domain.Interfaces.Services;

public interface IAiEnrichmentService
{
    SemanticSearchPlannerIdentity SemanticSearchIdentity { get; }

    Task<AiImageEnrichmentResult> EnrichImageAsync(string imageTitle, string? imageDescription, CancellationToken cancellationToken = default);

    Task<string> CompareImagesAsync(IReadOnlyCollection<CollectionImage> images, string language, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<string>> SuggestTagsAsync(string imageTitle, string? imageDescription, CancellationToken cancellationToken = default);

    Task<SemanticSearchPlan> CreateSemanticSearchPlanAsync(
        string naturalLanguageQuery,
        string locale,
        CancellationToken cancellationToken = default);
}
