namespace NasaExplorer.Application.DTOs.Search;

public sealed class AppliedFiltersDto
{
    public DateOnly? DateFrom { get; set; }

    public DateOnly? DateTo { get; set; }

    public string? Rover { get; set; }

    public string? Camera { get; set; }

    public string? Mission { get; set; }

    public IReadOnlyCollection<string> Inferred { get; set; } = [];
}
