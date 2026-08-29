// Uncategorized (competitionCategoryName === null — entries created outside the import flow).
export const UNCATEGORIZED_COLOR = 'var(--color-bp-hueso-100)';

// 8 pastel tints (HSL L=90, S=58, hues spaced round the wheel), ink text stays the single dark
// var(--color-bp-text) (#1f2320) against every one of them — computed contrast ranges
// 11.47:1-13.68:1, comfortably clear of WCAG AA's 4.5:1. The BOS-flagged ring
// (var(--color-bp-cobre-700), #9a4b27) clears the 3:1 non-text minimum against every tint too
// (4.44:1-5.29:1 measured).
const CATEGORY_PALETTE: readonly string[] = [
  '#d7e6f4', // blue
  '#f4e2d7', // orange
  '#d7f4ed', // aqua
  '#f4ebd7', // gold
  '#f4d7e3', // magenta
  '#d9f4d7', // green
  '#ded7f4', // violet
  '#f4d9d7', // red
];

// Deterministic per-competition assignment: names sorted alphabetically (same order the board's
// existing category filter dropdown already uses) so a category always lands on the same slot for
// the session, cycling past 8 slots. Repeats past 8 are an acceptable degradation here (unlike a
// chart legend) because color is a secondary cue — the category/style text is always shown too.
export function buildCategoryColorMap(
  categoryNames: readonly string[],
): ReadonlyMap<string, string> {
  const sorted = [...new Set(categoryNames)].sort((a, b) => a.localeCompare(b));
  return new Map(sorted.map((name, i) => [name, CATEGORY_PALETTE[i % CATEGORY_PALETTE.length]]));
}
