import type { NasaSearchFilterKey, NasaSearchFilters, NasaSearchRouteState } from "@/types/search";

const routeTextMaxLength = 240;
const isoDatePattern = /^\d{4}-\d{2}-\d{2}$/;
const suppressibleFilterKeys: readonly NasaSearchFilterKey[] = ["dateFrom", "dateTo", "rover", "camera", "mission"];

const toOptionalText = (value: unknown): string | undefined => {
  if (typeof value !== "string") {
    return undefined;
  }

  const normalized = value.trim();

  return normalized.length > 0 && normalized.length <= routeTextMaxLength ? normalized : undefined;
};

const toOptionalDate = (value: unknown): string | undefined => {
  const normalized = toOptionalText(value);

  return normalized !== undefined && isoDatePattern.test(normalized) ? normalized : undefined;
};

const toSemanticFlag = (value: unknown): true | undefined =>
  value === true || value === "true" || value === "1" ? true : undefined;

const toDatePreset = (value: unknown): NasaSearchRouteState["datePreset"] =>
  value === "custom" || value === "last-30" || value === "last-year" ? value : undefined;

export const parseSuppressedInferences = (value: unknown): NasaSearchFilterKey[] => {
  if (typeof value !== "string") {
    return [];
  }

  return [...new Set(
    value
      .split(",")
      .map((key) => key.trim())
      .filter((key): key is NasaSearchFilterKey => suppressibleFilterKeys.includes(key as NasaSearchFilterKey))
  )];
};

export const serializeSuppressedInferences = (keys: readonly NasaSearchFilterKey[]): string | undefined =>
  keys.length > 0 ? [...new Set(keys)].join(",") : undefined;

export const normalizeSearchRouteState = (search: Record<string, unknown>): NasaSearchRouteState => {
  const query = toOptionalText(search.q);

  return {
    q: query,
    semantic: query === undefined ? undefined : toSemanticFlag(search.semantic),
    datePreset: toDatePreset(search.datePreset),
    dateFrom: toOptionalDate(search.dateFrom),
    dateTo: toOptionalDate(search.dateTo),
    rover: toOptionalText(search.rover),
    camera: toOptionalText(search.camera),
    mission: toOptionalText(search.mission),
    suppressInferred: serializeSuppressedInferences(parseSuppressedInferences(search.suppressInferred))
  };
};

export const toSearchFilters = (
  search: NasaSearchRouteState,
  pageSize: number,
  locale: "en" | "es"
): NasaSearchFilters => ({
  query: search.q ?? "",
  datePreset: search.datePreset ?? (search.dateFrom === undefined && search.dateTo === undefined ? "any" : "custom"),
  dateFrom: search.dateFrom ?? null,
  dateTo: search.dateTo ?? null,
  rover: search.rover ?? null,
  camera: search.camera ?? null,
  mission: search.mission ?? null,
  pageSize,
  locale,
  suppressInferred: parseSuppressedInferences(search.suppressInferred)
});

const toRouteValue = (value: string | null | undefined): string | undefined => {
  const normalized = value?.trim() ?? "";

  return normalized.length > 0 ? normalized : undefined;
};

export const toSearchRouteState = (
  filters: Partial<NasaSearchFilters>,
  semantic: boolean
): NasaSearchRouteState => ({
  q: toRouteValue(filters.query),
  semantic: semantic ? true : undefined,
  datePreset: filters.datePreset === "any" ? undefined : filters.datePreset,
  dateFrom: toRouteValue(filters.dateFrom),
  dateTo: toRouteValue(filters.dateTo),
  rover: toRouteValue(filters.rover),
  camera: toRouteValue(filters.camera),
  mission: toRouteValue(filters.mission),
  suppressInferred: serializeSuppressedInferences(filters.suppressInferred ?? [])
});
