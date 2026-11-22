import { useMemo, useState } from "react";
import {
  ArrowLeftRight,
  CheckSquare,
  ChevronDown,
  ChevronUp,
  ListX,
  Search,
  Square,
} from "lucide-react";
import { api } from "../../shared/api/client";
import type { ChapterPreviewDto, ChapterSummaryDto } from "../../shared/api/types";
import { Badge } from "../../shared/components/Badge";
import { Button } from "../../shared/components/Button";
import { Card } from "../../shared/components/Card";
import { formatSpanishDuration } from "../../shared/utils/format";

type PreviewState =
  | { status: "loading" }
  | { status: "ready"; preview: ChapterPreviewDto }
  | { status: "error"; message: string };

interface ChapterListProps {
  uploadId: string;
  chapters: ChapterSummaryDto[];
  selected: Set<number>;
  disabled?: boolean;
  onToggle: (index: number) => void;
  onSelectAll: () => void;
  onSelectNone: () => void;
  onInvert: () => void;
  onDeselectFrontMatter: () => void;
}

export function ChapterList({
  uploadId,
  chapters,
  selected,
  disabled = false,
  onToggle,
  onSelectAll,
  onSelectNone,
  onInvert,
  onDeselectFrontMatter,
}: ChapterListProps) {
  const [filter, setFilter] = useState("");
  const [expanded, setExpanded] = useState<Set<number>>(new Set());
  const [previews, setPreviews] = useState<Record<number, PreviewState>>({});

  const visibleChapters = useMemo(() => {
    const q = filter.trim().toLowerCase();
    if (!q) return chapters;
    return chapters.filter(
      (c) => c.title.toLowerCase().includes(q) || String(c.index).includes(q)
    );
  }, [chapters, filter]);

  const selectedChapters = chapters.filter((c) => selected.has(c.index));
  const selectedWords = selectedChapters.reduce((sum, c) => sum + c.wordCount, 0);
  const selectedSeconds = selectedChapters.reduce((sum, c) => sum + c.estimatedSeconds, 0);
  const hasFrontMatterSelected = selectedChapters.some((c) => c.isLikelyFrontMatter);

  const toggleExpanded = (index: number) => {
    setExpanded((prev) => {
      const next = new Set(prev);
      if (next.has(index)) {
        next.delete(index);
      } else {
        next.add(index);
      }
      return next;
    });

    const current = previews[index];
    if (current && current.status !== "error") return;

    setPreviews((prev) => ({ ...prev, [index]: { status: "loading" } }));
    api
      .getChapterPreview(uploadId, index)
      .then((preview) =>
        setPreviews((prev) => ({ ...prev, [index]: { status: "ready", preview } }))
      )
      .catch((err: unknown) =>
        setPreviews((prev) => ({
          ...prev,
          [index]: {
            status: "error",
            message: err instanceof Error ? err.message : "No se pudo cargar el fragmento.",
          },
        }))
      );
  };

  const renderPreview = (chapter: ChapterSummaryDto) => {
    const state = previews[chapter.index];
    if (!state || state.status === "loading") {
      return (
        <p>
          {chapter.excerpt && <span className="text-[#93A0B8]">{chapter.excerpt} </span>}
          <span className="text-[#64728C]">Cargando fragmento…</span>
        </p>
      );
    }
    if (state.status === "error") {
      return <p className="text-red-300">{state.message}</p>;
    }
    return (
      <>
        <p className="whitespace-pre-line max-h-64 overflow-y-auto text-[#93A0B8]">
          {state.preview.text}
        </p>
        {state.preview.truncated && (
          <p className="mt-2 text-[#64728C]">
            Fragmento recortado. El audio incluirá el capítulo completo.
          </p>
        )}
      </>
    );
  };

  return (
    <Card className="flex flex-col">
      <div className="flex flex-wrap items-start justify-between gap-3 mb-4">
        <div>
          <h3 className="text-sm font-semibold text-[#EDF0F7]">Capítulos</h3>
          <p className="text-xs text-[#93A0B8] mt-0.5 font-mono">
            {selected.size} de {chapters.length} seleccionados
          </p>
          <p className="text-[11px] text-[#64728C] mt-0.5">
            ~{formatSpanishDuration(selectedSeconds)} · {selectedWords.toLocaleString("es-ES")}{" "}
            palabras
          </p>
        </div>

        <div className="flex flex-wrap items-center gap-2">
          <Button
            variant="ghost"
            size="sm"
            disabled={disabled || chapters.length === 0}
            icon={<CheckSquare className="w-3.5 h-3.5" />}
            onClick={onSelectAll}
          >
            Todos
          </Button>
          <Button
            variant="ghost"
            size="sm"
            disabled={disabled || chapters.length === 0}
            icon={<Square className="w-3.5 h-3.5" />}
            onClick={onSelectNone}
          >
            Ninguno
          </Button>
          <Button
            variant="ghost"
            size="sm"
            disabled={disabled || chapters.length === 0}
            icon={<ArrowLeftRight className="w-3.5 h-3.5" />}
            onClick={onInvert}
          >
            Invertir
          </Button>
          <Button
            variant="ghost"
            size="sm"
            disabled={disabled || !hasFrontMatterSelected}
            icon={<ListX className="w-3.5 h-3.5" />}
            onClick={onDeselectFrontMatter}
          >
            Quitar preliminares
          </Button>
        </div>
      </div>

      {chapters.length > 5 && (
        <div className="relative mb-3">
          <Search className="w-3.5 h-3.5 text-[#64728C] absolute left-3 top-1/2 -translate-y-1/2" />
          <input
            type="text"
            value={filter}
            onChange={(e) => setFilter(e.target.value)}
            placeholder="Filtrar capítulos…"
            className="w-full pl-9 pr-3 py-1.5 rounded-lg bg-[#0E1118] border border-[#252C3D] focus:border-[#F59E0B] focus:outline-none text-xs text-[#EDF0F7] placeholder:text-[#64728C]"
          />
        </div>
      )}

      {chapters.length === 0 ? (
        <div className="py-10 text-center text-xs text-[#64728C] border border-dashed border-[#252C3D] rounded-xl">
          Carga un libro EPUB para elegir los capítulos a convertir.
        </div>
      ) : (
        <ul className="max-h-[420px] overflow-y-auto divide-y divide-[#1E2434] border border-[#252C3D] rounded-xl bg-[#0E1118]">
          {visibleChapters.map((chapter) => {
            const isChecked = selected.has(chapter.index);
            const isOpen = expanded.has(chapter.index);
            const panelId = `chapter-panel-${chapter.index}`;
            return (
              <li key={chapter.index}>
                <div
                  className={`flex items-center gap-3 px-3.5 py-2.5 text-sm transition-colors ${
                    disabled ? "opacity-60" : "hover:bg-[#161B26]"
                  }`}
                >
                  <input
                    type="checkbox"
                    aria-label={`Seleccionar ${chapter.title}`}
                    checked={isChecked}
                    disabled={disabled}
                    onChange={() => onToggle(chapter.index)}
                    className="w-4 h-4 rounded accent-[#F59E0B] cursor-pointer shrink-0"
                  />
                  <span className="font-mono text-xs text-[#64728C] w-7 shrink-0">
                    {String(chapter.index).padStart(2, "0")}
                  </span>
                  <span className="flex-1 min-w-0 truncate text-[#EDF0F7]">{chapter.title}</span>
                  {chapter.isLikelyFrontMatter && <Badge tone="neutral">Preliminar</Badge>}
                  <span className="hidden sm:inline font-mono text-[11px] text-[#64728C] shrink-0">
                    ~{formatSpanishDuration(chapter.estimatedSeconds)}
                  </span>
                  <button
                    type="button"
                    aria-expanded={isOpen}
                    aria-controls={panelId}
                    aria-label={`${isOpen ? "Ocultar" : "Ver"} fragmento de ${chapter.title}`}
                    onClick={() => toggleExpanded(chapter.index)}
                    className="p-1 rounded text-[#64728C] hover:text-[#EDF0F7] shrink-0"
                  >
                    {isOpen ? <ChevronUp className="w-4 h-4" /> : <ChevronDown className="w-4 h-4" />}
                  </button>
                </div>
                {isOpen && (
                  <div
                    id={panelId}
                    role="region"
                    aria-label={`Fragmento de ${chapter.title}`}
                    className="px-12 pb-3 text-xs leading-relaxed"
                  >
                    {renderPreview(chapter)}
                  </div>
                )}
              </li>
            );
          })}
        </ul>
      )}
    </Card>
  );
}
