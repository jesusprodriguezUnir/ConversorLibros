import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { AlertTriangle } from "lucide-react";
import { api } from "../../shared/api/client";
import type { InspectedBookDto, SettingsResponse } from "../../shared/api/types";
import { useToast } from "../../shared/components/Toast";
import { useConversion } from "../../app/ConversionContext";
import { EpubDropzone } from "./EpubDropzone";
import { BookDetailsCard } from "./BookDetailsCard";
import { ChapterList } from "./ChapterList";
import { ConversionOptions } from "./ConversionOptions";
import { ProgressPanel } from "./ProgressPanel";

export function NewConversionPage() {
  const { showToast } = useToast();
  const { state, isRunning, startConversion, cancelConversion } = useConversion();

  const [settings, setSettings] = useState<SettingsResponse | null>(null);
  const [book, setBook] = useState<InspectedBookDto | null>(null);
  const [inspecting, setInspecting] = useState(false);
  const [selectedChapters, setSelectedChapters] = useState<Set<number>>(new Set());

  const [speed, setSpeed] = useState(1.0);
  const [translate, setTranslate] = useState(false);
  const [sourceLanguage, setSourceLanguage] = useState("en");
  const [format, setFormat] = useState<"opus" | "m4b">("opus");

  useEffect(() => {
    api
      .getSettings()
      .then((res) => {
        setSettings(res);
        setSpeed(res.settings.defaultSpeed);
        setFormat(res.settings.defaultFormat);
      })
      .catch((err) => {
        showToast(
          err instanceof Error
            ? `No se pudieron cargar los ajustes: ${err.message}`
            : "No se pudieron cargar los ajustes.",
          "error"
        );
      });
  }, [showToast]);

  // Mirrors the backend layout: final audio lives in <OutputRoot>/<folderName>/, work files in work/.
  const outputPreview = (() => {
    if (!settings || !book) return null;
    const root = settings.settings.outputRoot.replace(/[\\/]+$/, "");
    const sep = root.includes("\\") ? "\\" : "/";
    const bookDir = `${root}${sep}${book.folderName}`;
    return format === "m4b" ? `${bookDir}${sep}${book.folderName}.m4b` : `${bookDir}${sep}`;
  })();

  const handleSelectFile = async (file: File) => {
    setInspecting(true);
    try {
      const inspected = await api.inspectBook(file);
      setBook(inspected);
      setSelectedChapters(new Set(inspected.chapters.map((c) => c.index)));
      if (inspected.language) {
        setSourceLanguage(inspected.language);
      }
      showToast(`Libro cargado: ${inspected.chapters.length} capítulo(s).`, "success");
    } catch (err) {
      showToast(err instanceof Error ? err.message : "No se pudo abrir el EPUB.", "error");
    } finally {
      setInspecting(false);
    }
  };

  const handleToggleChapter = (index: number) => {
    setSelectedChapters((prev) => {
      const next = new Set(prev);
      if (next.has(index)) {
        next.delete(index);
      } else {
        next.add(index);
      }
      return next;
    });
  };

  const handleSelectAll = () => {
    if (!book) return;
    setSelectedChapters(new Set(book.chapters.map((c) => c.index)));
  };

  const handleSelectNone = () => {
    setSelectedChapters(new Set());
  };

  const handleInvert = () => {
    if (!book) return;
    setSelectedChapters((prev) => {
      const next = new Set(book.chapters.filter((c) => !prev.has(c.index)).map((c) => c.index));
      return next;
    });
  };

  const handleDeselectFrontMatter = () => {
    if (!book) return;
    const frontMatter = new Set(book.chapters.filter((c) => c.isLikelyFrontMatter).map((c) => c.index));
    setSelectedChapters((prev) => new Set(Array.from(prev).filter((index) => !frontMatter.has(index))));
  };

  const canStart =
    !isRunning &&
    !inspecting &&
    book !== null &&
    selectedChapters.size > 0 &&
    (settings ? settings.piper.isValid : true);

  const handleStart = async () => {
    if (!book || selectedChapters.size === 0) return;
    try {
      await startConversion({
        uploadId: book.uploadId,
        chapterIndexes: Array.from(selectedChapters).sort((a, b) => a - b),
        speed,
        translate,
        sourceLanguage: translate ? sourceLanguage.trim() || "en" : null,
        format,
      });
    } catch (err) {
      showToast(err instanceof Error ? err.message : "No se pudo iniciar la conversión.", "error");
    }
  };

  const handleCancel = async () => {
    try {
      await cancelConversion();
    } catch (err) {
      showToast(err instanceof Error ? err.message : "No se pudo cancelar.", "error");
    }
  };

  return (
    <div className="space-y-6">
      <header>
        <h1 className="text-2xl font-bold tracking-tight text-[#EDF0F7]">Nueva conversión</h1>
        <p className="text-sm text-[#93A0B8] mt-1">
          Carga un EPUB sin DRM, elige los capítulos y genera tu audiolibro en español.
        </p>
      </header>

      {settings && !settings.piper.isValid && (
        <div className="p-4 rounded-2xl bg-amber-500/10 border border-amber-500/35 flex flex-col sm:flex-row sm:items-center justify-between gap-3">
          <div className="flex items-start gap-3">
            <AlertTriangle className="w-5 h-5 text-amber-400 shrink-0 mt-0.5" />
            <div>
              <p className="text-sm font-semibold text-amber-200">
                Modelo Piper no configurado
              </p>
              <p className="text-xs text-amber-200/80 mt-0.5">{settings.piper.message}</p>
            </div>
          </div>
          <Link
            to="/ajustes"
            className="px-3.5 py-2 rounded-lg text-xs font-semibold bg-[#F59E0B] text-[#0B0D12] hover:bg-[#FBBF24] transition-colors shrink-0 text-center"
          >
            Ir a Ajustes
          </Link>
        </div>
      )}

      <div className="grid grid-cols-1 lg:grid-cols-12 gap-6 items-start">
        <div className="lg:col-span-7 space-y-6">
          <EpubDropzone
            book={book}
            loading={inspecting}
            disabled={isRunning}
            onSelectFile={handleSelectFile}
          />

          {book && <BookDetailsCard book={book} />}

          <ChapterList
            key={book?.uploadId ?? "no-book"}
            uploadId={book?.uploadId ?? ""}
            chapters={book?.chapters ?? []}
            selected={selectedChapters}
            disabled={isRunning}
            onToggle={handleToggleChapter}
            onSelectAll={handleSelectAll}
            onSelectNone={handleSelectNone}
            onInvert={handleInvert}
            onDeselectFrontMatter={handleDeselectFrontMatter}
          />
        </div>

        <div className="lg:col-span-5 space-y-6">
          <ConversionOptions
            speed={speed}
            translate={translate}
            sourceLanguage={sourceLanguage}
            format={format}
            disabled={isRunning}
            onSpeedChange={setSpeed}
            onTranslateChange={setTranslate}
            onSourceLanguageChange={setSourceLanguage}
            onFormatChange={setFormat}
          />

          {outputPreview && (
            <p className="text-xs text-[#93A0B8] break-all">
              Salida: <span className="font-mono text-[#EDF0F7]">{outputPreview}</span>
            </p>
          )}

          <ProgressPanel
            state={state}
            canStart={canStart}
            isRunning={isRunning}
            onStart={() => void handleStart()}
            onCancel={() => void handleCancel()}
          />
        </div>
      </div>
    </div>
  );
}
