import { zodResolver } from "@hookform/resolvers/zod";
import { Link, useNavigate, useRouterState } from "@tanstack/react-router";
import {
  ChevronDown,
  GitCompareArrows,
  Grid2X2,
  Library,
  LogOut,
  Orbit,
  Search,
  type LucideIcon
} from "lucide-react";
import { useEffect, type ReactNode } from "react";
import { useForm } from "react-hook-form";
import { z } from "zod";
import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger
} from "@/components/ui/dropdown-menu";
import { Input } from "@/components/ui/input";
import { Form, FormControl, FormField, FormItem, FormMessage } from "@/components/ui/form";
import { defaultNasaSearchQuery } from "@/constants";
import { useAuthSession } from "@/hooks/auth";
import { useUiStore, uiSelectors } from "@/store";
import { cn } from "@/lib/utils";
import { fallbackExplorerName, getUserDisplayName, getUserInitials } from "@/lib/userProfile";
import { m } from "@/paraglide/messages";
import { setAppLanguage } from "@/lib/i18n";
import { normalizeSearchRouteState } from "@/lib/searchRoute";
import type { AppLanguage } from "@/types/ui";

type AppShellProps = {
  children: ReactNode;
  contentClassName?: string;
};

type AppNavTarget = "/search" | "/collections" | "/comparator";

type AppNavLinkProps = {
  to: AppNavTarget;
  label: string;
  icon: LucideIcon;
  isActive: boolean;
};

const shellSearchSchema = z.object({
  query: z.string().trim().max(160)
});

type ShellSearchFormValues = z.infer<typeof shellSearchSchema>;

const isRouteActive = (pathname: string, target: AppNavTarget): boolean =>
  pathname === target || pathname.startsWith(`${target}/`);

export function AppShell({ children, contentClassName }: AppShellProps) {
  const pathname = useRouterState({
    select: (state) => state.location.pathname
  });

  return (
    <div className="min-h-screen bg-space-void text-foreground">
      <header className="sticky top-0 z-40 border-b border-white/10 bg-space-shell/95 backdrop-blur-xl">
        <div className="grid h-14 grid-cols-[auto_1fr_auto] items-center gap-4 px-4">
          <div className="flex min-w-0 items-center gap-4">
            <Link to="/search" className="flex min-w-0 items-center gap-3">
              <span className="flex h-8 w-8 items-center justify-center rounded-md border border-space-orange/30 bg-space-orange/10 text-space-orange">
                <Orbit className="h-5 w-5" />
              </span>
              <span className="hidden min-w-0 sm:block">
                <span className="block text-base font-semibold leading-4 tracking-normal text-white">{m.app_brand()}</span>
                <span className="block text-[10px] font-medium uppercase tracking-normal text-muted-foreground">
                  {m.app_tagline()}
                </span>
              </span>
            </Link>
            <nav className="hidden items-center gap-1 md:flex" aria-label={m.nav_primary()}>
              <AppNavLink
                to="/search"
                label={m.nav_explore()}
                icon={Grid2X2}
                isActive={isRouteActive(pathname, "/search")}
              />
              <AppNavLink
                to="/collections"
                label={m.nav_collections()}
                icon={Library}
                isActive={isRouteActive(pathname, "/collections")}
              />
              <AppNavLink
                to="/comparator"
                label={m.nav_compare()}
                icon={GitCompareArrows}
                isActive={isRouteActive(pathname, "/comparator")}
              />
            </nav>
          </div>
          <ShellSearchField />
          <div className="flex items-center gap-2 justify-end">
            <LanguageMenu />
            <MobileSearchLink pathname={pathname} />
            <UserMenu />
          </div>
        </div>
      </header>
      <main className={cn("min-h-[calc(100vh-3.5rem)] pb-14 md:pb-0", contentClassName)}>{children}</main>
      <MobileBottomNav pathname={pathname} />
    </div>
  );
}

function UserMenu() {
  const navigate = useNavigate();
  const { user, isAuthenticated, logout } = useAuthSession();
  const displayName = getUserDisplayName(user);
  const initials = getUserInitials(user);

  const handleLogout = () => {
    logout();
    void navigate({ to: "/auth" });
  };

  if (!isAuthenticated) {
    return (
      <Button
        asChild
        variant="ghost"
        className="h-10 rounded-full border border-white/10 bg-space-panel px-2 pr-3 text-sm text-foreground shadow-sm shadow-black/20 hover:bg-space-panelStrong hover:text-white"
      >
        <Link to="/auth">
          <Avatar className="h-7 w-7 border border-space-orange/30">
            <AvatarFallback className="bg-space-orange text-xs font-semibold text-space-void">KJ</AvatarFallback>
          </Avatar>
          <span className="hidden font-medium sm:inline">{m.auth_sign_in()}</span>
        </Link>
      </Button>
    );
  }

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <button
          type="button"
          className="flex h-10 items-center gap-2 rounded-full border border-white/10 bg-space-panel px-2 pr-3 text-left text-sm text-foreground shadow-sm shadow-black/20 transition-colors hover:bg-space-panelStrong"
        >
          <Avatar className="h-7 w-7 border border-space-orange/30">
            <AvatarFallback className="bg-space-orange text-xs font-semibold text-space-void">{initials}</AvatarFallback>
          </Avatar>
          <span className="hidden max-w-36 truncate font-medium sm:inline">{displayName}</span>
          <ChevronDown className="h-4 w-4 text-muted-foreground" />
        </button>
      </DropdownMenuTrigger>
      <DropdownMenuContent
        align="end"
        className="w-64 border-white/10 bg-space-panel p-2 text-foreground shadow-2xl shadow-black/35"
      >
        <DropdownMenuLabel className="px-2 py-2">
          <span className="block text-sm text-white">{displayName}</span>
          <span className="mt-1 block truncate text-xs font-normal text-muted-foreground">
            {user?.email ?? fallbackExplorerName}
          </span>
        </DropdownMenuLabel>
        <DropdownMenuSeparator className="bg-white/10" />
        <DropdownMenuItem
          onSelect={handleLogout}
          className="cursor-pointer rounded-md text-muted-foreground focus:bg-white/10 focus:text-white"
        >
          <LogOut className="h-4 w-4" />
          {m.auth_sign_out()}
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}

function LanguageMenu() {
  const currentLanguage = useUiStore(uiSelectors.currentLanguage);
  const setLanguage = useUiStore(uiSelectors.setLanguageAction);

  const handleLanguageSelect = (language: AppLanguage) => () => {
    setLanguage(language);
    setAppLanguage(language);
  };

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button
          type="button"
          variant="ghost"
          size="sm"
          className="h-9 rounded-full border border-white/10 bg-space-panel px-3 text-xs uppercase tracking-wide text-muted-foreground hover:bg-space-panelStrong hover:text-white"
          aria-label={m.language_label()}
        >
          {currentLanguage}
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent
        align="end"
        className="w-40 border-white/10 bg-space-panel p-1 text-foreground shadow-2xl shadow-black/35"
      >
        <DropdownMenuLabel className="px-2 py-1.5 text-xs text-muted-foreground">
          {m.language_label()}
        </DropdownMenuLabel>
        <DropdownMenuSeparator className="bg-white/10" />
        <DropdownMenuItem
          onSelect={handleLanguageSelect("en")}
          className="cursor-pointer rounded-md text-muted-foreground focus:bg-white/10 focus:text-white"
        >
          {m.language_english()}
        </DropdownMenuItem>
        <DropdownMenuItem
          onSelect={handleLanguageSelect("es")}
          className="cursor-pointer rounded-md text-muted-foreground focus:bg-white/10 focus:text-white"
        >
          {m.language_spanish()}
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}

function AppNavLink({ to, label, icon: Icon, isActive }: AppNavLinkProps) {
  return (
    <Button
      asChild
      variant={isActive ? "secondary" : "ghost"}
      size="sm"
      className={cn(
        "h-9 rounded-md px-3 text-muted-foreground hover:bg-white/5 hover:text-white",
        isActive && "bg-white/10 text-white hover:bg-white/10"
      )}
    >
      <Link to={to}>
        <Icon className="h-4 w-4" />
        {label}
      </Link>
    </Button>
  );
}

function ShellSearchField() {
  const navigate = useNavigate();
  const { pathname, search } = useRouterState({
    select: (state) => ({
      pathname: state.location.pathname,
      search: state.location.search as { q?: unknown }
    })
  });
  const activeSearchState = pathname.startsWith("/search")
    ? normalizeSearchRouteState(search as Record<string, unknown>)
    : {};
  const routeQuery = activeSearchState.q ?? "";
  const form = useForm<ShellSearchFormValues>({
    resolver: zodResolver(shellSearchSchema),
    defaultValues: { query: routeQuery }
  });
  const { reset } = form;

  useEffect(() => {
    reset({ query: routeQuery });
  }, [reset, routeQuery]);

  const handleSubmit = (values: ShellSearchFormValues) => {
    const nextQuery = values.query.trim();
    const queryChanged = nextQuery !== routeQuery;

    void navigate({
      to: "/search",
      search: {
        ...activeSearchState,
        q: nextQuery.length > 0 && nextQuery !== defaultNasaSearchQuery ? nextQuery : undefined,
        semantic: nextQuery.length > 0 ? activeSearchState.semantic : undefined,
        suppressInferred: queryChanged ? undefined : activeSearchState.suppressInferred
      }
    });
  };

  return (
    <Form {...form}>
      <form
        className="mx-auto hidden w-full max-w-[420px] items-center md:flex"
        noValidate
        onSubmit={form.handleSubmit(handleSubmit)}
      >
        <FormField
          control={form.control}
          name="query"
          render={({ field }) => (
            <FormItem className="relative w-full space-y-0">
              <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
              <FormControl>
                <Input
                  {...field}
                  type="search"
                  maxLength={160}
                  placeholder={m.search_placeholder()}
                  className="h-9 rounded-full border-white/10 bg-space-void/60 pl-10 pr-4 text-sm text-foreground shadow-inner shadow-black/20 placeholder:text-muted-foreground focus-visible:ring-space-cyan/70"
                  aria-label={m.search_aria()}
                />
              </FormControl>
              <FormMessage className="sr-only" />
            </FormItem>
          )}
        />
      </form>
    </Form>
  );
}

type MobileSearchLinkProps = {
  pathname: string;
};

function MobileSearchLink({ pathname }: MobileSearchLinkProps) {
  const navigate = useNavigate();
  const openMobileFilters = useUiStore(uiSelectors.openMobileFiltersAction);

  const handleClick = () => {
    if (!pathname.startsWith("/search")) {
      void navigate({ to: "/search", search: {} });
    }

    openMobileFilters();
  };

  return (
    <Button
      type="button"
      variant="ghost"
      size="icon"
      className="h-9 w-9 text-muted-foreground hover:bg-white/5 hover:text-foreground md:hidden"
      aria-label={m.search_aria()}
      data-cy="mobile-search"
      onClick={handleClick}
    >
      <Search className="h-5 w-5" />
    </Button>
  );
}

type MobileBottomNavProps = {
  pathname: string;
};

type MobileNavItem = {
  to: AppNavTarget;
  label: string;
  icon: LucideIcon;
};

function MobileBottomNav({ pathname }: MobileBottomNavProps) {
  const mobileNavItems: readonly MobileNavItem[] = [
    { to: "/search", label: m.nav_explore(), icon: Grid2X2 },
    { to: "/collections", label: m.nav_collections(), icon: Library },
    { to: "/comparator", label: m.nav_compare(), icon: GitCompareArrows }
  ];

  return (
    <nav className="cosmara-mobile-nav" aria-label={m.nav_mobile()}>
      {mobileNavItems.map((item) => {
        const active = isRouteActive(pathname, item.to);
        return (
          <Link
            key={item.to}
            to={item.to}
            className={cn(
              "flex flex-col items-center gap-0.5 rounded-md px-3 py-1.5 text-[10px] font-medium transition-colors",
              active ? "text-space-orange" : "text-muted-foreground"
            )}
            aria-current={active ? "page" : undefined}
          >
            <item.icon className="h-5 w-5" />
            {item.label}
          </Link>
        );
      })}
    </nav>
  );
}
