import { useCallback, useEffect, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { api } from "../../shared/api/client";
import type { ReaderChapterDto, ReaderIndexDto } from "../../shared/api/types";
import { CoverPage } from "./CoverPage";
import { PageIndicator } from "./PageIndicator";
import { PaginatedChapter } from "./PaginatedChapter";
import { ReaderToc } from "./ReaderToc";
import { clampPage, spreadStart } from "./pagination";
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
  const [layout, setLayout] = useState<{ count: number; ppv: 1 | 2 }>({ count: 1, ppv: 1 });
  const onLayout = useCallback((l: { count: number; ppv: 1 | 2 }) => setLayout(l), []);

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
  const selectChapter = (chapterIndex: number) => {
    setCurrent(chapterIndex);
    setPage(0);
  };
  const position = index.chapters.findIndex((c) => c.index === current);
  const shown = clampPage(page, layout.count);
  const start = spreadStart(shown, layout.ppv);
  const go = (p: number) => setPage(clampPage(p, layout.count));
  const prevChapter = index.chapters[position - 1]?.index;
  const nextChapter = index.chapters[position + 1]?.index;
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
            onLayout={onLayout}
            cover={position === 0 && index.hasCover ? <CoverPage bookId={id} title={index.title} /> : null}
          />
        )}
      </main>
      <footer className="reader-controls">
        <button type="button" disabled={prevChapter === undefined} onClick={() => selectChapter(prevChapter)}>
          Capítulo anterior
        </button>
        <button type="button" disabled={!pageReady} onClick={() => go(start - layout.ppv)}>
          Página anterior
        </button>
        <PageIndicator page={shown} count={layout.count} ppv={layout.ppv} />
        <button type="button" disabled={!pageReady} onClick={() => go(start + layout.ppv)}>
          Página siguiente
        </button>
        <button type="button" disabled={nextChapter === undefined} onClick={() => selectChapter(nextChapter)}>
          Capítulo siguiente
        </button>
      </footer>
    </div>
  );
}
