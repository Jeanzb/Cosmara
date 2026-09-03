namespace NasaExplorer.Application.DTOs.Search;

public sealed class NasaSearchResultDto
{
    public IReadOnlyCollection<NasaImageDto> Images { get; set; } = [];

    public int TotalHits { get; set; }

    public int Page { get; set; }

    public int PageSize { get; set; }

    public string SearchId { get; set; } = string.Empty;

    public string Mode { get; set; } = "standard";

    public string? InterpretedQuery { get; set; }

    public AppliedFiltersDto AppliedFilters { get; set; } = new();

    public bool Degraded { get; set; }

    public string? DegradationReason { get; set; }

    public string? NextCursor { get; set; }

    public IReadOnlyCollection<RelaxationSuggestionDto> RelaxationSuggestions { get; set; } = [];
}
