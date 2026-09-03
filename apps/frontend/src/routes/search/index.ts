import { createFileRoute } from "@tanstack/react-router";
import { createElement } from "react";
import { SearchPage } from "@/pages/search";
import { normalizeSearchRouteState } from "@/lib/searchRoute";

export const Route = createFileRoute("/search/")({
  validateSearch: normalizeSearchRouteState,
  component: SearchRoute
});

function SearchRoute() {
  const search = Route.useSearch();

  return createElement(SearchPage, { searchState: search });
}
