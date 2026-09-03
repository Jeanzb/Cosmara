import { apiClient, type ApiQueryParams } from "@/api";
import type { NasaSearchFilters, NasaSearchResult, NasaSearchTelemetryEvent } from "@/types/search";

type SearchRequestOptions = {
  signal?: AbortSignal;
};

export class SearchService {
  public static searchImages(filters: NasaSearchFilters, options: SearchRequestOptions = {}): Promise<NasaSearchResult> {
    return this.executeSearch("/api/search", filters, options);
  }

  public static semanticSearchImages(filters: NasaSearchFilters, options: SearchRequestOptions = {}): Promise<NasaSearchResult> {
    return this.executeSearch("/api/search/semantic", filters, options);
  }

  public static recordEvent(event: NasaSearchTelemetryEvent): Promise<void> {
    return apiClient.post<void, NasaSearchTelemetryEvent>("/api/search/events", event, {
      authenticated: false,
      responseType: "void"
    });
  }

  private static executeSearch(
    path: "/api/search" | "/api/search/semantic",
    filters: NasaSearchFilters,
    options: SearchRequestOptions
  ): Promise<NasaSearchResult> {
    return apiClient.get<NasaSearchResult>(path, {
      query: this.toQueryParams(filters),
      authenticated: false,
      signal: options.signal
    });
  }

  private static toQueryParams(filters: NasaSearchFilters): ApiQueryParams {
    if ((filters.cursor ?? "").trim().length > 0) {
      return {
        cursor: filters.cursor,
        pageSize: filters.pageSize
      };
    }

    return {
      q: filters.query,
      dateFrom: filters.dateFrom,
      dateTo: filters.dateTo,
      rover: filters.rover,
      camera: filters.camera,
      mission: filters.mission,
      page: filters.page,
      pageSize: filters.pageSize,
      locale: filters.locale,
      suppressInferred: filters.suppressInferred?.join(",")
    };
  }
}
