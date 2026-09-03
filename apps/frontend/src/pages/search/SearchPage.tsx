import { AppShell } from "@/components/app";
import { SearchDashboard } from "@/components/search";
import type { NasaSearchRouteState } from "@/types/search";

type SearchPageProps = {
  searchState: NasaSearchRouteState;
};

export function SearchPage({ searchState }: SearchPageProps) {
  return (
    <AppShell contentClassName="overflow-hidden">
      <SearchDashboard searchState={searchState} />
    </AppShell>
  );
}
