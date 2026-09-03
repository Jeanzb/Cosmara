import type { NasaSearchFilters } from "@/types/search";

export const queryKeys = {
  search: {
    results: (filters: NasaSearchFilters, isSemantic: boolean, runId?: string, requestVersion = 0) =>
      ["search", "results", isSemantic ? "semantic" : "standard", filters, runId, requestVersion] as const
  },
  collections: {
    root: ["collections"] as const,
    list: () => ["collections", "list"] as const,
    detail: (collectionId: string) => ["collections", "detail", collectionId] as const
  },
  ai: {
    enrichment: (nasaImageId: string) => ["ai", "enrichment", nasaImageId] as const,
    comparison: (imageIds: readonly string[]) => ["ai", "comparison", imageIds] as const
  },
  tags: {
    suggestions: (collectionImageId: string) => ["tags", "suggestions", collectionImageId] as const
  },
  export: {
    collectionPdf: (collectionId: string) => ["export", "collection-pdf", collectionId] as const
  }
};
