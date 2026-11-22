import { useMemo, useState } from "react";
import { Search, CheckSquare, Square } from "lucide-react";
import type { ChapterSummaryDto } from "../../shared/api/types";
import { Button } from "../../shared/components/Button";
import { Card } from "../../shared/components/Card";
import { formatCharEstimate } from "../../shared/utils/format";

interface ChapterListProps {
  chapters: ChapterSummaryDto[];
  selected: Set<number>;
  disabled?: boolean;
  onToggle: (index: number) => void;
  onSelectAll: () => void;
  onSelectNone: () => void;
}

export function ChapterList({
  chapters,
  selected,
  disabled = false,
  onToggle,
  onSelectAll,
  onSelectNone,
}: ChapterListProps) {
  const [filter, setFilter] = useState("");

  const visibleChapters = useMemo(() => {
    const q = filter.trim().toLowerCase();
    if (!q) return chapters;
    return chapters.filter(
      (c) => c.title.toLowerCase().includes(q) || String(c.index).includes(q)
    );
  }, [chapters, filter]);

  return (
    <Card className="flex flex-col">
      <div className="flex flex-wrap items-center justify-between gap-3 mb-4">
        <div>
          <h3 className="text-sm font-semibold text-[#EDF0F7]">Capítulos</h3>
          <p className="text-xs text-[#93A0B8] mt-0.5 font-mono">
            {selected.size} de {chapters.length} seleccionados
          </p>
        </div>

        <div className="flex items-center gap-2">
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
        <div className="max-h-[420px] overflow-y-auto divide-y divide-[#1E2434] border border-[#252C3D] rounded-xl bg-[#0E1118]">
          {visibleChapters.map((chapter) => {
            const isChecked = selected.has(chapter.index);
            return (
              <label
                key={chapter.index}
                className={`flex items-center gap-3 px-3.5 py-2.5 text-sm transition-colors ${
                  disabled ? "opacity-60 cursor-not-allowed" : "hover:bg-[#161B26] cursor-pointer"
                }`}
              >
                <input
                  type="checkbox"
                  checked={isChecked}
                  disabled={disabled}
                  onChange={() => onToggle(chapter.index)}
                  className="w-4 h-4 rounded accent-[#F59E0B] cursor-pointer"
                />
                <span className="font-mono text-xs text-[#64728C] w-7 shrink-0">
                  {String(chapter.index).padStart(2, "0")}
                </span>
                <span className="flex-1 truncate text-[#EDF0F7]">{chapter.title}</span>
                <span className="font-mono text-[11px] text-[#64728C] shrink-0">
                  {formatCharEstimate(chapter.charCount)}
                </span>
              </label>
            );
          })}
        </div>
      )}
    </Card>
  );
}
