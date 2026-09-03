using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace NasaExplorer.Application.Features.Search;

internal static partial class SemanticSearchQueryNormalizer
{
    private const int MaximumCandidateQueries = 2;

    private static readonly IReadOnlyDictionary<string, string> TokenTranslations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["amanecer"] = "sunrise",
        ["astronauta"] = "astronaut",
        ["astronautas"] = "astronaut",
        ["atardecer"] = "sunset",
        ["aurora"] = "aurora",
        ["camara"] = "camera",
        ["camaras"] = "camera",
        ["cohete"] = "rocket",
        ["cohetes"] = "rocket",
        ["cometa"] = "comet",
        ["cometas"] = "comet",
        ["crater"] = "crater",
        ["crateres"] = "craters",
        ["eclipse"] = "eclipse",
        ["eclipses"] = "eclipse",
        ["espacial"] = "space",
        ["espacio"] = "space",
        ["estrella"] = "star",
        ["estrellas"] = "stars",
        ["galaxia"] = "galaxy",
        ["galaxias"] = "galaxies",
        ["jupiter"] = "jupiter",
        ["luna"] = "moon",
        ["lunar"] = "moon",
        ["marciano"] = "martian",
        ["marcianos"] = "martian",
        ["marte"] = "mars",
        ["nebulosa"] = "nebula",
        ["nebulosas"] = "nebulae",
        ["planeta"] = "planet",
        ["planetas"] = "planets",
        ["rover"] = "rover",
        ["rovers"] = "rovers",
        ["satelite"] = "satellite",
        ["satelites"] = "satellites",
        ["saturno"] = "saturn",
        ["sol"] = "sun",
        ["telescopio"] = "telescope",
        ["telescopios"] = "telescopes",
        ["tierra"] = "earth"
    };

    private static readonly IReadOnlySet<string> StopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "a",
        "al",
        "capturada",
        "capturadas",
        "capturado",
        "capturados",
        "con",
        "de",
        "del",
        "el",
        "en",
        "la",
        "las",
        "los",
        "mostrame",
        "mostrar",
        "muestrame",
        "por",
        "que",
        "quiero",
        "show",
        "me",
        "please",
        "with",
        "by",
        "from"
    };

    private static readonly IReadOnlyDictionary<string, string> PhraseTranslations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["agujero negro"] = "black hole",
        ["aurora boreal"] = "aurora",
        ["campo profundo"] = "deep field",
        ["estacion espacial"] = "space station",
        ["lanzamiento espacial"] = "space launch",
        ["misiones apolo"] = "apollo missions",
        ["planeta rojo"] = "mars",
        ["puesta de sol"] = "sunset",
        ["sistema solar"] = "solar system",
        ["transbordador espacial"] = "space shuttle",
        ["via lactea"] = "milky way"
    };

    public static string Normalize(string query)
    {
        string normalizedQuery = RemoveDiacritics(query).ToLowerInvariant().Trim();

        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            return string.Empty;
        }

        foreach ((string source, string target) in PhraseTranslations)
        {
            normalizedQuery = Regex.Replace(
                normalizedQuery,
                $@"\b{Regex.Escape(source)}\b",
                target,
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        }

        string[] terms = SearchTermSeparator()
            .Split(normalizedQuery)
            .Where(term => !string.IsNullOrWhiteSpace(term))
            .Where(term => !StopWords.Contains(term))
            .Select(term => TokenTranslations.GetValueOrDefault(term, term))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return string.Join(' ', terms);
    }

    public static IReadOnlyCollection<string> BuildCandidateQueries(string originalQuery, string optimizedQuery)
    {
        List<string> candidates = [];
        AddCandidate(candidates, Normalize(optimizedQuery));
        AddCandidate(candidates, Normalize(originalQuery));

        return candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaximumCandidateQueries)
            .ToArray();
    }

    private static void AddCandidate(List<string> candidates, string candidate)
    {
        string trimmed = candidate.Trim();

        if (!string.IsNullOrWhiteSpace(trimmed))
        {
            candidates.Add(trimmed);
        }
    }

    private static string RemoveDiacritics(string value)
    {
        string normalized = value.Normalize(NormalizationForm.FormD);
        StringBuilder builder = new(normalized.Length);

        foreach (char character in normalized)
        {
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(character);

            if (category != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    [GeneratedRegex(@"[^\p{L}\p{Nd}]+", RegexOptions.CultureInvariant)]
    private static partial Regex SearchTermSeparator();
}
