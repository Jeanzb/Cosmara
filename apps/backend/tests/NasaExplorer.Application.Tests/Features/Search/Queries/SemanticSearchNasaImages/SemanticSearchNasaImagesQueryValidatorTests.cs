using NasaExplorer.Application.Features.Search.Queries.SemanticSearchNasaImages;

namespace NasaExplorer.Application.Tests.Features.Search.Queries.SemanticSearchNasaImages;

public sealed class SemanticSearchNasaImagesQueryValidatorTests
{
    private readonly SemanticSearchNasaImagesQueryValidator _validator = new();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_requires_query_for_initial_request(string? query)
    {
        FluentValidation.Results.ValidationResult result = _validator.Validate(CreateQuery(query));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(SemanticSearchNasaImagesQuery.Query));
    }

    [Fact]
    public void Validate_allows_cursor_request_without_query()
    {
        SemanticSearchNasaImagesQuery request = CreateQuery(null) with { Cursor = "abc123" };

        FluentValidation.Results.ValidationResult result = _validator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("english")]
    [InlineData("e")]
    [InlineData("es_CO")]
    [InlineData("en-US")]
    [InlineData("fr")]
    public void Validate_rejects_invalid_locale(string locale)
    {
        FluentValidation.Results.ValidationResult result = _validator.Validate(CreateQuery("mars") with { Locale = locale });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(SemanticSearchNasaImagesQuery.Locale));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("EN")]
    [InlineData("ES")]
    public void Validate_accepts_supported_locale(string locale)
    {
        FluentValidation.Results.ValidationResult result = _validator.Validate(CreateQuery("mars") with { Locale = locale });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_accepts_unknown_suppression_keys_for_forward_compatible_ignoring()
    {
        SemanticSearchNasaImagesQuery request = CreateQuery("mars") with
        {
            SuppressInferred = "rover,futureFilter"
        };

        FluentValidation.Results.ValidationResult result = _validator.Validate(request);

        Assert.True(result.IsValid);
    }

    private static SemanticSearchNasaImagesQuery CreateQuery(string? query)
    {
        return new SemanticSearchNasaImagesQuery(query, null, null, null, null, null, 1, 24);
    }
}
