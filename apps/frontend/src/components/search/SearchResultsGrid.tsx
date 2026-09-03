import { AlertTriangle, RotateCcw, SlidersHorizontal, Sparkles, X } from "lucide-react";
import { useEffect, useRef } from "react";
import { ApiError } from "@/api";
import { SearchBatchBar } from "@/components/search/SearchBatchBar";
import { SearchImageCard } from "@/components/search/SearchImageCard";
import { Button } from "@/components/ui/button";
import { getCurrentAppLanguage } from "@/lib/i18n";
import { cn } from "@/lib/utils";
import { m } from "@/paraglide/messages";
import { uiSelectors, useUiStore } from "@/store";
import type {
  NasaAppliedSearchFilters,
  NasaImage,
  NasaRelaxationSuggestion,
  NasaSearchDegradationReason,
  NasaSearchFilterKey,
  NasaSearchResult
} from "@/types/search";

type SearchResultsGridProps = {
  images: readonly NasaImage[];
  result?: NasaSearchResult;
  totalHits: number;
  isLoading: boolean;
  isFetching: boolean;
  isFetchingNextPage: boolean;
  isRecoveringCursor: boolean;
  hasNextPage: boolean;
  isUsingFallback: boolean;
  effectiveSemanticSearch: boolean;
  semanticSearchDisabled: boolean;
  error: Error | null;
  onImageOpened: (image: NasaImage) => void;
  onImagePreviewIntent: (image: NasaImage) => void;
  onLoadMore: () => void;
  onResetSearch: () => void;
  onRetry: () => void;
  onSemanticSearchChange: (enabled: boolean) => void;
  onRemoveAppliedFilter: (filter: NasaSearchFilterKey, inferred: boolean) => void;
  onApplyRelaxationSuggestion: (suggestion: NasaRelaxationSuggestion) => void;
};

const skeletonKeys = ["sk-1", "sk-2", "sk-3", "sk-4", "sk-5", "sk-6", "sk-7", "sk-8"];
const semanticDescriptionId = "semantic-search-description";

const getFilterLabel = (filter: NasaSearchFilterKey): string => {
  switch (filter) {
    case "dateFrom":
      return m.search_from();
    case "dateTo":
      return m.search_to();
    case "mission":
      return m.search_mission_label();
    case "rover":
      return m.search_rover_label();
    case "camera":
      return m.search_camera_label();
  }
};

const getDegradationReasonLabel = (reason?: NasaSearchDegradationReason | null): string => {
  switch (reason) {
    case "ai_unavailable":
      return m.search_degraded_ai_unavailable();
    case "ai_invalid_response":
      return m.search_degraded_ai_invalid_response();
    case "ai_timeout":
      return m.search_degraded_ai_timeout();
    case "partial_nasa_failure":
      return m.search_degraded_partial_nasa_failure();
    case "cache_unavailable":
      return m.search_degraded_cache_unavailable();
    default:
      return m.search_degraded_default();
  }
};

const getSuggestionLabel = (suggestion: NasaRelaxationSuggestion): string => {
  switch (suggestion.code) {
    case "remove_date_range":
      return m.search_suggestion_remove_date_range();
    case "remove_mission":
      return m.search_suggestion_remove_mission();
    case "remove_rover":
      return m.search_suggestion_remove_rover();
    case "remove_camera":
      return m.search_suggestion_remove_camera();
  }
};

const getErrorMessage = (error: Error): string =>
  error instanceof ApiError && error.status === 410
    ? m.search_cursor_expired()
    : m.search_error_description();

export function SearchResultsGrid({
  images,
  result,
  totalHits,
  isLoading,
  isFetching,
  isFetchingNextPage,
  isRecoveringCursor,
  hasNextPage,
  isUsingFallback,
  effectiveSemanticSearch,
  semanticSearchDisabled,
  error,
  onImageOpened,
  onImagePreviewIntent,
  onLoadMore,
  onResetSearch,
  onRetry,
  onSemanticSearchChange,
  onRemoveAppliedFilter,
  onApplyRelaxationSuggestion
}: SearchResultsGridProps) {
  const scrollRootRef = useRef<HTMLDivElement | null>(null);
  const loadMoreRef = useRef<HTMLDivElement | null>(null);
  const openMobileFilters = useUiStore(uiSelectors.openMobileFiltersAction);
  const showSkeletons = isLoading || (isFetching && images.length === 0);
  const hasNoResults = !showSkeletons && images.length === 0 && !isUsingFallback;
  const formatResultsCount = new Intl.NumberFormat(getCurrentAppLanguage() === "es" ? "es-CO" : "en-US");
  const statusMessage = isRecoveringCursor
    ? m.search_recovering_cursor()
    : isFetchingNextPage
      ? m.search_loading_more()
      : isLoading
        ? m.search_searching()
        : isUsingFallback
          ? m.search_demo_notice()
          : m.search_results_count({ count: formatResultsCount.format(totalHits) });

  const renderSearchImage = (image: NasaImage) => (
    <SearchImageCard
      key={image.nasaImageId}
      image={image}
      onOpen={onImageOpened}
      onPreviewIntent={onImagePreviewIntent}
    />
  );

  useEffect(() => {
    const scrollRoot = scrollRootRef.current;
    const loadMoreMarker = loadMoreRef.current;

    if (scrollRoot === null || loadMoreMarker === null || !hasNextPage || isFetchingNextPage || isLoading) {
      return;
    }

    const observer = new IntersectionObserver(
      (entries) => {
        if (entries[0]?.isIntersecting) {
          onLoadMore();
        }
      },
      {
        root: scrollRoot,
        rootMargin: "480px 0px"
      }
    );

    observer.observe(loadMoreMarker);

    return () => {
      observer.disconnect();
    };
  }, [hasNextPage, isFetchingNextPage, isLoading, onLoadMore]);

  return (
    <section
      className="relative min-h-0 overflow-hidden border-white/10 lg:border-r"
      aria-label={m.search_results_aria()}
      aria-busy={isLoading || isFetching}
    >
      <p className="sr-only" role="status" aria-live="polite" aria-atomic="true">
        {statusMessage}
      </p>
      <div className="flex h-full min-h-0 flex-col">
        <div className="flex min-h-16 shrink-0 flex-wrap items-center justify-between gap-3 border-b border-white/10 px-4 py-3">
          <div>
            <p className="text-sm font-semibold text-space-cyan">
              {isLoading
                ? m.search_searching()
                : isUsingFallback
                  ? m.search_demo_count({ count: formatResultsCount.format(images.length) })
                  : m.search_results_count({ count: formatResultsCount.format(totalHits) })}
            </p>
            <p className="text-xs text-muted-foreground">
              {isRecoveringCursor
                ? m.search_recovering_cursor()
                : isUsingFallback
                  ? m.search_demo_notice()
                  : isFetching
                    ? m.search_refreshing()
                    : m.search_ready()}
            </p>
          </div>
          <div className="flex items-center gap-2">
            <Button
              type="button"
              variant="ghost"
              size="sm"
              className={cn(
                "h-9 rounded-full border border-white/10 bg-space-panel px-2 text-xs text-muted-foreground hover:bg-white/5 hover:text-white sm:px-3",
                effectiveSemanticSearch && "border-space-cyan/40 bg-space-cyan/10 text-space-cyan"
              )}
              aria-label={m.search_semantic()}
              aria-describedby={semanticDescriptionId}
              aria-pressed={effectiveSemanticSearch}
              disabled={semanticSearchDisabled}
              data-cy="semantic-toggle"
              onClick={() => onSemanticSearchChange(!effectiveSemanticSearch)}
            >
              <Sparkles className="h-4 w-4" aria-hidden="true" />
              <span className="hidden sm:inline">{m.search_semantic()}</span>
            </Button>
            <span id={semanticDescriptionId} className="sr-only">
              {semanticSearchDisabled ? m.search_semantic_requires_query() : m.search_semantic_description()}
            </span>
            <Button
              type="button"
              variant="ghost"
              size="icon"
              className="h-9 w-9 rounded-full border border-white/10 bg-space-panel text-muted-foreground hover:bg-white/5 hover:text-white lg:hidden"
              aria-label={m.search_open_filters()}
              onClick={openMobileFilters}
            >
              <SlidersHorizontal className="h-4 w-4" />
            </Button>
          </div>
        </div>
        <div ref={scrollRootRef} className="cosmara-scrollbar min-h-0 flex-1 overflow-y-auto px-4 py-4">
          {error !== null ? (
            <div className="mb-4 flex flex-wrap items-center justify-between gap-3 rounded-lg border border-space-orange/30 bg-space-orange/10 px-4 py-3" role="alert">
              <div className="flex min-w-0 items-start gap-2">
                <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0 text-space-orange" aria-hidden="true" />
                <div>
                  <p className="text-sm font-semibold text-white">{m.search_error_title()}</p>
                  <p className="text-xs leading-5 text-muted-foreground">{getErrorMessage(error)}</p>
                </div>
              </div>
              <Button
                type="button"
                variant="outline"
                size="sm"
                className="border-space-orange/40 bg-transparent text-space-orange hover:bg-space-orange/10 hover:text-white"
                data-cy="search-retry"
                onClick={onRetry}
              >
                <RotateCcw className="h-3.5 w-3.5" aria-hidden="true" />
                {m.search_retry()}
              </Button>
            </div>
          ) : null}
          {effectiveSemanticSearch && result !== undefined ? (
            <SemanticInterpretation
              result={result}
              onRemoveAppliedFilter={onRemoveAppliedFilter}
              onApplyRelaxationSuggestion={onApplyRelaxationSuggestion}
            />
          ) : null}
          <div
            className={cn(
              hasNoResults
                ? "flex min-h-full items-center justify-center"
                : "grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-3 min-[1500px]:grid-cols-4"
            )}
          >
            {showSkeletons
              ? skeletonKeys.map((key) => <SearchImageSkeleton key={key} />)
              : hasNoResults
                ? <SearchEmptyState onReset={onResetSearch} />
                : images.map(renderSearchImage)}
          </div>
          {!hasNoResults && !isLoading && !isUsingFallback ? (
            <div ref={loadMoreRef} className="flex min-h-16 items-center justify-center text-xs text-muted-foreground">
              {isFetchingNextPage ? (
                <span>{m.search_loading_more()}</span>
              ) : hasNextPage ? (
                <Button type="button" variant="ghost" size="sm" onClick={onLoadMore}>
                  {m.search_load_more()}
                </Button>
              ) : (
                <span>{m.search_end_results()}</span>
              )}
            </div>
          ) : null}
        </div>
      </div>
      <SearchBatchBar images={images} />
    </section>
  );
}

type SemanticInterpretationProps = {
  result: NasaSearchResult;
  onRemoveAppliedFilter: (filter: NasaSearchFilterKey, inferred: boolean) => void;
  onApplyRelaxationSuggestion: (suggestion: NasaRelaxationSuggestion) => void;
};

function SemanticInterpretation({
  result,
  onRemoveAppliedFilter,
  onApplyRelaxationSuggestion
}: SemanticInterpretationProps) {
  const appliedFilters = result.appliedFilters;
  const appliedFilterEntries = appliedFilters === null || appliedFilters === undefined
    ? []
    : (Object.entries(appliedFilters) as [keyof NasaAppliedSearchFilters, unknown][])
        .filter((entry): entry is [NasaSearchFilterKey, string] => entry[0] !== "inferred" && typeof entry[1] === "string" && entry[1].length > 0);
  const inferredFilters = new Set(appliedFilters?.inferred ?? []);
  const suggestions = result.relaxationSuggestions ?? [];

  return (
    <section
      className="mb-4 rounded-lg border border-space-cyan/20 bg-space-cyan/5 p-4"
      aria-label={m.search_semantic_interpretation_label()}
      data-cy="semantic-interpretation"
    >
      <div className="flex items-start gap-3">
        <span className="mt-0.5 flex h-7 w-7 shrink-0 items-center justify-center rounded-full bg-space-cyan/10 text-space-cyan">
          <Sparkles className="h-4 w-4" aria-hidden="true" />
        </span>
        <div className="min-w-0 flex-1 space-y-3">
          <div>
            <p className="text-xs font-semibold uppercase tracking-wide text-space-cyan">
              {m.search_semantic_interpretation_label()}
            </p>
            {result.interpretedQuery ? (
              <p className="mt-1 text-sm leading-6 text-white">“{result.interpretedQuery}”</p>
            ) : (
              <p className="mt-1 text-sm leading-6 text-muted-foreground">{m.search_semantic_interpretation_unavailable()}</p>
            )}
          </div>
          {result.degraded ? (
            <div className="flex items-start gap-2 rounded-md border border-space-orange/25 bg-space-orange/10 px-3 py-2 text-xs leading-5 text-space-orange">
              <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" aria-hidden="true" />
              <p>{getDegradationReasonLabel(result.degradationReason)}</p>
            </div>
          ) : null}
          {appliedFilterEntries.length > 0 ? (
            <div>
              <p className="text-[11px] font-semibold uppercase tracking-wide text-muted-foreground">
                {m.search_applied_filters()}
              </p>
              <div className="mt-2 flex flex-wrap gap-2">
                {appliedFilterEntries.map(([filter, value]) => {
                  const inferred = inferredFilters.has(filter);
                  const label = `${getFilterLabel(filter)}: ${value}`;

                  return (
                    <button
                      key={filter}
                      type="button"
                      className={cn(
                        "inline-flex items-center gap-1 rounded-full border px-2.5 py-1 text-[11px] font-medium transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-space-cyan/70",
                        inferred
                          ? "border-space-cyan/30 bg-space-cyan/10 text-space-cyan hover:bg-space-cyan/20"
                          : "border-white/15 bg-white/5 text-muted-foreground hover:border-white/30 hover:text-white"
                      )}
                      aria-label={m.search_remove_applied_filter({ filter: label })}
                      data-cy={inferred ? "semantic-inferred-filter" : "semantic-applied-filter"}
                      data-filter={filter}
                      onClick={() => onRemoveAppliedFilter(filter, inferred)}
                    >
                      <span>{label}</span>
                      {inferred ? <span className="text-[9px] uppercase">{m.search_inferred()}</span> : null}
                      <X className="h-3 w-3" aria-hidden="true" />
                    </button>
                  );
                })}
              </div>
            </div>
          ) : null}
          {suggestions.length > 0 ? (
            <div>
              <p className="text-[11px] font-semibold uppercase tracking-wide text-muted-foreground">
                {m.search_relaxation_suggestions()}
              </p>
              <div className="mt-2 flex flex-wrap gap-2">
                {suggestions.map((suggestion) => (
                  <Button
                    key={suggestion.code}
                    type="button"
                    variant="outline"
                    size="sm"
                    className="h-8 border-space-cyan/25 bg-transparent px-3 text-xs text-space-cyan hover:bg-space-cyan/10 hover:text-white"
                    data-cy="semantic-suggestion"
                    data-suggestion={suggestion.code}
                    onClick={() => onApplyRelaxationSuggestion(suggestion)}
                  >
                    {getSuggestionLabel(suggestion)}
                  </Button>
                ))}
              </div>
            </div>
          ) : null}
        </div>
      </div>
    </section>
  );
}

type SearchEmptyStateProps = {
  onReset: () => void;
};

function SearchEmptyState({ onReset }: SearchEmptyStateProps) {
  return (
    <div className="max-w-sm rounded-lg border border-white/10 bg-space-panel px-6 py-8 text-center shadow-sm shadow-black/20">
      <p className="text-sm font-semibold text-white">{m.search_empty_title()}</p>
      <p className="mt-2 text-sm leading-6 text-muted-foreground">{m.search_empty_description()}</p>
      <Button
        type="button"
        size="sm"
        className="mt-5 h-9 rounded-full bg-space-orange px-5 text-space-void hover:bg-space-orange/90"
        onClick={onReset}
      >
        {m.search_reset()}
      </Button>
    </div>
  );
}

function SearchImageSkeleton() {
  return (
    <div className="overflow-hidden rounded-lg border border-white/10 bg-space-panel shadow-sm shadow-black/20" aria-hidden="true">
      <div className="aspect-[4/3] animate-pulse bg-white/10" />
      <div className="space-y-3 p-3">
        <div className="h-4 w-3/4 rounded bg-white/10" />
        <div className="h-3 w-1/3 rounded bg-white/10" />
        <div className="flex items-center justify-between">
          <div className="h-6 w-20 rounded bg-space-cyan/10" />
          <div className="h-7 w-24 rounded bg-white/10" />
        </div>
      </div>
    </div>
  );
}
