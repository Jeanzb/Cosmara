using FluentValidation;

namespace NasaExplorer.Application.Features.Search.Commands.TrackSearchEvent;

public sealed class TrackSearchEventCommandValidator : AbstractValidator<TrackSearchEventCommand>
{
    private static readonly IReadOnlySet<string> AllowedEvents = new HashSet<string>(StringComparer.Ordinal)
    {
        "search_submitted",
        "search_completed",
        "result_opened",
        "suggestion_applied",
        "search_error",
        "search_session_expired"
    };

    private static readonly IReadOnlySet<string> AllowedModes = new HashSet<string>(StringComparer.Ordinal)
    {
        "standard",
        "semantic"
    };

    private static readonly IReadOnlySet<string> AllowedReasons = new HashSet<string>(StringComparer.Ordinal)
    {
        "request_failed",
        "search_session_expired",
        "nasa_upstream_unavailable",
        "validation_error",
        "network_error",
        "unknown_error",
        "no_results",
        "ai_unavailable",
        "ai_invalid_response",
        "ai_timeout",
        "partial_nasa_failure",
        "cache_unavailable",
        "remove_date_range",
        "remove_mission",
        "remove_rover",
        "remove_camera"
    };

    public TrackSearchEventCommandValidator()
    {
        RuleFor(command => command.EventName)
            .NotEmpty()
            .Must(value => value is not null && AllowedEvents.Contains(value))
            .WithMessage("EventName is not supported.");

        RuleFor(command => command.Mode)
            .NotEmpty()
            .Must(value => value is not null && AllowedModes.Contains(value))
            .WithMessage("Mode must be standard or semantic.");

        RuleFor(command => command.SearchId)
            .MaximumLength(128)
            .Matches("^[A-Za-z0-9_-]+$")
            .When(command => !string.IsNullOrWhiteSpace(command.SearchId));

        RuleFor(command => command.ResultId)
            .MaximumLength(160)
            .Matches("^[A-Za-z0-9._:-]+$")
            .When(command => !string.IsNullOrWhiteSpace(command.ResultId));

        RuleFor(command => command.ResultCount)
            .InclusiveBetween(0, 1_000_000)
            .When(command => command.ResultCount.HasValue);

        RuleFor(command => command.Reason)
            .Must(value => value is null || AllowedReasons.Contains(value))
            .WithMessage("Reason is not supported.");

        RuleFor(command => command.AdditionalProperties)
            .Must(properties => properties is null || properties.Count == 0)
            .WithMessage("Additional properties are not allowed.");
    }
}
