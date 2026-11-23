import type { ComponentProps } from "react";
import { AudioControls } from "./AudioControls";
import { AppearancePanel } from "./AppearancePanel";
import { BackToAudioButton } from "./BackToAudioButton";
import { BookmarksPanel } from "./BookmarksPanel";

interface Props extends ComponentProps<typeof AudioControls> {
  percent: number;
  suspended: boolean;
  onBackToAudio: () => void;
  bookmarks: ComponentProps<typeof BookmarksPanel>;
}

export function ReaderToolbar({ percent, suspended, onBackToAudio, bookmarks, ...audio }: Props) {
  return (
    <div className="reader-toolbar" role="toolbar" aria-label="Audio">
      <AudioControls {...audio} />
      <span>Libro: {percent} %</span>
      {suspended && <BackToAudioButton onClick={onBackToAudio} />}
      <BookmarksPanel {...bookmarks} />
      <AppearancePanel />
    </div>
  );
}
