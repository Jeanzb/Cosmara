import { keepPreviousData, useInfiniteQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
import { ApiError } from "@/api";
import { defaultNasaSearchPageSize, queryKeys } from "@/constants";
import { preloadImageUrls } from "@/lib/imagePreload";
import { selectNasaImageCardUrl, selectNasaImagePreviewUrl } from "@/lib/nasaImageAssets";
import { SearchService } from "@/services/search";
import type {
  NasaImage,
  NasaRelaxationSuggestion,
  NasaSearchFilters,
  NasaSearchMode,
  NasaSearchResult,
  NasaSearchTelemetryEvent
} from "@/types/search";

type UseNasaSearchOptions = {
  filters: NasaSearchFilters;
  semanticSearch: boolean;
  enabled?: boolean;
};

type SearchPageParam = string | number | null;

const nasaGeneralSearchPageLimit = 100;

const createSearchRunId = (): string => `${Date.now()}-${Math.random().toString(36).slice(2)}`;

const hasFilterValue = (value?: string | null): boolean => (value ?? "").trim().length > 0;

const isGeneralImageSearch = (filters: NasaSearchFilters): boolean =>
  !hasFilterValue(filters.query)
  && !hasFilterValue(filters.dateFrom)
  && !hasFilterValue(filters.dateTo)
  && !hasFilterValue(filters.rover)
  && !hasFilterValue(filters.camera)
  && !hasFilterValue(filters.mission);

const isExpiredCursorError = (error: unknown): error is ApiError => error instanceof ApiError && error.status === 410;

const createRequestIdentity = (filters: NasaSearchFilters, semanticSearch: boolean): string =>
  JSON.stringify([
    semanticSearch,
    filters.query,
    filters.dateFrom,
    filters.dateTo,
    filters.rover,
    filters.camera,
    filters.mission,
    filters.pageSize,
    filters.locale,
    filters.suppressInferred
  ]);

const deduplicateImages = (pages: readonly NasaSearchResult[]): readonly NasaImage[] => {
  const imagesById = new Map<string, NasaImage>();

  pages.forEach((page) => {
    page.images.forEach((image) => {
      imagesById.set(image.nasaImageId, image);
    });
  });

  return [...imagesById.values()];
};

export const useNasaSearch = ({ filters, semanticSearch, enabled = true }: UseNasaSearchOptions) => {
  const queryClient = useQueryClient();
  const [generalSearchRunId] = useState(createSearchRunId);
  const [requestVersion, setRequestVersion] = useState(0);
  const activeSearchAbortControllerRef = useRef<AbortController | null>(null);
  const cursorRecoveryIdentityRef = useRef<string | null>(null);
  const telemetryRunRef = useRef({
    requestKey: "",
    eventKeys: new Set<string>()
  });
  const baseFilters = useMemo<NasaSearchFilters>(
    () => ({
      ...filters,
      cursor: undefined,
      page: undefined
    }),
    [filters]
  );
  const effectiveSemanticSearch = semanticSearch && hasFilterValue(baseFilters.query);
  const shouldRandomizeGeneralSearch = !effectiveSemanticSearch && isGeneralImageSearch(baseFilters);
  const requestIdentity = createRequestIdentity(baseFilters, effectiveSemanticSearch);
  const { mutate: recordTelemetryMutation } = useMutation({
    mutationFn: SearchService.recordEvent,
    retry: false
  });
  const mode: NasaSearchMode = effectiveSemanticSearch ? "semantic" : "standard";
  const telemetryRequestKey = `${requestIdentity}:${requestVersion}`;
  const searchQueryKey = useMemo(
    () => queryKeys.search.results(
      baseFilters,
      effectiveSemanticSearch,
      shouldRandomizeGeneralSearch ? generalSearchRunId : undefined,
      requestVersion
    ),
    [baseFilters, effectiveSemanticSearch, generalSearchRunId, requestVersion, shouldRandomizeGeneralSearch]
  );

  useLayoutEffect(() => {
    if (telemetryRunRef.current.requestKey !== telemetryRequestKey) {
      telemetryRunRef.current = {
        requestKey: telemetryRequestKey,
        eventKeys: new Set<string>()
      };
    }
  }, [telemetryRequestKey]);

  useEffect(() => () => {
    void queryClient.cancelQueries({ queryKey: searchQueryKey, exact: true });
  }, [queryClient, searchQueryKey]);

  useEffect(() => () => {
    activeSearchAbortControllerRef.current?.abort();
    activeSearchAbortControllerRef.current = null;
  }, []);

  const recordEvent = useCallback(
    (event: NasaSearchTelemetryEvent): void => {
      recordTelemetryMutation(event);
    },
    [recordTelemetryMutation]
  );
  const recordEventOnce = useCallback(
    (requestKey: string, eventKey: string, event: NasaSearchTelemetryEvent): void => {
      if (telemetryRunRef.current.requestKey !== requestKey || telemetryRunRef.current.eventKeys.has(eventKey)) {
        return;
      }

      telemetryRunRef.current.eventKeys.add(eventKey);
      recordEvent(event);
    },
    [recordEvent]
  );

  const searchQuery = useInfiniteQuery({
    queryKey: searchQueryKey,
    queryFn: async ({ pageParam, signal }) => {
      signal.throwIfAborted();
      const requestAbortController = new AbortController();
      const abortRequest = (): void => requestAbortController.abort();

      activeSearchAbortControllerRef.current?.abort();
      activeSearchAbortControllerRef.current = requestAbortController;
      signal.addEventListener("abort", abortRequest, { once: true });

      const isFirstPage = pageParam === null;
      const pageFilters: NasaSearchFilters = {
        ...baseFilters,
        cursor: typeof pageParam === "string" ? pageParam : undefined,
        page: typeof pageParam === "number" ? pageParam : pageParam === null ? 1 : undefined
      };
      const requestOptions = { signal: requestAbortController.signal };

      if (isFirstPage) {
        recordEventOnce(
          telemetryRequestKey,
          "submitted",
          { eventName: "search_submitted", mode }
        );
      }

      try {
        let response: NasaSearchResult;

        if (shouldRandomizeGeneralSearch && isFirstPage) {
          const firstPage = await SearchService.searchImages(pageFilters, requestOptions);
          const pageSize = firstPage.pageSize || baseFilters.pageSize || defaultNasaSearchPageSize;
          const maxPages = Math.max(1, Math.ceil(firstPage.totalHits / pageSize));
          const randomPage = Math.floor(Math.random() * Math.min(maxPages, nasaGeneralSearchPageLimit)) + 1;

          response = randomPage === 1
            ? firstPage
            : await SearchService.searchImages({
              ...baseFilters,
              page: randomPage
            }, requestOptions);
        } else {
          response = effectiveSemanticSearch
            ? await SearchService.semanticSearchImages(pageFilters, requestOptions)
            : await SearchService.searchImages(pageFilters, requestOptions);
        }

        if (isFirstPage) {
          recordEventOnce(
            telemetryRequestKey,
            "completed",
            {
              eventName: "search_completed",
              searchId: response.searchId,
              mode: response.mode ?? mode,
              resultCount: response.totalHits
            }
          );
        }

        return response;
      } catch (error) {
        if (isExpiredCursorError(error)) {
          recordEventOnce(
            telemetryRequestKey,
            "session_expired",
            {
              eventName: "search_session_expired",
              mode,
              reason: "search_session_expired"
            }
          );
        }

        throw error;
      } finally {
        signal.removeEventListener("abort", abortRequest);

        if (activeSearchAbortControllerRef.current === requestAbortController) {
          activeSearchAbortControllerRef.current = null;
        }
      }
    },
    initialPageParam: null as SearchPageParam,
    getNextPageParam: (lastPage, pages): SearchPageParam | undefined => {
      if (typeof lastPage.nextCursor === "string") {
        return lastPage.nextCursor;
      }

      if (lastPage.mode === "semantic") {
        return undefined;
      }

      const loadedImagesCount = pages.reduce((count, page) => count + page.images.length, 0);
      const pageSize = lastPage.pageSize || baseFilters.pageSize || defaultNasaSearchPageSize;

      if (lastPage.images.length < pageSize || loadedImagesCount >= lastPage.totalHits) {
        return undefined;
      }

      return lastPage.page + 1;
    },
    enabled,
    placeholderData: keepPreviousData,
    retry: (failureCount, error) => !isExpiredCursorError(error) && failureCount < 1
  });
  const pages = searchQuery.data?.pages;
  const images = useMemo<readonly NasaImage[]>(() => deduplicateImages(pages ?? []), [pages]);
  const result = useMemo<NasaSearchResult | undefined>(() => {
    const firstPage = pages?.[0];
    const lastPage = pages?.[pages.length - 1];

    if (firstPage === undefined || lastPage === undefined) {
      return undefined;
    }

    const degradedPage = pages === undefined
      ? undefined
      : [...pages].reverse().find((page) => page.degraded === true);

    return {
      ...firstPage,
      images,
      page: lastPage.page,
      nextCursor: lastPage.nextCursor,
      totalHits: firstPage.totalHits,
      pageSize: firstPage.pageSize,
      degraded: degradedPage !== undefined,
      degradationReason: degradedPage?.degradationReason ?? firstPage.degradationReason
    };
  }, [images, pages]);
  const canRecoverExpiredCursor = isExpiredCursorError(searchQuery.error)
    && cursorRecoveryIdentityRef.current !== requestIdentity;
  const isRecoveringCursor = cursorRecoveryIdentityRef.current === requestIdentity
    && searchQuery.isFetching
    && searchQuery.isPlaceholderData;

  useEffect(() => {
    if (!canRecoverExpiredCursor) {
      return;
    }

    cursorRecoveryIdentityRef.current = requestIdentity;
    setRequestVersion((currentVersion) => currentVersion + 1);
  }, [canRecoverExpiredCursor, requestIdentity]);

  useEffect(() => {
    if (searchQuery.error === null || isExpiredCursorError(searchQuery.error)) {
      return;
    }

    recordEventOnce(
      telemetryRequestKey,
      "error",
      {
        eventName: "search_error",
        mode,
        reason: "request_failed"
      }
    );
  }, [mode, recordEventOnce, searchQuery.error, telemetryRequestKey]);

  useEffect(() => {
    if (images.length === 0) {
      return;
    }

    preloadImageUrls(images.slice(0, 8).map(selectNasaImageCardUrl));
  }, [images]);

  const prefetchImages = useCallback((nextImages: readonly NasaImage[]): void => {
    preloadImageUrls(nextImages.map(selectNasaImagePreviewUrl));
  }, []);

  const retry = useCallback((): void => {
    cursorRecoveryIdentityRef.current = null;
    setRequestVersion((currentVersion) => currentVersion + 1);
  }, []);

  const trackResultOpened = useCallback(
    (image: NasaImage): void => {
      recordEvent({
        eventName: "result_opened",
        searchId: result?.searchId,
        resultId: image.nasaImageId,
        mode: result?.mode ?? mode
      });
    },
    [mode, recordEvent, result?.mode, result?.searchId]
  );

  const trackSuggestionApplied = useCallback(
    (suggestion: NasaRelaxationSuggestion): void => {
      recordEvent({
        eventName: "suggestion_applied",
        searchId: result?.searchId,
        mode: result?.mode ?? mode,
        reason: suggestion.code
      });
    },
    [mode, recordEvent, result?.mode, result?.searchId]
  );

  return {
    filters: baseFilters,
    images,
    result,
    effectiveSemanticSearch,
    isLoading: searchQuery.isLoading || isRecoveringCursor,
    isFetching: searchQuery.isFetching,
    isFetchingNextPage: searchQuery.isFetchingNextPage,
    isPlaceholderData: searchQuery.isPlaceholderData,
    isRecoveringCursor,
    hasNextPage: searchQuery.hasNextPage,
    error: canRecoverExpiredCursor || isRecoveringCursor ? null : searchQuery.error,
    retry,
    trackResultOpened,
    trackSuggestionApplied,
    fetchNextPage: searchQuery.fetchNextPage,
    prefetchImages
  };
};
