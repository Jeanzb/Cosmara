using MediatR;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NasaExplorer.Application.Features.Search.Commands.TrackSearchEvent;

public sealed class TrackSearchEventCommand : IRequest
{
    public string? EventName { get; init; }

    public string? SearchId { get; init; }

    public string? ResultId { get; init; }

    public string? Mode { get; init; }

    public int? ResultCount { get; init; }

    public string? Reason { get; init; }

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalProperties { get; init; }
}
