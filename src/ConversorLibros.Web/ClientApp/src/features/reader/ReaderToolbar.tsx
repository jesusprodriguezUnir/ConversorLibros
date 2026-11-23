import type { ComponentProps } from "react";
import { AudioControls } from "./AudioControls";
import { BackToAudioButton } from "./BackToAudioButton";

interface Props extends ComponentProps<typeof AudioControls> {
  percent: number;
  suspended: boolean;
  onBackToAudio: () => void;
}

export function ReaderToolbar({ percent, suspended, onBackToAudio, ...audio }: Props) {
  return (
    <div className="reader-toolbar" role="toolbar" aria-label="Audio">
      <AudioControls {...audio} />
      <span>Libro: {percent} %</span>
      {suspended && <BackToAudioButton onClick={onBackToAudio} />}
    </div>
  );
}
