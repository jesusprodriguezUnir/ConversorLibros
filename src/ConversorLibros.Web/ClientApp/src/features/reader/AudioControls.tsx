import { SKIP_SECONDS } from "./sync";

interface Props {
  playing: boolean;
  disabled: boolean;
  speed: number;
  error: string | null;
  onToggle: () => void;
  onSkip: (seconds: number) => void;
  onSpeed: (direction: 1 | -1) => void;
}

export function AudioControls({ playing, disabled, speed, error, onToggle, onSkip, onSpeed }: Props) {
  return (
    <div className="reader-audio">
      <button type="button" disabled={disabled} onClick={() => onSkip(-SKIP_SECONDS)}>
        −{SKIP_SECONDS} s
      </button>
      <button type="button" disabled={disabled} onClick={onToggle}>
        {playing ? "Pausa" : "Reproducir"}
      </button>
      <button type="button" disabled={disabled} onClick={() => onSkip(SKIP_SECONDS)}>
        +{SKIP_SECONDS} s
      </button>
      <button type="button" onClick={() => onSpeed(-1)}>
        Más lenta
      </button>
      <span aria-label="Velocidad">{speed}×</span>
      <button type="button" onClick={() => onSpeed(1)}>
        Más rápida
      </button>
      {error && <p role="alert">{error}</p>}
    </div>
  );
}
