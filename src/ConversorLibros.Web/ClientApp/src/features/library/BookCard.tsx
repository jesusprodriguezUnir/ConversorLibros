import { Link } from "react-router-dom";
import { FolderOpen, Clock, Layers, Download, BookOpen } from "lucide-react";
import { api } from "../../shared/api/client";
import type { LibraryItemDto } from "../../shared/api/types";
import { Badge } from "../../shared/components/Badge";
import { Button } from "../../shared/components/Button";
import { formatDuration, getBookInitials } from "../../shared/utils/format";

interface BookCardProps {
  book: LibraryItemDto;
  onOpen: (id: string) => void;
  opening?: boolean;
}

export function BookCard({ book, onOpen, opening = false }: BookCardProps) {
  const duration = formatDuration(book.durationSeconds);

  return (
    <article className="group bg-[#131720] border border-[#252C3D] hover:border-[#3A4660] rounded-2xl overflow-hidden flex flex-col transition-all duration-200 hover:-translate-y-0.5 shadow-lg shadow-black/20">
      <div className="relative aspect-[3/4] bg-gradient-to-br from-[#1B2233] via-[#131824] to-[#0D1018] overflow-hidden border-b border-[#252C3D]">
        {book.hasCover && book.coverUrl ? (
          <img
            src={book.coverUrl}
            alt={`Portada de ${book.title}`}
            className="w-full h-full object-cover transition-transform duration-300 group-hover:scale-[1.03]"
            loading="lazy"
          />
        ) : (
          <div className="w-full h-full flex flex-col items-center justify-center p-6 text-center relative">
            <div className="absolute inset-0 bg-[radial-gradient(circle_at_30%_20%,rgba(245,158,11,0.14),transparent_60%)]" />
            <div className="w-16 h-16 rounded-2xl bg-[#1B2130] border border-[#2F3952] flex items-center justify-center mb-2 shadow-inner">
              <span className="text-xl font-bold tracking-wider text-[#FBBF24]">
                {getBookInitials(book.title)}
              </span>
            </div>
            <span className="text-[11px] uppercase tracking-widest text-[#64728C]">
              Audiolibro
            </span>
          </div>
        )}

        <div className="absolute top-3 left-3">
          <Badge tone={book.format === "opus" ? "accent" : "neutral"} className="backdrop-blur-md">
            {book.format.toUpperCase()}
          </Badge>
        </div>
      </div>

      <div className="p-4 flex flex-col flex-1 justify-between gap-4">
        <div>
          <h3 className="text-sm font-semibold text-[#EDF0F7] line-clamp-2 leading-snug">
            {book.title}
          </h3>
          <p className="text-xs text-[#93A0B8] mt-1 truncate">{book.author}</p>

          <div className="flex flex-wrap items-center gap-x-3 gap-y-1 mt-3 text-xs text-[#64728C]">
            <span className="inline-flex items-center gap-1">
              <Layers className="w-3.5 h-3.5" />
              {book.format === "opus"
                ? `${book.chapterCount} ${book.chapterCount === 1 ? "capítulo" : "capítulos"}`
                : "Fichero único"}
            </span>
            {duration && (
              <span className="inline-flex items-center gap-1 font-mono">
                <Clock className="w-3.5 h-3.5" />
                {duration}
              </span>
            )}
          </div>
        </div>

        <div className="flex flex-col gap-2">
          {book.hasReader ? (
            <Link
              to={`/biblioteca/${encodeURIComponent(book.id)}/leer`}
              className="inline-flex items-center justify-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-medium border border-[#252C3D] bg-[#1B2130] text-[#EDF0F7] hover:border-[#3A4660] transition-colors"
            >
              <BookOpen className="w-3.5 h-3.5" />
              Leer
            </Link>
          ) : (
            <>
              <Button variant="secondary" size="sm" disabled icon={<BookOpen className="w-3.5 h-3.5" />}>
                Leer
              </Button>
              <p className="text-[11px] text-[#64728C]">Reconvierte el libro para habilitar el lector</p>
            </>
          )}
        <div className="flex gap-2">
          <Button
            variant="secondary"
            size="sm"
            className="flex-1"
            loading={opening}
            icon={<FolderOpen className="w-3.5 h-3.5" />}
            onClick={() => onOpen(book.id)}
          >
            Abrir carpeta
          </Button>
          <a
            href={api.libraryDownloadUrl(book.id)}
            download
            title={book.format === "opus" ? "Descarga un ZIP con los capítulos y la portada" : "Descarga el archivo M4B"}
            className="inline-flex flex-1 items-center justify-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-medium border border-[#252C3D] bg-[#1B2130] text-[#EDF0F7] hover:border-[#3A4660] transition-colors"
          >
            <Download className="w-3.5 h-3.5" />
            Descargar
          </a>
        </div>
        </div>
      </div>
    </article>
  );
}
