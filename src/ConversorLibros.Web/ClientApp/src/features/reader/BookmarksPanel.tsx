import { useEffect, useId, useRef, useState, type KeyboardEvent } from "react";
import type { ReaderBookmarkDto, ReaderIndexChapterDto } from "../../shared/api/types";

interface Props {
  bookmarks: readonly ReaderBookmarkDto[];
  chapters: readonly ReaderIndexChapterDto[];
  error: string | null;
  /** False while there is no loaded chapter whose position can be bookmarked. */
  canAdd: boolean;
  onAdd: () => void;
  onJump: (bookmark: ReaderBookmarkDto) => void;
  onDelete: (bookmark: ReaderBookmarkDto) => void;
}

const UNAVAILABLE = "Capítulo no disponible";

function formatTime(ms: number): string {
  const total = Math.floor(Math.max(0, ms) / 1000);
  return `${Math.floor(total / 60)}:${String(total % 60).padStart(2, "0")}`;
}

export function BookmarksPanel({ bookmarks, chapters, error, canAdd, onAdd, onJump, onDelete }: Props) {
  const [open, setOpen] = useState(false);
  const panelId = useId();
  const toggleRef = useRef<HTMLButtonElement>(null);

  const panelRef = useRef<HTMLDivElement>(null);
  const addRef = useRef<HTMLButtonElement>(null);
  /** Delete control (bookmark id, or "" for the add button) to focus once the deleted item is gone. */
  const focusAfterRef = useRef<string | null>(null);

  useEffect(() => {
    const target = focusAfterRef.current;
    if (target === null) return;
    focusAfterRef.current = null;
    const next = target ? panelRef.current?.querySelector<HTMLElement>(`[data-bookmark-delete="${CSS.escape(target)}"]`) : null;
    (next ?? addRef.current)?.focus();
  }, [bookmarks]);

  // A failed delete leaves the list unchanged: do not carry the pending focus move over to a later update.
  useEffect(() => {
    if (error) focusAfterRef.current = null;
  }, [error]);

  const close = () => {
    setOpen(false);
    toggleRef.current?.focus();
  };
  const onKeyDown = (event: KeyboardEvent) => {
    if (event.key === "Escape" && open) close();
  };

  return (
    <div className="reader-bookmarks" onKeyDown={onKeyDown}>
      <button
        ref={toggleRef}
        type="button"
        aria-expanded={open}
        aria-controls={panelId}
        onClick={() => setOpen((o) => !o)}
      >
        Marcadores
      </button>
      {open && (
        <div id={panelId} ref={panelRef} className="reader-bookmarks-panel" role="group" aria-label="Marcadores">
          <button ref={addRef} type="button" disabled={!canAdd} onClick={onAdd}>
            Añadir marcador
          </button>
          {error && <p role="alert">{error}</p>}
          {bookmarks.length === 0 ? (
            <p>Aún no hay marcadores.</p>
          ) : (
            <ul>
              {bookmarks.map((b) => {
                const title = chapters.find((c) => c.index === b.chapterIndex)?.title;
                const label = `${title ?? UNAVAILABLE} · ${formatTime(b.ms)}`;
                return (
                  <li key={b.id}>
                    <button
                      type="button"
                      disabled={title === undefined}
                      onClick={() => {
                        onJump(b);
                        close();
                      }}
                    >
                      {label}
                      {b.excerpt && <span className="reader-bookmark-excerpt">{b.excerpt}</span>}
                    </button>
                    <button
                      type="button"
                      data-bookmark-delete={b.id}
                      aria-label={`Eliminar marcador de ${title ?? UNAVAILABLE} (${formatTime(b.ms)})`}
                      onClick={() => {
                        const i = bookmarks.indexOf(b);
                        focusAfterRef.current = (bookmarks[i + 1] ?? bookmarks[i - 1])?.id ?? "";
                        onDelete(b);
                      }}
                    >
                      Eliminar
                    </button>
                  </li>
                );
              })}
            </ul>
          )}
        </div>
      )}
    </div>
  );
}
