using Microsoft.Extensions.Logging;
using NasaExplorer.Application.Features.Search.Commands.TrackSearchEvent;

namespace NasaExplorer.Application.Tests.Features.Search.Commands.TrackSearchEvent;

public sealed class TrackSearchEventCommandHandlerTests
{
    [Fact]
    public async Task Handle_logs_only_the_validated_structured_fields()
    {
        CapturingLogger<TrackSearchEventCommandHandler> logger = new();
        TrackSearchEventCommandHandler handler = new(logger);

        await handler.Handle(new TrackSearchEventCommand
        {
            EventName = "result_opened",
            SearchId = "search-123",
            ResultId = "PIA-123",
            Mode = "semantic",
            ResultCount = 1,
            Reason = null
        }, CancellationToken.None);

        IReadOnlyDictionary<string, object?> properties = Assert.Single(logger.Events);
        Assert.Equal("result_opened", properties["SearchEvent"]);
        Assert.Equal("search-123", properties["SearchId"]);
        Assert.Equal("PIA-123", properties["ResultId"]);
        Assert.Equal("semantic", properties["SearchMode"]);
        Assert.DoesNotContain(properties.Keys, key => key.Contains("query", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<IReadOnlyDictionary<string, object?>> Events { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Dictionary<string, object?> values = ((IEnumerable<KeyValuePair<string, object?>>)(object)state!)
                .Where(pair => pair.Key != "{OriginalFormat}")
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            Events.Add(values);
        }
    }
}
