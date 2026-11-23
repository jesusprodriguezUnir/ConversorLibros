import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { api } from "../../shared/api/client";
import type { ReaderChapterDto, ReaderIndexDto } from "../../shared/api/types";
import { CoverPage } from "./CoverPage";
import { PageIndicator } from "./PageIndicator";
import { PaginatedChapter } from "./PaginatedChapter";
import { ReaderToc } from "./ReaderToc";
import { ReaderToolbar } from "./ReaderToolbar";
import { flattenEntries } from "./entries";
import { clampPage, spreadStart } from "./pagination";
import { SPEED_STEP, bookPercent } from "./sync";
import { useAudioSync } from "./useAudioSync";
import { useAutoFollow } from "./useAutoFollow";
import "./reader.css";

const RECONVERT_HINT = "Reconvierte el libro para habilitar el lector";

type IndexState = { status: "loading" } | { status: "unavailable" } | { status: "ready"; index: ReaderIndexDto };
type ChapterState =
  | { status: "loading" }
  | { status: "error"; message: string }
  | { status: "ready"; chapter: ReaderChapterDto };

export function ReaderPage() {
  const { id = "" } = useParams();
  const [indexState, setIndexState] = useState<IndexState>({ status: "loading" });
  const [current, setCurrent] = useState<number | null>(null);
  const [chapterState, setChapterState] = useState<ChapterState>({ status: "loading" });
  const [page, setPage] = useState(0);
  const [layout, setLayout] = useState({ count: 1, ppv: 1 as 1 | 2, stride: 1 });
  const onLayout = useCallback((l: { count: number; ppv: 1 | 2; stride: number }) => setLayout(l), []);
  const [audio] = useState(() => new Audio());
  const contentRef = useRef<HTMLDivElement>(null);
  const follow = useAutoFollow();

  const chapters = indexState.status === "ready" ? indexState.index.chapters : [];
  const chapter = chapterState.status === "ready" ? chapterState.chapter : null;
  const entries = useMemo(() => (chapter ? flattenEntries(chapter) : []), [chapter]);
  const shown = clampPage(page, layout.count);
  const position = chapters.findIndex((c) => c.index === current);
  const nextChapter = chapters[position + 1]?.index;
  const prevChapter = chapters[position - 1]?.index;

  // A manual pick drops any pending autoplay; `onEnded` re-arms it after calling this.
  const selectChapter = (chapterIndex: number) => {
    sync.cancelAutoplay();
    follow.resume();
    setCurrent(chapterIndex);
    setPage(0);
  };
  const go = (p: number) => setPage(clampPage(p, layout.count));

  const sync = useAudioSync({
    audio,
    src: chapter && current !== null ? api.readerAudioUrl(id, current) : null,
    entries,
    durationMs: chapter?.durationMs ?? 0,
    contentRef,
    stride: layout.stride,
    ppv: layout.ppv,
    page: shown,
    followingRef: follow.followingRef,
    onPage: go,
    onEnded: () => {
      if (nextChapter === undefined) return false;
      selectChapter(nextChapter);
      return true;
    },
  });

  useEffect(() => {
    let cancelled = false;
    api
      .getReaderIndex(id)
      .then((index) => {
        if (cancelled) return;
        if (!index.available || index.chapters.length === 0) return setIndexState({ status: "unavailable" });
        setIndexState({ status: "ready", index });
        setCurrent(index.chapters[0].index);
      })
      .catch(() => !cancelled && setIndexState({ status: "unavailable" }));
    return () => {
      cancelled = true;
    };
  }, [id]);

  useEffect(() => {
    if (current === null) return;
    let cancelled = false;
    setChapterState({ status: "loading" });
    api
      .getReaderChapter(id, current)
      .then((chapter) => !cancelled && setChapterState({ status: "ready", chapter }))
      .catch(() => !cancelled && setChapterState({ status: "error", message: "No se pudo cargar este capítulo." }));
    return () => {
      cancelled = true;
    };
  }, [id, current]);

  if (indexState.status === "loading") return <div className="reader-shell">Cargando…</div>;
  if (indexState.status === "unavailable") {
    return (
      <div className="reader-shell reader-error">
        <p>{RECONVERT_HINT}</p>
        <Link to="/biblioteca">Volver a la biblioteca</Link>
      </div>
    );
  }

  const { index } = indexState;
  const start = spreadStart(shown, layout.ppv);
  // Turning pages by hand while audio plays suspends auto-follow.
  const manualGo = (p: number) => {
    if (sync.playing) follow.suspend();
    go(p);
  };
  const backToAudio = () => {
    follow.resume();
    sync.syncNow();
  };
  const entryClick = (entryIndex: number) => {
    follow.resume();
    sync.seekTo(entries[entryIndex].startMs);
  };
  // The layout still describes the previous chapter until the new one renders.
  const pageReady = chapterState.status === "ready";

  return (
    <div className="reader-shell">
      <header className="reader-bar">
        <Link to="/biblioteca">Biblioteca</Link>
        <strong>{index.title}</strong>
      </header>
      <ReaderToc chapters={index.chapters} current={current} onSelect={selectChapter} />
      <main className="reader-main">
        {chapterState.status === "loading" && <p role="status">Cargando capítulo…</p>}
        {chapterState.status === "error" && <p role="alert">{chapterState.message}</p>}
        {chapterState.status === "ready" && (
          <PaginatedChapter
            chapter={chapterState.chapter}
            page={shown}
            contentRef={contentRef}
            onLayout={onLayout}
            onEntryClick={entryClick}
            cover={position === 0 && index.hasCover ? <CoverPage bookId={id} title={index.title} /> : null}
          />
        )}
      </main>
      <ReaderToolbar
        playing={sync.playing}
        disabled={!pageReady}
        speed={sync.speed}
        error={sync.error}
        percent={bookPercent(
          chapters.map((c) => c.durationMs),
          position,
          sync.positionMs
        )}
        suspended={!follow.following}
        onToggle={sync.toggle}
        onSkip={sync.skip}
        onSpeed={(direction) => sync.changeSpeed(direction * SPEED_STEP)}
        onBackToAudio={backToAudio}
      />
      <footer className="reader-controls">
        <button type="button" disabled={prevChapter === undefined} onClick={() => selectChapter(prevChapter)}>
          Capítulo anterior
        </button>
        <button type="button" disabled={!pageReady} onClick={() => manualGo(start - layout.ppv)}>
          Página anterior
        </button>
        <PageIndicator page={shown} count={layout.count} ppv={layout.ppv} />
        <button type="button" disabled={!pageReady} onClick={() => manualGo(start + layout.ppv)}>
          Página siguiente
        </button>
        <button type="button" disabled={nextChapter === undefined} onClick={() => selectChapter(nextChapter)}>
          Capítulo siguiente
        </button>
      </footer>
    </div>
  );
}
