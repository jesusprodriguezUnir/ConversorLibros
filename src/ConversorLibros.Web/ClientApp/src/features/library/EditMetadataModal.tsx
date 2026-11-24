import { useEffect, useState } from "react";
import { api } from "../../shared/api/client";
import type { CatalogBookDto } from "../../shared/api/types";
import { Button } from "../../shared/components/Button";
import { Modal } from "../../shared/components/Modal";

interface EditMetadataModalProps {
  book: CatalogBookDto | null;
  onClose: () => void;
  onSaved: (updated: CatalogBookDto) => void;
}

export function EditMetadataModal({ book, onClose, onSaved }: EditMetadataModalProps) {
  const [title, setTitle] = useState("");
  const [author, setAuthor] = useState("");
  const [series, setSeries] = useState("");
  const [seriesIndex, setSeriesIndex] = useState("");
  const [publisher, setPublisher] = useState("");
  const [publishedDate, setPublishedDate] = useState("");
  const [isbn, setIsbn] = useState("");
  const [tagsText, setTagsText] = useState("");
  const [description, setDescription] = useState("");
  const [coverFile, setCoverFile] = useState<File | null>(null);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!book) return;
    setTitle(book.title);
    setAuthor(book.author === "Autor desconocido" ? "" : book.author);
    setSeries(book.series ?? "");
    setSeriesIndex(book.seriesIndex != null ? String(book.seriesIndex) : "");
    setPublisher(book.publisher ?? "");
    setPublishedDate(book.publishedDate ?? "");
    setIsbn(book.isbn ?? "");
    setTagsText(book.tags.join(", "));
    setDescription(book.description ?? "");
    setCoverFile(null);
    setError(null);
  }, [book]);

  if (!book) return null;

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setSaving(true);
    setError(null);
    try {
      const parsedIndex = seriesIndex.trim() ? Number(seriesIndex.replace(",", ".")) : null;
      const tags = tagsText
        .split(",")
        .map((t) => t.trim())
        .filter(Boolean);

      const updated = await api.updateBookMetadata(book.id, {
        title: title.trim() || book.title,
        author: author.trim(),
        series: series.trim(),
        seriesIndex: parsedIndex !== null && !Number.isNaN(parsedIndex) ? parsedIndex : null,
        publisher: publisher.trim(),
        publishedDate: publishedDate.trim(),
        isbn: isbn.trim(),
        description: description.trim(),
        tags,
      });

      if (coverFile) {
        await api.uploadBookCover(book.id, coverFile);
      }

      onSaved({
        ...updated,
        hasCover: coverFile ? true : updated.hasCover,
        coverUrl: coverFile ? `/api/library/${encodeURIComponent(book.id)}/cover?t=${Date.now()}` : updated.coverUrl,
      });
    } catch (err) {
      setError(err instanceof Error ? err.message : "No se pudieron guardar los cambios.");
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal open={book !== null} onClose={onClose} title="Editar metadatos">
      <form onSubmit={(e) => void handleSubmit(e)} className="space-y-3 text-sm">
        {error && (
          <p className="text-xs text-rose-300 bg-rose-500/10 border border-rose-500/30 rounded-lg px-3 py-2">
            {error}
          </p>
        )}

        <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
          <label className="block space-y-1">
            <span className="text-xs text-[#93A0B8]">Título</span>
            <input
              type="text"
              value={title}
              onChange={(e) => setTitle(e.target.value)}
              required
              className="w-full px-3 py-1.5 rounded-lg bg-[#0F131B] border border-[#252C3D] text-[#EDF0F7] focus:border-[#F59E0B] focus:outline-none"
            />
          </label>

          <label className="block space-y-1">
            <span className="text-xs text-[#93A0B8]">Autor</span>
            <input
              type="text"
              value={author}
              onChange={(e) => setAuthor(e.target.value)}
              className="w-full px-3 py-1.5 rounded-lg bg-[#0F131B] border border-[#252C3D] text-[#EDF0F7] focus:border-[#F59E0B] focus:outline-none"
            />
          </label>

          <label className="block space-y-1">
            <span className="text-xs text-[#93A0B8]">Serie</span>
            <input
              type="text"
              value={series}
              onChange={(e) => setSeries(e.target.value)}
              className="w-full px-3 py-1.5 rounded-lg bg-[#0F131B] border border-[#252C3D] text-[#EDF0F7] focus:border-[#F59E0B] focus:outline-none"
            />
          </label>

          <label className="block space-y-1">
            <span className="text-xs text-[#93A0B8]">Número en la serie</span>
            <input
              type="text"
              value={seriesIndex}
              onChange={(e) => setSeriesIndex(e.target.value)}
              placeholder="Ej. 1"
              className="w-full px-3 py-1.5 rounded-lg bg-[#0F131B] border border-[#252C3D] text-[#EDF0F7] focus:border-[#F59E0B] focus:outline-none"
            />
          </label>

          <label className="block space-y-1">
            <span className="text-xs text-[#93A0B8]">Editorial</span>
            <input
              type="text"
              value={publisher}
              onChange={(e) => setPublisher(e.target.value)}
              className="w-full px-3 py-1.5 rounded-lg bg-[#0F131B] border border-[#252C3D] text-[#EDF0F7] focus:border-[#F59E0B] focus:outline-none"
            />
          </label>

          <label className="block space-y-1">
            <span className="text-xs text-[#93A0B8]">ISBN</span>
            <input
              type="text"
              value={isbn}
              onChange={(e) => setIsbn(e.target.value)}
              className="w-full px-3 py-1.5 rounded-lg bg-[#0F131B] border border-[#252C3D] text-[#EDF0F7] focus:border-[#F59E0B] focus:outline-none"
            />
          </label>
        </div>

        <label className="block space-y-1">
          <span className="text-xs text-[#93A0B8]">Etiquetas (separadas por comas)</span>
          <input
            type="text"
            value={tagsText}
            onChange={(e) => setTagsText(e.target.value)}
            placeholder="Fantasía, Ciencia Ficción"
            className="w-full px-3 py-1.5 rounded-lg bg-[#0F131B] border border-[#252C3D] text-[#EDF0F7] focus:border-[#F59E0B] focus:outline-none"
          />
        </label>

        <label className="block space-y-1">
          <span className="text-xs text-[#93A0B8]">Sinopsis</span>
          <textarea
            rows={3}
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            className="w-full px-3 py-1.5 rounded-lg bg-[#0F131B] border border-[#252C3D] text-[#EDF0F7] focus:border-[#F59E0B] focus:outline-none"
          />
        </label>

        <label className="block space-y-1">
          <span className="text-xs text-[#93A0B8]">Cambiar portada (JPG/PNG)</span>
          <input
            type="file"
            accept="image/jpeg,image/png"
            onChange={(e) => setCoverFile(e.target.files?.[0] ?? null)}
            className="block w-full text-xs text-[#93A0B8] file:mr-3 file:py-1.5 file:px-3 file:rounded-lg file:border-0 file:bg-[#1B2130] file:text-[#EDF0F7]"
          />
        </label>

        <div className="flex justify-end gap-2 pt-2">
          <Button type="button" variant="secondary" onClick={onClose}>
            Cancelar
          </Button>
          <Button type="submit" loading={saving}>
            Guardar cambios
          </Button>
        </div>
      </form>
    </Modal>
  );
}
