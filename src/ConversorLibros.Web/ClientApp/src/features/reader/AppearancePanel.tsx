import { useState } from "react";
import { FONT_SIZES_REM, THEMES, type Theme } from "./appearance";
import { useReaderAppearance } from "./ReaderAppearanceContext";

const THEME_LABELS: Record<Theme, string> = { light: "Claro", sepia: "Sepia", dark: "Oscuro" };

export function AppearancePanel() {
  const [open, setOpen] = useState(false);
  const { theme, fontStep, setTheme, changeFont } = useReaderAppearance();

  return (
    <div className="reader-appearance">
      <button type="button" aria-expanded={open} onClick={() => setOpen((o) => !o)}>
        Apariencia
      </button>
      {open && (
        <div className="reader-appearance-panel" role="group" aria-label="Opciones de apariencia">
          <div role="radiogroup" aria-label="Tema">
            {THEMES.map((t) => (
              <label key={t}>
                <input type="radio" name="reader-theme" checked={theme === t} onChange={() => setTheme(t)} />
                {THEME_LABELS[t]}
              </label>
            ))}
          </div>
          <div>
            <button type="button" disabled={fontStep === 0} onClick={() => changeFont(-1)}>
              Reducir texto
            </button>
            <button type="button" disabled={fontStep === FONT_SIZES_REM.length - 1} onClick={() => changeFont(1)}>
              Aumentar texto
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
