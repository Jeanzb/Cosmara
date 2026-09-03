namespace NasaExplorer.Domain.Models.Nasa;

public sealed record NasaSearchCriteria(
    string Query,
    DateOnly? DateFrom,
    DateOnly? DateTo,
    string? Rover,
    string? Camera,
    string? Mission,
    int Page,
    int PageSize,
    int PageScanLimit = 8);
