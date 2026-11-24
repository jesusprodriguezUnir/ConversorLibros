import { BookOpen, Headphones } from "lucide-react";
import type { CatalogBookDto } from "../../shared/api/types";

interface LibraryTableViewProps {
  books: CatalogBookDto[];
  selectedId: string | null;
  onSelect: (book: CatalogBookDto) => void;
  onOpenReader: (book: CatalogBookDto) => void;
}

export function LibraryTableView({
  books,
  selectedId,
  onSelect,
  onOpenReader,
}: LibraryTableViewProps) {
  return (
    <div className="rounded-2xl bg-[#131720] border border-[#252C3D] overflow-hidden">
      <div className="overflow-x-auto">
        <table className="w-full text-left border-collapse text-sm">
          <thead>
            <tr className="border-b border-[#252C3D] text-[11px] uppercase tracking-wider text-[#93A0B8] bg-[#0F131B]">
              <th className="py-2.5 px-3 font-semibold">Título</th>
              <th className="py-2.5 px-3 font-semibold">Autor</th>
              <th className="py-2.5 px-3 font-semibold">Serie</th>
              <th className="py-2.5 px-3 font-semibold">Estado</th>
              <th className="py-2.5 px-3 font-semibold text-right">Progreso</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-[#1E2433]">
            {books.map((book) => {
              const selected = book.id === selectedId;
              return (
                <tr
                  key={book.id}
                  onClick={() => onSelect(book)}
                  onDoubleClick={() => {
                    if (book.hasReader) onOpenReader(book);
                  }}
                  className={`cursor-pointer transition-colors ${
                    selected
                      ? "bg-[#F59E0B]/12 text-[#EDF0F7]"
                      : "hover:bg-[#1B2130]/70 text-[#CBD5E1]"
                  }`}
                >
                  <td className="py-2.5 px-3 font-medium text-[#EDF0F7] max-w-[260px]">
                    <div className="flex items-center gap-2.5 min-w-0">
                      {book.coverUrl ? (
                        <img
                          src={book.coverUrl}
                          alt=""
                          className="w-7 h-10 object-cover rounded shrink-0 bg-[#1B2130]"
                          loading="lazy"
                        />
                      ) : (
                        <div className="w-7 h-10 rounded bg-[#1B2130] border border-[#2A3347] flex items-center justify-center shrink-0">
                          <BookOpen className="w-3.5 h-3.5 text-[#64728C]" />
                        </div>
                      )}
                      <span className="truncate">{book.title}</span>
                    </div>
                  </td>
                  <td className="py-2.5 px-3 text-[#93A0B8] truncate max-w-[180px]">{book.author}</td>
                  <td className="py-2.5 px-3 text-[#93A0B8] truncate max-w-[160px]">
                    {book.series
                      ? `${book.series}${book.seriesIndex != null ? ` #${book.seriesIndex}` : ""}`
                      : "—"}
                  </td>
                  <td className="py-2.5 px-3">
                    {book.audioStatus === "ready" ? (
                      <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-medium bg-emerald-500/15 text-emerald-300">
                        <Headphones className="w-3 h-3" />
                        {(book.audioFormat ?? "audio").toUpperCase()}
                      </span>
                    ) : book.audioStatus === "converting" || book.audioStatus === "queued" ? (
                      <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-medium bg-amber-500/15 text-amber-300">
                        En curso
                      </span>
                    ) : (
                      <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-medium bg-[#1B2130] text-[#93A0B8]">
                        <BookOpen className="w-3 h-3" />
                        EPUB
                      </span>
                    )}
                  </td>
                  <td className="py-2.5 px-3 text-right text-xs text-[#93A0B8] tabular-nums">
                    {book.readingPercent > 0 ? `${Math.round(book.readingPercent)}%` : "—"}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </div>
  );
}
