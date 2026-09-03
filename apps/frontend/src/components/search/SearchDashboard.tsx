import { useNavigate } from "@tanstack/react-router";
import { useCallback, useEffect, useMemo } from "react";
import { SearchFiltersPanel } from "@/components/search/SearchFiltersPanel";
import { SearchInspector } from "@/components/search/SearchInspector";
import { SearchResultsGrid } from "@/components/search/SearchResultsGrid";
import { SearchTimeline } from "@/components/search/SearchTimeline";
import { defaultNasaSearchPageSize, sampleSearchImages } from "@/constants";
import { useNasaSearch } from "@/hooks";
import { getCurrentAppLanguage } from "@/lib/i18n";
import {
  parseSuppressedInferences,
  serializeSuppressedInferences,
  toSearchFilters,
  toSearchRouteState
} from "@/lib/searchRoute";
import { formatTimelineRange } from "@/lib/searchTimeline";
import { cn } from "@/lib/utils";
import { m } from "@/paraglide/messages";
import { uiSelectors, useUiStore } from "@/store";
import type {
  NasaImage,
  NasaRelaxationSuggestion,
  NasaSearchFilterKey,
  NasaSearchFilters,
  NasaSearchRouteState
} from "@/types/search";

type SearchDashboardProps = {
  searchState: NasaSearchRouteState;
};

const hasQuery = (filters: NasaSearchFilters): boolean => filters.query.trim().length > 0;

export function SearchDashboard({ searchState }: SearchDashboardProps) {
  const navigate = useNavigate();
  const language = getCurrentAppLanguage();
  const filters = useMemo(
    () => toSearchFilters(searchState, defaultNasaSearchPageSize, language),
    [language, searchState]
  );
  const semanticSearchRequested = searchState.semantic === true;
  const selectedImage = useUiStore(uiSelectors.selectedImage);
  const inspectorOpen = useUiStore(uiSelectors.inspectorOpen);
  const clearSelectedImage = useUiStore(uiSelectors.clearSelectedImageAction);
  const {
    effectiveSemanticSearch,
    error,
    fetchNextPage,
    hasNextPage,
    images,
    isFetching,
    isFetchingNextPage,
    isLoading,
    isPlaceholderData,
    isRecoveringCursor,
    prefetchImages,
    result,
    retry,
    trackResultOpened,
    trackSuggestionApplied
  } = useNasaSearch({
    filters,
    semanticSearch: semanticSearchRequested
  });
  const isUsingFallback = error !== null && result === undefined && !isLoading;
  const displayImages = isUsingFallback ? sampleSearchImages : images;
  const fallbackImage = displayImages[0];
  const totalHits = isUsingFallback ? sampleSearchImages.length : result?.totalHits ?? 0;
  const showInspectorColumn = inspectorOpen && fallbackImage !== undefined;
  const timelineDateRange = formatTimelineRange(filters, displayImages);
  const visibleResult = isPlaceholderData ? undefined : result;

  useEffect(() => {
    if (displayImages.length === 0) {
      if (selectedImage !== null) {
        clearSelectedImage();
      }

      return;
    }

    const selectedImageStillVisible =
      selectedImage !== null && displayImages.some((image) => image.nasaImageId === selectedImage.nasaImageId);

    if (selectedImage !== null && !selectedImageStillVisible) {
      clearSelectedImage();
    }
  }, [clearSelectedImage, displayImages, selectedImage]);

  const navigateToSearch = useCallback(
    (nextState: NasaSearchRouteState) => {
      void navigate({ to: "/search", search: nextState });
    },
    [navigate]
  );

  const applyFilters = useCallback(
    (nextFilters: Partial<NasaSearchFilters>) => {
      const query = nextFilters.query?.trim() ?? "";
      const queryChanged = query !== filters.query;
      navigateToSearch(toSearchRouteState({
        ...nextFilters,
        suppressInferred: queryChanged ? [] : filters.suppressInferred
      }, semanticSearchRequested && query.length > 0));
    },
    [filters.query, filters.suppressInferred, navigateToSearch, semanticSearchRequested]
  );

  const toggleSemanticSearch = useCallback(
    (enabled: boolean) => {
      if (!hasQuery(filters)) {
        return;
      }

      navigateToSearch(toSearchRouteState(filters, enabled));
    },
    [filters, navigateToSearch]
  );

  const removeAppliedFilter = useCallback(
    (filter: NasaSearchFilterKey, inferred: boolean) => {
      const suppressedInferences = parseSuppressedInferences(searchState.suppressInferred);
      const removesDateBoundary = filter === "dateFrom" || filter === "dateTo";

      navigateToSearch({
        ...searchState,
        [filter]: undefined,
        datePreset: removesDateBoundary ? undefined : searchState.datePreset,
        suppressInferred: inferred
          ? serializeSuppressedInferences([...suppressedInferences, filter])
          : searchState.suppressInferred
      });
    },
    [navigateToSearch, searchState]
  );

  const applyRelaxationSuggestion = useCallback(
    (suggestion: NasaRelaxationSuggestion) => {
      trackSuggestionApplied(suggestion);
      const suppressedInferences = parseSuppressedInferences(searchState.suppressInferred);

      if (suggestion.filter === "dateRange") {
        navigateToSearch({
          ...searchState,
          datePreset: undefined,
          dateFrom: undefined,
          dateTo: undefined,
          suppressInferred: serializeSuppressedInferences([...suppressedInferences, "dateFrom", "dateTo"])
        });
        return;
      }

      const filter = suggestion.filter;
      navigateToSearch({
        ...searchState,
        [filter]: undefined,
        suppressInferred: serializeSuppressedInferences([
          ...suppressedInferences,
          filter
        ])
      });
    },
    [navigateToSearch, searchState, trackSuggestionApplied]
  );

  const prefetchPreviewImage = useCallback(
    (image: NasaImage) => {
      prefetchImages([image]);
    },
    [prefetchImages]
  );

  const loadNextPage = useCallback(() => {
    if (hasNextPage && !isFetchingNextPage) {
      void fetchNextPage();
    }
  }, [fetchNextPage, hasNextPage, isFetchingNextPage]);

  const resetSearch = useCallback(() => {
    navigateToSearch({});
  }, [navigateToSearch]);

  return (
    <section
      className={cn(
        "grid h-[calc(100vh-3.5rem)] grid-cols-1 grid-rows-[minmax(0,1fr)] overflow-hidden transition-[grid-template-columns] duration-300 ease-out lg:grid-rows-[minmax(0,1fr)_auto]",
        showInspectorColumn
          ? "lg:grid-cols-[260px_minmax(0,1fr)_380px]"
          : "lg:grid-cols-[260px_minmax(0,1fr)_0px]"
      )}
    >
      <h1 className="sr-only">{m.search_page_title()}</h1>
      <SearchFiltersPanel filters={filters} isFetching={isFetching} onApplyFilters={applyFilters} />
      <SearchResultsGrid
        images={displayImages}
        result={visibleResult}
        totalHits={totalHits}
        isLoading={isLoading}
        isFetching={isFetching}
        isFetchingNextPage={isFetchingNextPage}
        isRecoveringCursor={isRecoveringCursor}
        hasNextPage={hasNextPage}
        isUsingFallback={isUsingFallback}
        effectiveSemanticSearch={effectiveSemanticSearch}
        semanticSearchDisabled={!hasQuery(filters)}
        error={error}
        onImageOpened={trackResultOpened}
        onImagePreviewIntent={prefetchPreviewImage}
        onLoadMore={loadNextPage}
        onResetSearch={resetSearch}
        onRetry={retry}
        onSemanticSearchChange={toggleSemanticSearch}
        onRemoveAppliedFilter={removeAppliedFilter}
        onApplyRelaxationSuggestion={applyRelaxationSuggestion}
      />
      <SearchInspector fallbackImage={fallbackImage} />
      <SearchTimeline images={displayImages} dateRangeLabel={timelineDateRange} />
    </section>
  );
}
