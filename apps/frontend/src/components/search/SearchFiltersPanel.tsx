import { zodResolver } from "@hookform/resolvers/zod";
import { Info, Search, SlidersHorizontal, X } from "lucide-react";
import { useEffect, useMemo, useState, type ReactNode } from "react";
import { useForm, useWatch } from "react-hook-form";
import { z } from "zod";
import { DateRangeFilter } from "@/components/search/DateRangeFilter";
import { Button } from "@/components/ui/button";
import { Form, FormControl, FormField, FormItem, FormLabel, FormMessage } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { ScrollArea } from "@/components/ui/scroll-area";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { defaultNasaSearchQuery } from "@/constants";
import { getCurrentAppLanguage } from "@/lib/i18n";
import { cn } from "@/lib/utils";
import { m } from "@/paraglide/messages";
import { uiSelectors, useUiStore } from "@/store";
import type { NasaSearchFilters } from "@/types/search";

type SearchFiltersPanelProps = {
  filters: NasaSearchFilters;
  isFetching: boolean;
  onApplyFilters: (filters: Partial<NasaSearchFilters>) => void;
};

type SuggestedSearch = {
  label: string;
  filters: Partial<NasaSearchFilters>;
};

type FilterOption = {
  label: string;
  value: string;
};

const defaultDraft: SearchFilterFormValues = {
  query: defaultNasaSearchQuery,
  datePreset: "any",
  dateFrom: "",
  dateTo: "",
  mission: "",
  rover: "",
  camera: ""
};

const emptySelectValue = "__all__";

const createSearchFilterSchema = () => z.object({
  query: z.string().trim().max(160, m.search_validation_query_length()),
  datePreset: z.enum(["any", "custom", "last-30", "last-year"]),
  dateFrom: z.string(),
  dateTo: z.string(),
  mission: z.string(),
  rover: z.string(),
  camera: z.string()
}).superRefine((values, context) => {
  if (values.dateFrom.length > 0 && values.dateTo.length > 0 && values.dateFrom > values.dateTo) {
    context.addIssue({
      code: "custom",
      message: m.search_validation_date_range(),
      path: ["dateTo"]
    });
  }
});

type SearchFilterFormValues = z.infer<ReturnType<typeof createSearchFilterSchema>>;

const getSuggestedSearchPool = (): readonly SuggestedSearch[] => [
  {
    label: m.search_suggestion_mars_curiosity(),
    filters: {
      query: "curiosity mars",
      datePreset: "custom",
      dateFrom: "2024-01-01",
      dateTo: "2024-12-31",
      rover: "Curiosity"
    }
  },
  { label: m.search_suggestion_jwst_nebulae(), filters: { query: "jwst nebula", mission: "JWST" } },
  { label: m.search_suggestion_lunar_surface(), filters: { query: "lunar surface", mission: "Apollo" } },
  { label: m.search_suggestion_earth_night(), filters: { query: "earth at night" } },
  { label: m.search_suggestion_apollo_moonwalk(), filters: { query: "apollo moonwalk", mission: "Apollo" } },
  { label: m.search_suggestion_saturn_rings(), filters: { query: "saturn rings", mission: "Cassini" } },
  { label: m.search_suggestion_solar_flares(), filters: { query: "solar flare" } },
  { label: m.search_suggestion_space_station(), filters: { query: "international space station" } },
  { label: m.search_suggestion_jupiter_storms(), filters: { query: "jupiter storm", mission: "Juno" } },
  { label: m.search_suggestion_hubble_deep_field(), filters: { query: "hubble deep field", mission: "Hubble" } }
];

const suggestedSearchCount = 4;

const getMissionOptions = (): readonly FilterOption[] => [
  { label: m.search_all_missions(), value: "" },
  { label: "JWST", value: "JWST" },
  { label: "Hubble", value: "Hubble" },
  { label: "Apollo", value: "Apollo" },
  { label: "Cassini", value: "Cassini" },
  { label: "Juno", value: "Juno" }
];

const getRoverOptions = (): readonly FilterOption[] => [
  { label: m.search_all_rovers(), value: "" },
  { label: "Curiosity", value: "Curiosity" },
  { label: "Perseverance", value: "Perseverance" },
  { label: "Opportunity", value: "Opportunity" },
  { label: "Spirit", value: "Spirit" }
];

const getCameraOptions = (): readonly FilterOption[] => [
  { label: m.search_all_cameras(), value: "" },
  { label: "NIRCam", value: "NIRCam" },
  { label: "Mastcam", value: "Mastcam" },
  { label: "JunoCam", value: "JunoCam" },
  { label: "WFC3", value: "WFC3" }
];

const toDraft = (filters: Partial<NasaSearchFilters>): SearchFilterFormValues => ({
  query: filters.query ?? defaultDraft.query,
  datePreset: filters.datePreset ?? (filters.dateFrom !== undefined || filters.dateTo !== undefined ? "custom" : "any"),
  dateFrom: filters.dateFrom ?? "",
  dateTo: filters.dateTo ?? "",
  mission: filters.mission ?? "",
  rover: filters.rover ?? "",
  camera: filters.camera ?? ""
});

const toNullable = (value: string): string | null => {
  const trimmedValue = value.trim();

  return trimmedValue.length > 0 ? trimmedValue : null;
};

const formatDateInput = (date: Date): string => date.toISOString().slice(0, 10);

const pickSuggestedSearches = (): readonly SuggestedSearch[] => {
  const pool = [...getSuggestedSearchPool()];

  for (let index = pool.length - 1; index > 0; index -= 1) {
    const swapIndex = Math.floor(Math.random() * (index + 1));
    [pool[index], pool[swapIndex]] = [pool[swapIndex], pool[index]];
  }

  return pool.slice(0, suggestedSearchCount);
};

export function SearchFiltersPanel({ filters, isFetching, onApplyFilters }: SearchFiltersPanelProps) {
  const [suggestedSearches] = useState(pickSuggestedSearches);
  const mobileFiltersOpen = useUiStore(uiSelectors.mobileFiltersOpen);
  const closeMobileFilters = useUiStore(uiSelectors.closeMobileFiltersAction);
  const language = getCurrentAppLanguage();
  const searchFilterSchema = useMemo(createSearchFilterSchema, [language]);
  const form = useForm<SearchFilterFormValues>({
    resolver: zodResolver(searchFilterSchema),
    defaultValues: toDraft(filters)
  });
  const draft = useWatch({ control: form.control });
  const resolvedDraft: SearchFilterFormValues = { ...defaultDraft, ...draft };

  useEffect(() => {
    form.reset(toDraft(filters));
  }, [filters, form.reset]);

  const updateDateDraftField =
    (field: "dateFrom" | "dateTo") =>
    (value: string) => {
      form.setValue("datePreset", "custom", { shouldDirty: true });
      form.setValue(field, value, { shouldDirty: true, shouldValidate: form.formState.isSubmitted });
    };

  const updateDatePreset = (datePreset: string) => {
    const nextDatePreset = datePreset as SearchFilterFormValues["datePreset"];
    form.setValue("datePreset", nextDatePreset, { shouldDirty: true });

    if (nextDatePreset === "any") {
      form.setValue("dateFrom", "", { shouldDirty: true, shouldValidate: form.formState.isSubmitted });
      form.setValue("dateTo", "", { shouldDirty: true, shouldValidate: form.formState.isSubmitted });
      return;
    }

    if (nextDatePreset === "custom") {
      return;
    }

    const dateTo = new Date();
    const dateFrom = new Date(dateTo);

    if (nextDatePreset === "last-30") {
      dateFrom.setDate(dateTo.getDate() - 30);
    }

    if (nextDatePreset === "last-year") {
      dateFrom.setFullYear(dateTo.getFullYear() - 1);
    }

    form.setValue("dateFrom", formatDateInput(dateFrom), { shouldDirty: true, shouldValidate: form.formState.isSubmitted });
    form.setValue("dateTo", formatDateInput(dateTo), { shouldDirty: true, shouldValidate: form.formState.isSubmitted });
  };

  const applyDraft = (nextDraft: SearchFilterFormValues) => {
    onApplyFilters({
      query: nextDraft.query.trim(),
      datePreset: nextDraft.datePreset,
      dateFrom: toNullable(nextDraft.dateFrom),
      dateTo: toNullable(nextDraft.dateTo),
      mission: toNullable(nextDraft.mission),
      rover: toNullable(nextDraft.rover),
      camera: toNullable(nextDraft.camera)
    });
    closeMobileFilters();
  };

  const clearFilters = () => {
    form.reset(defaultDraft);
    applyDraft(defaultDraft);
  };

  const applySuggestedSearch = (suggestedSearch: SuggestedSearch) => {
    const nextDraft = { ...defaultDraft, ...toDraft(suggestedSearch.filters) };

    form.reset(nextDraft);
    applyDraft(nextDraft);
  };

  return (
    <>
      {mobileFiltersOpen ? (
        <div className="cosmara-mobile-overlay lg:hidden" aria-hidden="true" onClick={closeMobileFilters} />
      ) : null}
      <aside
        className={cn(
          "min-h-0 border-r border-white/10 bg-space-shell/70 lg:block",
          mobileFiltersOpen ? "cosmara-mobile-drawer" : "hidden"
        )}
        aria-label={m.search_filters_aria()}
      >
        <Form {...form}>
          <form className="flex h-full min-h-0 flex-col" noValidate onSubmit={form.handleSubmit(applyDraft)}>
            <div className="flex items-center justify-between border-b border-white/10 px-3 py-4">
              <p className="text-xs font-semibold uppercase text-white">{m.search_filters_label()}</p>
              <div className="flex items-center gap-3">
                <button type="button" className="text-xs font-medium text-space-cyan hover:text-white" onClick={clearFilters}>
                  {m.search_clear_all()}
                </button>
                <Button
                  type="button"
                  variant="ghost"
                  size="icon"
                  className="h-8 w-8 rounded-md text-muted-foreground hover:bg-white/5 hover:text-white lg:hidden"
                  aria-label={m.search_close_filters()}
                  onClick={closeMobileFilters}
                >
                  <X className="h-4 w-4" />
                </Button>
              </div>
            </div>
            <ScrollArea className="min-h-0 flex-1">
              <div className="space-y-5 px-3 py-4 pr-4">
                <FormField
                  control={form.control}
                  name="query"
                  render={({ field }) => (
                    <FormItem>
                      <FilterGroup label={m.search_query_label()}>
                        <FormLabel className="sr-only">{m.search_aria()}</FormLabel>
                        <div className="relative">
                          <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
                          <FormControl>
                            <Input
                              {...field}
                              type="search"
                              className="cosmara-control pl-9"
                              placeholder={m.search_placeholder()}
                              data-cy="search-input"
                            />
                          </FormControl>
                        </div>
                        <FormMessage />
                      </FilterGroup>
                    </FormItem>
                  )}
                />
                <FormField
                  control={form.control}
                  name="dateTo"
                  render={() => (
                    <FormItem>
                      <FilterGroup label={m.search_date_range_label()} hasInfo>
                        <DateRangeFilter
                          preset={resolvedDraft.datePreset}
                          dateFrom={resolvedDraft.dateFrom}
                          dateTo={resolvedDraft.dateTo}
                          onPresetChange={updateDatePreset}
                          onDateChange={updateDateDraftField}
                        />
                        <FormMessage />
                      </FilterGroup>
                    </FormItem>
                  )}
                />
                <FormField
                  control={form.control}
                  name="mission"
                  render={({ field }) => (
                    <FormItem>
                      <FilterGroup label={m.search_mission_label()} hasInfo>
                        <FilterSelect
                          ariaLabel={m.search_mission_label()}
                          value={field.value}
                          options={getMissionOptions()}
                          onValueChange={field.onChange}
                        />
                        <FormMessage />
                      </FilterGroup>
                    </FormItem>
                  )}
                />
                <FormField
                  control={form.control}
                  name="rover"
                  render={({ field }) => (
                    <FormItem>
                      <FilterGroup label={m.search_rover_label()} hasInfo>
                        <FilterSelect
                          ariaLabel={m.search_rover_label()}
                          value={field.value}
                          options={getRoverOptions()}
                          onValueChange={field.onChange}
                        />
                        <FormMessage />
                      </FilterGroup>
                    </FormItem>
                  )}
                />
                <FormField
                  control={form.control}
                  name="camera"
                  render={({ field }) => (
                    <FormItem>
                      <FilterGroup label={m.search_camera_label()} hasInfo>
                        <FilterSelect
                          ariaLabel={m.search_camera_label()}
                          value={field.value}
                          options={getCameraOptions()}
                          onValueChange={field.onChange}
                        />
                        <FormMessage />
                      </FilterGroup>
                    </FormItem>
                  )}
                />
                <div className="border-t border-white/10 pt-4">
                  <div className="mb-2 flex items-center justify-between">
                    <p className="text-xs font-semibold uppercase text-white">{m.search_suggested_label()}</p>
                  </div>
                  <ul className="space-y-1">
                    {suggestedSearches.map((suggestedSearch) => (
                      <SuggestedSearchButton
                        key={suggestedSearch.label}
                        suggestedSearch={suggestedSearch}
                        onApply={applySuggestedSearch}
                      />
                    ))}
                  </ul>
                </div>
              </div>
            </ScrollArea>
            <div className="border-t border-white/10 p-3">
              <Button
                type="submit"
                className="h-10 w-full rounded-md bg-space-cyan text-space-void hover:bg-space-cyan/90"
                aria-busy={isFetching}
                data-cy="search-btn"
              >
                <SlidersHorizontal className="h-4 w-4" />
                {m.search_apply_filters()}
              </Button>
            </div>
          </form>
        </Form>
      </aside>
    </>
  );
}

type FilterGroupProps = {
  label: string;
  hasInfo?: boolean;
  children: ReactNode;
};

function FilterGroup({ label, hasInfo = false, children }: FilterGroupProps) {
  return (
    <div className="space-y-2">
      <div className="flex items-center gap-2">
        <span className="text-xs font-semibold uppercase text-white">{label}</span>
        {hasInfo ? <Info className="h-3.5 w-3.5 text-muted-foreground" aria-hidden="true" /> : null}
      </div>
      {children}
    </div>
  );
}

type FilterSelectProps = {
  ariaLabel: string;
  value: string;
  options: readonly FilterOption[];
  onValueChange: (value: string) => void;
};

function FilterSelect({ ariaLabel, value, options, onValueChange }: FilterSelectProps) {
  const resolvedOptions = value.length > 0 && !options.some((option) => option.value === value)
    ? [...options, { label: value, value }]
    : options;
  const handleValueChange = (nextValue: string) => {
    onValueChange(nextValue === emptySelectValue ? "" : nextValue);
  };

  return (
    <Select value={value.length > 0 ? value : emptySelectValue} onValueChange={handleValueChange}>
      <SelectTrigger
        className="cosmara-control h-10 px-3 text-left shadow-inner shadow-black/20 [&>svg]:text-muted-foreground [&>svg]:opacity-100"
        aria-label={ariaLabel}
      >
        <SelectValue />
      </SelectTrigger>
      <SelectContent
        position="popper"
        className="z-[80] rounded-md border-white/10 bg-space-panel text-white shadow-2xl shadow-black/50"
      >
        {resolvedOptions.map((option) => (
          <SelectItem
            key={option.label}
            value={option.value.length > 0 ? option.value : emptySelectValue}
            className="cursor-pointer text-xs text-white focus:bg-space-cyan/15 focus:text-white data-[state=checked]:text-space-cyan"
          >
            {option.label}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  );
}

type SuggestedSearchButtonProps = {
  suggestedSearch: SuggestedSearch;
  onApply: (suggestedSearch: SuggestedSearch) => void;
};

function SuggestedSearchButton({ suggestedSearch, onApply }: SuggestedSearchButtonProps) {
  return (
    <li>
      <button
        type="button"
        className="flex w-full items-center gap-2 rounded-md px-1.5 py-1.5 text-left text-xs text-muted-foreground transition-colors hover:bg-white/5 hover:text-white"
        onClick={() => onApply(suggestedSearch)}
      >
        <Search className="h-3.5 w-3.5 shrink-0 text-space-cyan/80" aria-hidden="true" />
        <span className="truncate">{suggestedSearch.label}</span>
      </button>
    </li>
  );
}
