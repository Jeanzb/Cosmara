namespace NasaExplorer.Application.DTOs.Search;

public sealed class NasaImageDto
{
    public string NasaImageId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? Center { get; set; }

    public string? Mission { get; set; }

    public string? Rover { get; set; }

    public string? Camera { get; set; }

    public string MediaType { get; set; } = string.Empty;

    public string ThumbnailUrl { get; set; } = string.Empty;

    public string ImageUrl { get; set; } = string.Empty;

    public string? SourceUrl { get; set; }

    public DateTimeOffset? DateCreated { get; set; }

    public string? DisplayDate { get; set; }

    public string AspectRatio { get; set; } = "4/3";

    public NasaImageUrlsDto Urls { get; set; } = new();

    public NasaImageTimelineDto? Timeline { get; set; }

    public IReadOnlyCollection<string> Keywords { get; set; } = [];

    public double? RelevanceScore { get; set; }

    public IReadOnlyCollection<string> MatchReasons { get; set; } = [];
}
