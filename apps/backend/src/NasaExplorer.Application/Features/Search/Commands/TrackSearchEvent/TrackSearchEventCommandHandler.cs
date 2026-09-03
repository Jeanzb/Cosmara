using MediatR;
using Microsoft.Extensions.Logging;

namespace NasaExplorer.Application.Features.Search.Commands.TrackSearchEvent;

public sealed class TrackSearchEventCommandHandler : IRequestHandler<TrackSearchEventCommand>
{
    private readonly ILogger<TrackSearchEventCommandHandler> _logger;

    public TrackSearchEventCommandHandler(ILogger<TrackSearchEventCommandHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(TrackSearchEventCommand request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogInformation(
            "Search telemetry {SearchEvent} {SearchId} {ResultId} {SearchMode} {ResultCount} {Reason}.",
            request.EventName,
            request.SearchId,
            request.ResultId,
            request.Mode,
            request.ResultCount,
            request.Reason);

        return Task.CompletedTask;
    }
}
