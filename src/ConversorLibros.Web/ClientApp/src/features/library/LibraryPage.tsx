import { useCallback, useEffect, useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { BookOpen, RefreshCw, Search, Sparkles } from "lucide-react";
import { api } from "../../shared/api/client";
import type { LibraryItemDto } from "../../shared/api/types";
import { Button } from "../../shared/components/Button";
import { Card } from "../../shared/components/Card";
import { useToast } from "../../shared/components/Toast";
import { BookCard } from "./BookCard";

type FormatFilter = "all" | "opus" | "m4b";

export function LibraryPage() {
  const { showToast } = useToast();
  const [books, setBooks] = useState<LibraryItemDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [openingId, setOpeningId] = useState<string | null>(null);
  const [query, setQuery] = useState("");
  const [format, setFormat] = useState<FormatFilter>("all");

  const loadBooks = useCallback(async () => {
    setLoading(true);
    try {
      const data = await api.getLibrary();
      setBooks(data);
    } catch (err) {
      showToast(err instanceof Error ? err.message : "No se pudo cargar la biblioteca.", "error");
    } finally {
      setLoading(false);
    }
  }, [showToast]);

  useEffect(() => {
    void loadBooks();
  }, [loadBooks]);

  const filteredBooks = useMemo(() => {
    const q = query.trim().toLowerCase();
    return books.filter((book) => {
      if (format !== "all" && book.format !== format) {
        return false;
      }
      if (!q) return true;
      return (
        book.title.toLowerCase().includes(q) ||
        book.author.toLowerCase().includes(q)
      );
    });
  }, [books, query, format]);

  const handleOpen = async (id: string) => {
    setOpeningId(id);
    try {
      await api.openLibraryItem(id);
    } catch (err) {
      showToast(err instanceof Error ? err.message : "No se pudo abrir la carpeta.", "error");
    } finally {
      setOpeningId(null);
    }
  };

  return (
    <div className="space-y-6">
      <header className="flex flex-col sm:flex-row sm:items-end justify-between gap-4">
        <div>
          <h1 className="text-2xl font-bold tracking-tight text-[#EDF0F7]">Biblioteca</h1>
          <p className="text-sm text-[#93A0B8] mt-1">
            {loading
              ? "Escaneando carpeta de audiolibros…"
              : `${books.length} ${books.length === 1 ? "audiolibro disponible" : "audiolibros disponibles"}`}
          </p>
        </div>

        <div className="flex items-center gap-2">
          <Button
            variant="secondary"
            size="md"
            loading={loading}
            icon={<RefreshCw className="w-4 h-4" />}
            onClick={() => void loadBooks()}
          >
            Actualizar
          </Button>
          <Link
            to="/convertir"
            className="inline-flex items-center gap-2 px-4 py-2 rounded-lg text-sm font-semibold bg-[#F59E0B] text-[#0B0D12] hover:bg-[#FBBF24] transition-colors"
          >
            <Sparkles className="w-4 h-4" />
            Nueva conversión
          </Link>
        </div>
      </header>

      {books.length > 0 && (
        <div className="flex flex-col sm:flex-row items-stretch sm:items-center justify-between gap-3">
          <div className="relative flex-1 max-w-md">
            <Search className="w-4 h-4 text-[#64728C] absolute left-3.5 top-1/2 -translate-y-1/2" />
            <input
              type="text"
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              placeholder="Buscar por título o autor…"
              className="w-full pl-10 pr-4 py-2 rounded-xl bg-[#131720] border border-[#252C3D] focus:border-[#F59E0B] focus:outline-none text-sm text-[#EDF0F7] placeholder:text-[#64728C]"
            />
          </div>

          <div className="inline-flex items-center p-1 rounded-xl bg-[#131720] border border-[#252C3D]">
            {(
              [
                { id: "all", label: "Todos" },
                { id: "opus", label: "Opus" },
                { id: "m4b", label: "M4B" },
              ] as const
            ).map((opt) => (
              <button
                key={opt.id}
                type="button"
                onClick={() => setFormat(opt.id)}
                className={`px-3 py-1.5 rounded-lg text-xs font-medium transition-colors cursor-pointer ${
                  format === opt.id
                    ? "bg-[#1B2130] text-[#EDF0F7] shadow-sm"
                    : "text-[#93A0B8] hover:text-[#EDF0F7]"
                }`}
              >
                {opt.label}
              </button>
            ))}
          </div>
        </div>
      )}

      {loading ? (
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-5">
          {Array.from({ length: 4 }).map((_, idx) => (
            <div
              key={idx}
              className="rounded-2xl bg-[#131720] border border-[#252C3D] overflow-hidden animate-pulse"
            >
              <div className="aspect-[3/4] bg-[#1B2130]" />
              <div className="p-4 space-y-2.5">
                <div className="h-4 bg-[#1B2130] rounded w-3/4" />
                <div className="h-3 bg-[#1B2130] rounded w-1/2" />
                <div className="h-8 bg-[#1B2130] rounded-lg mt-4" />
              </div>
            </div>
          ))}
        </div>
      ) : books.length === 0 ? (
        <Card className="py-16 text-center flex flex-col items-center justify-center">
          <div className="w-14 h-14 rounded-2xl bg-[#1B2130] border border-[#2A3347] flex items-center justify-center mb-4">
            <BookOpen className="w-7 h-7 text-[#F59E0B]" />
          </div>
          <h2 className="text-lg font-semibold text-[#EDF0F7]">Tu biblioteca está vacía</h2>
          <p className="text-sm text-[#93A0B8] max-w-md mt-1.5">
            Aún no hay audiolibros en tu carpeta de salida. Convierte un EPUB sin DRM para empezar tu colección.
          </p>
          <Link
            to="/convertir"
            className="mt-6 inline-flex items-center gap-2 px-5 py-2.5 rounded-xl text-sm font-semibold bg-[#F59E0B] text-[#0B0D12] hover:bg-[#FBBF24] transition-colors"
          >
            <Sparkles className="w-4 h-4" />
            Convertir tu primer libro
          </Link>
        </Card>
      ) : filteredBooks.length === 0 ? (
        <Card className="py-12 text-center">
          <p className="text-sm text-[#93A0B8]">
            No hay audiolibros que coincidan con el filtro actual.
          </p>
        </Card>
      ) : (
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-5">
          {filteredBooks.map((book) => (
            <BookCard
              key={book.id}
              book={book}
              onOpen={handleOpen}
              opening={openingId === book.id}
            />
          ))}
        </div>
      )}
    </div>
  );
}
