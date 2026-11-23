import { createContext, useCallback, useContext, useEffect, useMemo, useReducer, type ReactNode } from "react";
import { appearanceReducer, loadAppearance, saveAppearance, type Appearance, type Theme } from "./appearance";

interface ReaderAppearanceValue extends Appearance {
  setTheme: (theme: Theme) => void;
  changeFont: (delta: number) => void;
}

const ReaderAppearanceContext = createContext<ReaderAppearanceValue | null>(null);

export function ReaderAppearanceProvider({ children }: { children: ReactNode }) {
  const [state, dispatch] = useReducer(appearanceReducer, undefined, loadAppearance);
  const setTheme = useCallback((theme: Theme) => dispatch({ type: "theme", theme }), []);
  const changeFont = useCallback((delta: number) => dispatch({ type: "font", delta }), []);

  useEffect(() => saveAppearance(state), [state]);

  const value = useMemo(() => ({ ...state, setTheme, changeFont }), [state, setTheme, changeFont]);
  return <ReaderAppearanceContext.Provider value={value}>{children}</ReaderAppearanceContext.Provider>;
}

export function useReaderAppearance() {
  const ctx = useContext(ReaderAppearanceContext);
  if (!ctx) {
    throw new Error("useReaderAppearance must be used within ReaderAppearanceProvider");
  }
  return ctx;
}
