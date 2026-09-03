using MediatR;
using NasaExplorer.Application.DTOs.Search;

namespace NasaExplorer.Application.Features.Search.Queries.SemanticSearchNasaImages;

public sealed record SemanticSearchNasaImagesQuery(
    string? Query,
    DateOnly? DateFrom,
    DateOnly? DateTo,
    string? Rover,
    string? Camera,
    string? Mission,
    int Page,
    int PageSize,
    string? Locale = null,
    string? Cursor = null,
    string? SuppressInferred = null) : IRequest<NasaSearchResultDto>;
