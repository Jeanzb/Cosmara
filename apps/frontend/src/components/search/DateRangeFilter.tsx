import { CalendarDays, ChevronLeft, ChevronRight, Sparkles } from "lucide-react";
import { useMemo, useState } from "react";
import { useFormField } from "@/components/ui/form";
import { cn } from "@/lib/utils";
import { getCurrentAppLanguage } from "@/lib/i18n";
import { formatFriendlyDateRange } from "@/lib/searchDateRange";
import { m } from "@/paraglide/messages";

type DatePreset = "any" | "custom" | "last-30" | "last-year";

type DateRangeFilterProps = {
  preset: DatePreset;
  dateFrom: string;
  dateTo: string;
  onPresetChange: (preset: string) => void;
  onDateChange: (field: "dateFrom" | "dateTo") => (value: string) => void;
};

type PresetChip = {
  value: DatePreset;
  label: string;
};

const getPresetChips = (): readonly PresetChip[] => [
  { value: "any", label: m.search_any_time() },
  { value: "last-30", label: m.search_last_30_days() },
  { value: "last-year", label: m.search_last_year() },
  { value: "custom", label: m.search_custom() }
];

const monthFormatter = (language: string) => new Intl.DateTimeFormat(language, {
  month: "long",
  year: "numeric",
  timeZone: "UTC"
});

const dayFormatter = (language: string) => new Intl.DateTimeFormat(language, {
  weekday: "short",
  timeZone: "UTC"
});

const dateFormatter = (language: string) => new Intl.DateTimeFormat(language, {
  day: "2-digit",
  month: "2-digit",
  year: "numeric",
  timeZone: "UTC"
});

const parseDateValue = (value: string): Date | null => {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) {
    return null;
  }

  const parsed = new Date(`${value}T00:00:00Z`);

  return Number.isNaN(parsed.getTime()) ? null : parsed;
};

const formatDateValue = (date: Date): string => date.toISOString().slice(0, 10);

const getMonthStart = (date: Date): Date => new Date(Date.UTC(date.getUTCFullYear(), date.getUTCMonth(), 1));

const buildCalendarDays = (visibleMonth: Date): readonly Date[] => {
  const firstDay = getMonthStart(visibleMonth);
  const leadingDays = firstDay.getUTCDay();
  const calendarStart = new Date(firstDay);
  calendarStart.setUTCDate(firstDay.getUTCDate() - leadingDays);

  return Array.from({ length: 42 }, (_, index) => {
    const day = new Date(calendarStart);
    day.setUTCDate(calendarStart.getUTCDate() + index);

    return day;
  });
};

const getWeekDays = (language: string): readonly string[] => Array.from({ length: 7 }, (_, index) => {
  const day = new Date(Date.UTC(2024, 0, 7 + index));

  return dayFormatter(language).format(day).replace(".", "");
});

export function DateRangeFilter({ preset, dateFrom, dateTo, onPresetChange, onDateChange }: DateRangeFilterProps) {
  const showCustomInputs = preset === "custom";
  const friendlyRange = formatFriendlyDateRange(dateFrom, dateTo);
  const { error, formMessageId } = useFormField();

  const renderChip = (chip: PresetChip) => {
    const isActive = preset === chip.value;

    return (
      <button
        key={chip.value}
        type="button"
        aria-pressed={isActive}
        className={cn(
          "rounded-full border px-3 py-1 text-[11px] font-medium transition-colors duration-150 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-space-cyan/70",
          isActive
            ? "border-space-orange/60 bg-space-orange/15 text-space-orange"
            : "border-white/10 bg-space-void/40 text-muted-foreground hover:border-white/25 hover:text-white"
        )}
        onClick={() => onPresetChange(chip.value)}
      >
        {chip.label}
      </button>
    );
  };

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap gap-1.5">{getPresetChips().map(renderChip)}</div>
      {showCustomInputs ? (
        <div className="grid grid-cols-1 gap-2">
          <DateInput
            label={m.search_from()}
            value={dateFrom}
            invalid={error !== undefined}
            describedBy={error === undefined ? undefined : formMessageId}
            onChange={onDateChange("dateFrom")}
          />
          <DateInput
            label={m.search_to()}
            value={dateTo}
            invalid={error !== undefined}
            describedBy={error === undefined ? undefined : formMessageId}
            onChange={onDateChange("dateTo")}
          />
        </div>
      ) : null}
      <div className="flex items-center gap-2 rounded-lg border border-space-cyan/15 bg-space-cyan/5 px-3 py-2">
        <Sparkles className="h-3.5 w-3.5 shrink-0 text-space-cyan" />
        <p className="text-[11px] leading-4 text-muted-foreground">
          <span className="font-medium text-space-cyan">{friendlyRange}</span>
        </p>
      </div>
    </div>
  );
}

type DateInputProps = {
  label: string;
  value: string;
  invalid: boolean;
  describedBy?: string;
  onChange: (value: string) => void;
};

function DateInput({ label, value, invalid, describedBy, onChange }: DateInputProps) {
  const selectedDate = parseDateValue(value);
  const language = getCurrentAppLanguage();
  const [isCalendarOpen, setIsCalendarOpen] = useState(false);
  const [visibleMonth, setVisibleMonth] = useState<Date>(() => getMonthStart(selectedDate ?? new Date()));
  const calendarDays = useMemo(() => buildCalendarDays(visibleMonth), [visibleMonth]);
  const weekDays = useMemo(() => getWeekDays(language), [language]);
  const selectedValue = selectedDate === null ? "" : formatDateValue(selectedDate);
  const todayValue = formatDateValue(new Date());
  const displayValue = selectedDate === null ? "yyyy-mm-dd" : dateFormatter(language).format(selectedDate);

  const toggleCalendar = () => {
    setIsCalendarOpen((currentValue) => !currentValue);
  };

  const showPreviousMonth = () => {
    setVisibleMonth((currentMonth) => new Date(Date.UTC(currentMonth.getUTCFullYear(), currentMonth.getUTCMonth() - 1, 1)));
  };

  const showNextMonth = () => {
    setVisibleMonth((currentMonth) => new Date(Date.UTC(currentMonth.getUTCFullYear(), currentMonth.getUTCMonth() + 1, 1)));
  };

  const selectDate = (date: Date) => {
    onChange(formatDateValue(date));
    setVisibleMonth(getMonthStart(date));
    setIsCalendarOpen(false);
  };

  const renderCalendarDay = (date: Date) => {
    const dateValue = formatDateValue(date);
    const isSelected = dateValue === selectedValue;
    const isToday = dateValue === todayValue;
    const isOutsideMonth = date.getUTCMonth() !== visibleMonth.getUTCMonth();

    return (
      <button
        key={dateValue}
        type="button"
        className={cn(
          "flex h-8 w-8 items-center justify-center rounded-md text-xs font-medium transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-space-cyan/70",
          isOutsideMonth ? "text-muted-foreground/35 hover:text-muted-foreground" : "text-white hover:bg-white/10",
          isToday && "border border-space-cyan/50 text-space-cyan",
          isSelected && "border-space-orange bg-space-orange text-space-void shadow-lg shadow-space-orange/20 hover:bg-space-orange"
        )}
        aria-pressed={isSelected}
        onClick={() => selectDate(date)}
      >
        {date.getUTCDate()}
      </button>
    );
  };

  return (
    <label className="block min-w-0">
      <span className="mb-1.5 block text-[10px] font-semibold uppercase text-muted-foreground">{label}</span>
      <span className="relative block min-w-0 space-y-2">
        <button
          type="button"
          className="cosmara-control flex min-w-0 items-center justify-between pr-2 text-left text-xs font-medium"
          aria-expanded={isCalendarOpen}
          aria-invalid={invalid}
          aria-describedby={describedBy}
          onClick={toggleCalendar}
        >
          <span className={cn(selectedDate === null && "text-muted-foreground")}>{displayValue}</span>
          <span className="flex h-7 w-7 items-center justify-center rounded-md text-muted-foreground transition-colors hover:bg-white/10 hover:text-space-cyan">
            <CalendarDays className="h-4 w-4" />
          </span>
        </button>
        {isCalendarOpen ? (
          <div className="cosmara-fade-in rounded-lg border border-space-cyan/20 bg-space-panel p-3 shadow-2xl shadow-black/45">
            <div className="mb-3 flex items-center justify-between">
              <button
                type="button"
                className="flex h-8 w-8 items-center justify-center rounded-md border border-white/10 text-muted-foreground transition-colors hover:border-space-cyan/40 hover:bg-space-cyan/10 hover:text-space-cyan"
                aria-label={m.search_previous_month()}
                onClick={showPreviousMonth}
              >
                <ChevronLeft className="h-4 w-4" />
              </button>
              <p className="text-xs font-semibold capitalize text-white">
                {monthFormatter(language).format(visibleMonth)}
              </p>
              <button
                type="button"
                className="flex h-8 w-8 items-center justify-center rounded-md border border-white/10 text-muted-foreground transition-colors hover:border-space-cyan/40 hover:bg-space-cyan/10 hover:text-space-cyan"
                aria-label={m.search_next_month()}
                onClick={showNextMonth}
              >
                <ChevronRight className="h-4 w-4" />
              </button>
            </div>
            <div className="mb-1 grid grid-cols-7 gap-1">
              {weekDays.map((weekDay) => (
                <span key={weekDay} className="py-1 text-center text-[10px] font-semibold uppercase text-muted-foreground">
                  {weekDay.slice(0, 2)}
                </span>
              ))}
            </div>
            <div className="grid grid-cols-7 gap-1">{calendarDays.map(renderCalendarDay)}</div>
          </div>
        ) : null}
      </span>
    </label>
  );
}
