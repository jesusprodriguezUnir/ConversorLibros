import { useId, useRef, useState, type KeyboardEvent } from "react";
import { FONT_SIZES_REM, THEMES, type Theme } from "./appearance";
import { useReaderAppearance } from "./ReaderAppearanceContext";

const THEME_LABELS: Record<Theme, string> = { light: "Claro", sepia: "Sepia", dark: "Oscuro" };
const MAX_STEP = FONT_SIZES_REM.length - 1;

export function AppearancePanel() {
  const [open, setOpen] = useState(false);
  const { theme, fontStep, setTheme, changeFont } = useReaderAppearance();
  const panelId = useId();
  const toggleRef = useRef<HTMLButtonElement>(null);
  const smallerRef = useRef<HTMLButtonElement>(null);
  const largerRef = useRef<HTMLButtonElement>(null);

  const onKeyDown = (event: KeyboardEvent) => {
    if (event.key !== "Escape" || !open) return;
    setOpen(false);
    toggleRef.current?.focus();
  };
  // A button that becomes disabled drops focus, so hand it to its counterpart first.
  const resize = (delta: 1 | -1) => {
    const next = fontStep + delta;
    if (next <= 0 || next >= MAX_STEP) (delta > 0 ? smallerRef : largerRef).current?.focus();
    changeFont(delta);
  };

  return (
    <div className="reader-appearance" onKeyDown={onKeyDown}>
      <button
        ref={toggleRef}
        type="button"
        aria-expanded={open}
        aria-controls={panelId}
        onClick={() => setOpen((o) => !o)}
      >
        Apariencia
      </button>
      {open && (
        <div id={panelId} className="reader-appearance-panel" role="group" aria-label="Opciones de apariencia">
          <div role="radiogroup" aria-label="Tema">
            {THEMES.map((t) => (
              <label key={t}>
                <input type="radio" name="reader-theme" checked={theme === t} onChange={() => setTheme(t)} />
                {THEME_LABELS[t]}
              </label>
            ))}
          </div>
          <div>
            <button ref={smallerRef} type="button" disabled={fontStep === 0} onClick={() => resize(-1)}>
              Reducir texto
            </button>
            <button ref={largerRef} type="button" disabled={fontStep === MAX_STEP} onClick={() => resize(1)}>
              Aumentar texto
            </button>
            <output aria-live="polite">Tamaño del texto: {Math.round(FONT_SIZES_REM[fontStep] * 16)} px</output>
          </div>
        </div>
      )}
    </div>
  );
}
