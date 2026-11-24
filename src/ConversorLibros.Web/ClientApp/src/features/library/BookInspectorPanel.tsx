import { BookOpen, Download, FolderOpen, Headphones, Pencil, Sparkles, Trash2 } from "lucide-react";
import { Link } from "react-router-dom";
import { api } from "../../shared/api/client";
import type { CatalogBookDto } from "../../shared/api/types";
import { Button } from "../../shared/components/Button";

interface BookInspectorPanelProps {
  book: CatalogBookDto | null;
  onEdit: (book: CatalogBookDto) => void;
  onOpenFolder: (book: CatalogBookDto) => void;
  onDelete: (book: CatalogBookDto) => void;
  opening: boolean;
  deleting: boolean;
}

export function BookInspectorPanel({
  book,
  onEdit,
  onOpenFolder,
  onDelete,
  opening,
  deleting,
}: BookInspectorPanelProps) {
  if (!book) {
    return (
      <aside className="rounded-2xl bg-[#131720] border border-[#252C3D] p-5 text-center h-fit">
        <BookOpen className="w-8 h-8 text-[#64728C] mx-auto mb-2" />
        <p className="text-sm text-[#93A0B8]">Selecciona un libro para ver sus detalles y acciones.</p>
      </aside>
    );
  }

  return (
    <aside className="rounded-2xl bg-[#131720] border border-[#252C3D] p-4 space-y-4 h-fit">
      <div className="aspect-[3/4] max-w-[190px] mx-auto rounded-xl overflow-hidden bg-[#1B2130] border border-[#2A3347] flex items-center justify-center">
        {book.coverUrl ? (
          <img
            src={book.coverUrl}
            alt={`Portada de ${book.title}`}
            className="w-full h-full object-cover"
          />
        ) : (
          <BookOpen className="w-12 h-12 text-[#64728C]" />
        )}
      </div>

      <div className="space-y-1">
        <h2 className="text-base font-bold text-[#EDF0F7] leading-snug">{book.title}</h2>
        <p className="text-sm text-[#93A0B8]">{book.author}</p>
        {book.series && (
          <p className="text-xs text-[#F59E0B]">
            {book.series}
            {book.seriesIndex != null ? ` #${book.seriesIndex}` : ""}
          </p>
        )}
      </div>

      {book.tags.length > 0 && (
        <div className="flex flex-wrap gap-1.5">
          {book.tags.map((tag) => (
            <span
              key={tag}
              className="px-2 py-0.5 rounded-md text-[11px] bg-[#1B2130] text-[#CBD5E1] border border-[#2A3347]"
            >
              {tag}
            </span>
          ))}
        </div>
      )}

      {book.readingPercent > 0 && (
        <div className="space-y-1">
          <div className="flex justify-between text-[11px] text-[#93A0B8]">
            <span>Progreso de lectura</span>
            <span>{Math.round(book.readingPercent)}%</span>
          </div>
          <div className="h-1.5 rounded-full bg-[#1B2130] overflow-hidden">
            <div
              className="h-full bg-[#F59E0B]"
              style={{ width: `${Math.min(100, Math.max(0, book.readingPercent))}%` }}
            />
          </div>
        </div>
      )}

      <div className="flex flex-col gap-2">
        {book.hasReader ? (
          <Link
            to={`/biblioteca/${encodeURIComponent(book.id)}/leer`}
            className="w-full inline-flex items-center justify-center gap-2 px-4 py-2 rounded-lg text-sm font-semibold bg-[#F59E0B] text-[#0B0D12] hover:bg-[#FBBF24] transition-colors"
          >
            <BookOpen className="w-4 h-4" />
            {book.audioStatus === "ready" ? "Leer y escuchar" : "Leer"}
          </Link>
        ) : (
          <button
            type="button"
            disabled
            title="Sin texto sincronizado; descargable o reproducible externamente"
            className="w-full inline-flex items-center justify-center gap-2 px-4 py-2 rounded-lg text-sm font-semibold bg-[#1B2130] text-[#64728C] cursor-not-allowed"
          >
            <BookOpen className="w-4 h-4" />
            Leer no disponible
          </button>
        )}

        <Link
          to="/convertir"
          className="w-full inline-flex items-center justify-center gap-2 px-4 py-2 rounded-lg text-sm font-medium bg-[#1B2130] text-[#EDF0F7] border border-[#2A3347] hover:border-[#F59E0B]/60 transition-colors"
        >
          <Sparkles className="w-4 h-4 text-[#F59E0B]" />
          {book.audioStatus === "ready" ? "Reconvertir audiolibro" : "Convertir a audiolibro"}
        </Link>
      </div>

      <div className="grid grid-cols-2 gap-2 pt-1 border-t border-[#252C3D]">
        <Button
          variant="secondary"
          size="sm"
          icon={<Pencil className="w-3.5 h-3.5" />}
          onClick={() => onEdit(book)}
        >
          Editar
        </Button>

        <Button
          variant="secondary"
          size="sm"
          loading={opening}
          icon={<FolderOpen className="w-3.5 h-3.5" />}
          onClick={() => onOpenFolder(book)}
        >
          Carpeta
        </Button>

        {book.audioStatus === "ready" && (
          <a
            href={api.libraryDownloadUrl(book.id)}
            download
            className="inline-flex items-center justify-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-medium bg-[#1B2130] text-[#EDF0F7] border border-[#2A3347] hover:bg-[#252C3D] transition-colors"
          >
            <Download className="w-3.5 h-3.5" />
            Descargar
          </a>
        )}

        <Button
          variant="ghost"
          size="sm"
          loading={deleting}
          icon={<Trash2 className="w-3.5 h-3.5 text-rose-400" />}
          onClick={() => onDelete(book)}
          className="text-rose-300 hover:text-rose-200"
        >
          Quitar
        </Button>
      </div>

      <dl className="text-xs space-y-1.5 pt-2 border-t border-[#252C3D] text-[#93A0B8]">
        {book.chapterCount > 0 && (
          <div className="flex justify-between">
            <dt>Capítulos</dt>
            <dd className="text-[#EDF0F7]">{book.chapterCount}</dd>
          </div>
        )}
        {book.audioStatus === "ready" && book.audioFormat && (
          <div className="flex justify-between">
            <dt className="inline-flex items-center gap-1">
              <Headphones className="w-3 h-3" /> Audio
            </dt>
            <dd className="text-emerald-300 uppercase">{book.audioFormat}</dd>
          </div>
        )}
        {book.publisher && (
          <div className="flex justify-between gap-2">
            <dt>Editorial</dt>
            <dd className="text-[#EDF0F7] truncate">{book.publisher}</dd>
          </div>
        )}
        {book.isbn && (
          <div className="flex justify-between gap-2">
            <dt>ISBN</dt>
            <dd className="text-[#EDF0F7] truncate">{book.isbn}</dd>
          </div>
        )}
        {book.epubPath && (
          <div className="pt-1">
            <dt className="text-[11px] uppercase tracking-wider text-[#64728C]">Archivo</dt>
            <dd className="text-[11px] text-[#93A0B8] break-all mt-0.5" title={book.epubPath}>
              {book.epubPath}
            </dd>
          </div>
        )}
      </dl>

      {book.description && (
        <div className="pt-2 border-t border-[#252C3D]">
          <h3 className="text-[11px] font-semibold uppercase tracking-wider text-[#64728C] mb-1">
            Sinopsis
          </h3>
          <p className="text-xs text-[#CBD5E1] leading-relaxed max-h-40 overflow-y-auto whitespace-pre-line">
            {book.description}
          </p>
        </div>
      )}
    </aside>
  );
}
