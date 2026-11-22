import { BookOpen, Clock, FileText } from "lucide-react";
import type { InspectedBookDto } from "../../shared/api/types";
import { Badge } from "../../shared/components/Badge";
import { Card } from "../../shared/components/Card";
import { formatSpanishDuration, getBookInitials } from "../../shared/utils/format";

interface BookDetailsCardProps {
  book: InspectedBookDto;
}

export function BookDetailsCard({ book }: BookDetailsCardProps) {
  const metadata = book.metadata;
  const seriesLabel =
    metadata?.series && metadata.seriesIndex !== null
      ? `${metadata.series} #${metadata.seriesIndex}`
      : metadata?.series ?? null;

  const facts: { label: string; value: string }[] = [];
  if (metadata?.publisher) facts.push({ label: "Editorial", value: metadata.publisher });
  if (metadata?.publishedDate) facts.push({ label: "Publicación", value: metadata.publishedDate });
  if (metadata?.isbn) facts.push({ label: "ISBN", value: metadata.isbn });
  if (seriesLabel) facts.push({ label: "Serie", value: seriesLabel });

  return (
    <Card>
      <div className="flex flex-col sm:flex-row gap-5">
        <div className="w-32 h-48 rounded-xl overflow-hidden bg-[#1B2130] border border-[#2A3347] shrink-0 flex items-center justify-center self-start">
          {book.hasCover && book.coverUrl ? (
            <img
              src={book.coverUrl}
              alt={`Portada de ${book.title}`}
              className="w-full h-full object-cover"
            />
          ) : (
            <span className="text-2xl font-bold text-[#FBBF24]">{getBookInitials(book.title)}</span>
          )}
        </div>

        <div className="flex-1 min-w-0 space-y-3">
          <div>
            <h2 className="text-lg font-semibold text-[#EDF0F7] break-words">{book.title}</h2>
            <p className="text-sm text-[#93A0B8]">{book.author}</p>
          </div>

          <div className="flex flex-wrap items-center gap-2">
            <Badge tone="accent">EPUB</Badge>
            <Badge tone="neutral" className="font-mono uppercase">
              {book.language}
            </Badge>
            <Badge tone="neutral">
              <BookOpen className="w-3 h-3" />
              {book.chapters.length} {book.chapters.length === 1 ? "capítulo" : "capítulos"}
            </Badge>
            <Badge tone="neutral">
              <FileText className="w-3 h-3" />
              {book.totalWordCount.toLocaleString("es-ES")} palabras
            </Badge>
            <Badge tone="neutral">
              <Clock className="w-3 h-3" />
              ~{formatSpanishDuration(book.totalEstimatedSeconds)}
            </Badge>
          </div>

          {facts.length > 0 && (
            <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1 text-xs">
              {facts.map((fact) => (
                <div key={fact.label} className="contents">
                  <dt className="text-[#64728C]">{fact.label}</dt>
                  <dd className="text-[#EDF0F7] break-words">{fact.value}</dd>
                </div>
              ))}
            </dl>
          )}

          {metadata?.subjects && metadata.subjects.length > 0 && (
            <div className="flex flex-wrap gap-1.5">
              {metadata.subjects.map((subject) => (
                <Badge key={subject} tone="neutral">
                  {subject}
                </Badge>
              ))}
            </div>
          )}

          {metadata?.description && (
            <p className="text-xs leading-relaxed text-[#93A0B8] whitespace-pre-line line-clamp-6">
              {metadata.description}
            </p>
          )}
        </div>
      </div>
    </Card>
  );
}
