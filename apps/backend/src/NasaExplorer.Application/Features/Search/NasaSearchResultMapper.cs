using NasaExplorer.Application.DTOs.Search;
using NasaExplorer.Domain.Models.Nasa;
using System.Globalization;

namespace NasaExplorer.Application.Features.Search;

internal static class NasaSearchResultMapper
{
    public static NasaSearchResultDto ToDto(NasaSearchResult result)
    {
        return new NasaSearchResultDto
        {
            SearchId = Guid.NewGuid().ToString("N"),
            Mode = "standard",
            TotalHits = result.TotalHits,
            Page = result.Page,
            PageSize = result.PageSize,
            Images = result.Images.Select(image => new NasaImageDto
            {
                NasaImageId = image.NasaImageId,
                Title = image.Title,
                Description = image.Description,
                Center = image.Center,
                Mission = image.Mission,
                Rover = image.Rover,
                Camera = image.Camera,
                MediaType = image.MediaType,
                ThumbnailUrl = image.ThumbnailUrl,
                ImageUrl = image.ImageUrl,
                SourceUrl = image.SourceUrl,
                DateCreated = image.DateCreated,
                DisplayDate = image.DateCreated?.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                AspectRatio = image.AspectRatio,
                Urls = new NasaImageUrlsDto
                {
                    Thumbnail = image.ThumbnailUrl,
                    Card = image.CardUrl ?? image.ThumbnailUrl,
                    Preview = image.PreviewUrl ?? image.ImageUrl,
                    Full = image.FullUrl ?? image.ImageUrl,
                    Source = image.SourceUrl
                },
                Timeline = image.DateCreated.HasValue
                    ? new NasaImageTimelineDto
                    {
                        Year = image.DateCreated.Value.UtcDateTime.Year,
                        Month = image.DateCreated.Value.UtcDateTime.Month,
                        Day = image.DateCreated.Value.UtcDateTime.Day,
                        Date = image.DateCreated.Value.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    }
                    : null,
                Keywords = image.Keywords
            }).ToArray()
        };
    }
}
