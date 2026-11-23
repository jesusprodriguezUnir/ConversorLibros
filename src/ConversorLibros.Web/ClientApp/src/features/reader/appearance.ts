export const APPEARANCE_KEY = "reader.appearance.v1";

export const THEMES = ["light", "sepia", "dark"] as const;
export type Theme = (typeof THEMES)[number];

/** Selectable text sizes (rem); `fontStep` indexes this list. */
export const FONT_SIZES_REM = [0.9, 1, 1.125, 1.25, 1.5, 1.75] as const;

export interface Appearance {
  theme: Theme;
  fontStep: number;
}

export const DEFAULT_APPEARANCE: Appearance = { theme: "light", fontStep: 2 };

export type AppearanceAction = { type: "theme"; theme: Theme } | { type: "font"; delta: number };

export function appearanceReducer(state: Appearance, action: AppearanceAction): Appearance {
  if (action.type === "theme") return { ...state, theme: action.theme };
  const fontStep = Math.min(Math.max(0, state.fontStep + action.delta), FONT_SIZES_REM.length - 1);
  return { ...state, fontStep };
}

/** Reads the stored appearance; each field falls back to its default when missing or invalid. */
export function loadAppearance(): Appearance {
  try {
    const raw = JSON.parse(localStorage.getItem(APPEARANCE_KEY) ?? "null") as Partial<Appearance> | null;
    if (typeof raw !== "object" || raw === null) return DEFAULT_APPEARANCE;
    const theme = THEMES.find((t) => t === raw.theme) ?? DEFAULT_APPEARANCE.theme;
    const step = raw.fontStep;
    const valid = Number.isInteger(step) && step! >= 0 && step! < FONT_SIZES_REM.length;
    return { theme, fontStep: valid ? step! : DEFAULT_APPEARANCE.fontStep };
  } catch {
    return DEFAULT_APPEARANCE;
  }
}

export function saveAppearance(appearance: Appearance) {
  try {
    localStorage.setItem(APPEARANCE_KEY, JSON.stringify(appearance));
  } catch {
    // Storage may be unavailable or full; the setting then lasts for this session only.
  }
}
