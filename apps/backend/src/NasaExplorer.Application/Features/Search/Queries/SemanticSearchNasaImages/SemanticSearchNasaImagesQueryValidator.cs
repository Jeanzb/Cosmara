using FluentValidation;

namespace NasaExplorer.Application.Features.Search.Queries.SemanticSearchNasaImages;

public sealed class SemanticSearchNasaImagesQueryValidator : AbstractValidator<SemanticSearchNasaImagesQuery>
{
    public SemanticSearchNasaImagesQueryValidator()
    {
        RuleFor(query => query.Query)
            .MaximumLength(240)
            .Must(query => !string.IsNullOrWhiteSpace(query))
            .WithMessage("A semantic search query is required when no cursor is provided.")
            .When(query => string.IsNullOrWhiteSpace(query.Cursor));

        RuleFor(query => query.Rover)
            .MaximumLength(120);

        RuleFor(query => query.Camera)
            .MaximumLength(120);

        RuleFor(query => query.Mission)
            .MaximumLength(120);

        RuleFor(query => query.Page)
            .InclusiveBetween(1, 1_000);

        RuleFor(query => query.PageSize)
            .InclusiveBetween(1, 100);

        RuleFor(query => query.Locale)
            .Must(locale => locale is null
                || locale.Equals("en", StringComparison.OrdinalIgnoreCase)
                || locale.Equals("es", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Locale must be en or es.")
            .When(query => !string.IsNullOrWhiteSpace(query.Locale));

        RuleFor(query => query.Cursor)
            .MaximumLength(128)
            .Matches("^[A-Za-z0-9_-]+$")
            .When(query => !string.IsNullOrWhiteSpace(query.Cursor));

        RuleFor(query => query.SuppressInferred)
            .MaximumLength(120)
            .When(query => !string.IsNullOrWhiteSpace(query.SuppressInferred));

        RuleFor(query => query.DateTo)
            .GreaterThanOrEqualTo(query => query.DateFrom)
            .When(query => query.DateFrom.HasValue && query.DateTo.HasValue);
    }
}
