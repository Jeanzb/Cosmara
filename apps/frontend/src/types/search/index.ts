export type NasaImageTimeline = {
  year: number;
  month: number;
  day: number;
  date: string;
};

export type NasaImageUrls = {
  thumbnail: string;
  card: string;
  preview: string;
  full: string;
  source?: string | null;
};

export type NasaImage = {
  nasaImageId: string;
  title: string;
  description?: string | null;
  center?: string | null;
  mission?: string | null;
  rover?: string | null;
  camera?: string | null;
  mediaType: string;
  thumbnailUrl: string;
  imageUrl: string;
  sourceUrl?: string | null;
  dateCreated?: string | null;
  displayDate?: string | null;
  aspectRatio: string;
  urls: NasaImageUrls;
  timeline?: NasaImageTimeline | null;
  keywords: readonly string[];
  relevanceScore?: number | null;
  matchReasons?: readonly string[];
};

export type NasaSearchMode = "standard" | "semantic";

export type NasaSearchFilterKey = "dateFrom" | "dateTo" | "rover" | "camera" | "mission";

export type NasaAppliedSearchFilters = Record<NasaSearchFilterKey, string | null> & {
  inferred: readonly NasaSearchFilterKey[];
};

export type NasaRelaxationSuggestion = {
  code: "remove_date_range" | "remove_mission" | "remove_rover" | "remove_camera";
  filter: "dateRange" | "mission" | "rover" | "camera";
};

export type NasaSearchDegradationReason =
  | "ai_unavailable"
  | "ai_invalid_response"
  | "ai_timeout"
  | "partial_nasa_failure"
  | "cache_unavailable";

export type NasaSearchTelemetryEventName =
  | "search_submitted"
  | "search_completed"
  | "result_opened"
  | "suggestion_applied"
  | "search_error"
  | "search_session_expired";

export type NasaSearchTelemetryEvent = {
  eventName: NasaSearchTelemetryEventName;
  searchId?: string | null;
  resultId?: string | null;
  mode: NasaSearchMode;
  resultCount?: number | null;
  reason?: string | null;
};

export type NasaSearchRouteState = {
  q?: string;
  semantic?: true;
  datePreset?: "custom" | "last-30" | "last-year";
  dateFrom?: string;
  dateTo?: string;
  rover?: string;
  camera?: string;
  mission?: string;
  suppressInferred?: string;
};

export type NasaSearchFilters = {
  query: string;
  datePreset?: "any" | "custom" | "last-30" | "last-year";
  dateFrom?: string | null;
  dateTo?: string | null;
  rover?: string | null;
  camera?: string | null;
  mission?: string | null;
  page?: number;
  pageSize?: number;
  cursor?: string | null;
  locale?: "en" | "es";
  suppressInferred?: readonly NasaSearchFilterKey[];
};

export type NasaSearchResult = {
  images: readonly NasaImage[];
  totalHits: number;
  page: number;
  pageSize: number;
  searchId?: string | null;
  mode?: NasaSearchMode;
  interpretedQuery?: string | null;
  appliedFilters?: NasaAppliedSearchFilters | null;
  degraded?: boolean;
  degradationReason?: NasaSearchDegradationReason | null;
  nextCursor?: string | null;
  relaxationSuggestions?: readonly NasaRelaxationSuggestion[];
};
