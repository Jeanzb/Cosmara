using NasaExplorer.Application.Features.Search.Commands.TrackSearchEvent;
using System.Text.Json;

namespace NasaExplorer.Application.Tests.Features.Search.Commands.TrackSearchEvent;

public sealed class TrackSearchEventCommandValidatorTests
{
    private readonly TrackSearchEventCommandValidator _validator = new();

    [Theory]
    [InlineData("search_submitted")]
    [InlineData("search_completed")]
    [InlineData("result_opened")]
    [InlineData("suggestion_applied")]
    [InlineData("search_error")]
    [InlineData("search_session_expired")]
    public void Validate_accepts_allowed_event_names(string eventName)
    {
        FluentValidation.Results.ValidationResult result = _validator.Validate(new TrackSearchEventCommand
        {
            EventName = eventName,
            Mode = "semantic",
            SearchId = "abc_123",
            ResultId = "PIA-123.4",
            ResultCount = 12,
            Reason = eventName == "search_error" ? "request_failed" : null
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_rejects_unknown_events_and_free_form_reasons()
    {
        FluentValidation.Results.ValidationResult result = _validator.Validate(new TrackSearchEventCommand
        {
            EventName = "query_recorded",
            Mode = "semantic",
            Reason = "the full user query"
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(TrackSearchEventCommand.EventName));
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(TrackSearchEventCommand.Reason));
    }

    [Fact]
    public void Validate_rejects_unknown_json_properties_including_query()
    {
        using JsonDocument document = JsonDocument.Parse("\"sensitive query\"");
        TrackSearchEventCommand command = new()
        {
            EventName = "search_submitted",
            Mode = "standard",
            AdditionalProperties = new Dictionary<string, JsonElement>
            {
                ["query"] = document.RootElement.Clone()
            }
        };

        FluentValidation.Results.ValidationResult result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(TrackSearchEventCommand.AdditionalProperties));
    }
}
