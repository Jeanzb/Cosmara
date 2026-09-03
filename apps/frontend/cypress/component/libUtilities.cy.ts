import { parseComparisonSections, stripMarkdown } from "@/lib/comparisonAnalysis";
import { formatFriendlyDateLabel, formatFriendlyDateRange } from "@/lib/searchDateRange";

describe("searchDateRange utilities", () => {
  it("describes the full archive when no dates are set", () => {
    expect(formatFriendlyDateRange("", "")).to.equal("Spanning the entire NASA archive");
  });

  it("formats a closed range as month-year with an arrow", () => {
    expect(formatFriendlyDateRange("2024-01-01", "2024-12-31")).to.equal("Jan 2024 → Dec 2024");
  });

  it("uses open-ended labels when only one bound is set", () => {
    expect(formatFriendlyDateRange("2020-06-15", "")).to.equal("Jun 2020 → present");
    expect(formatFriendlyDateRange("", "2020-06-15")).to.equal("earliest → Jun 2020");
  });

  it("formats an ISO date to short month and year in UTC", () => {
    expect(formatFriendlyDateLabel("2019-07-20")).to.equal("Jul 2019");
  });
});

describe("comparisonAnalysis utilities", () => {
  it("removes emphasis, code and heading markers", () => {
    expect(stripMarkdown("**bold** and `code`")).to.equal("bold and code");
    expect(stripMarkdown("# Heading")).to.equal("Heading");
  });

  it("parses a valid JSON analysis into sections", () => {
    const sections = parseComparisonSections(
      JSON.stringify({
        summary: "S",
        similarities: ["a"],
        differences: [],
        historicalContext: "",
        scientificValue: "",
        conclusion: ""
      })
    );

    expect(sections).to.not.be.null;
    expect(sections?.summary).to.equal("S");
    expect(sections?.similarities).to.deep.equal(["a"]);
  });

  it("returns null for plain text", () => {
    expect(parseComparisonSections("just narrative text")).to.be.null;
  });

  it("returns null when the JSON carries no meaningful content", () => {
    expect(parseComparisonSections(JSON.stringify({ title: "only a title" }))).to.be.null;
  });
});
