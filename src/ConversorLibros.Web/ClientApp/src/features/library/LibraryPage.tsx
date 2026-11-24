import { useCallback, useEffect, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import {
  BookOpen,
  FolderOpen,
  Headphones,
  LayoutGrid,
  List,
  Plus,
  RefreshCw,
  Search,
  Upload,
} from "lucide-react";
import { api } from "../../shared/api/client";
import type { CatalogBookDto, LibraryFacetsDto } from "../../shared/api/types";
import { Button } from "../../shared/components/Button";
import { Card } from "../../shared/components/Card";
import { useToast } from "../../shared/components/Toast";
import { FolderBrowserModal } from "../settings/FolderBrowserModal";
import { BookInspectorPanel } from "./BookInspectorPanel";
import { EditMetadataModal } from "./EditMetadataModal";
import { LibraryFacetsSidebar, type LibraryFilters } from "./LibraryFacetsSidebar";
import { LibraryTableView } from "./LibraryTableView";

type ViewMode = "table" | "grid";
type SortField = "title" | "author" | "series" | "created_at" | "last_read_at";

const EMPTY_FACETS: LibraryFacetsDto = {
  counts: { all: 0, withAudio: 0, textOnly: 0, converting: 0 },
  authors: [],
  series: [],
  tags: [],
};

export function LibraryPage() {
  const { showToast } = useToast();
  const navigate = useNavigate();
  const fileInputRef = useRef<HTMLInputElement>(null);

  const [books, setBooks] = useState<CatalogBookDto[]>([]);
  const [facets, setFacets] = useState<LibraryFacetsDto>(EMPTY_FACETS);
  const [booksRoot, setBooksRoot] = useState("");
  const [loading, setLoading] = useState(true);
  const [scanning, setScanning] = useState(false);
  const [importing, setImporting] = useState(false);
  const [dragActive, setDragActive] = useState(false);

  const [query, setQuery] = useState("");
  const [sort, setSort] = useState<SortField>("title");
  const [viewMode, setViewMode] = useState<ViewMode>("table");
  const [filters, setFilters] = useState<LibraryFilters>({
    audioStatus: "",
    author: "",
    series: "",
    tag: "",
  });

  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [folderModalOpen, setFolderModalOpen] = useState(false);
  const [editingBook, setEditingBook] = useState<CatalogBookDto | null>(null);
  const [openingId, setOpeningId] = useState<string | null>(null);
  const [deletingId, setDeletingId] = useState<string | null>(null);

  const loadCatalog = useCallback(async () => {
    setLoading(true);
    try {
      const res = await api.getLibraryCatalog({
        q: query.trim() || undefined,
        author: filters.author || undefined,
        series: filters.series || undefined,
        tag: filters.tag || undefined,
        audioStatus: filters.audioStatus || undefined,
        sort,
        dir: sort === "created_at" || sort === "last_read_at" ? "desc" : "asc",
      });
      setBooks(res.items);
      setFacets(res.facets);
      setBooksRoot(res.booksRoot);
      setSelectedId((prev) =>
        prev && res.items.some((b) => b.id === prev) ? prev : (res.items[0]?.id ?? null)
      );
    } catch (err) {
      showToast(err instanceof Error ? err.message : "No se pudo cargar la biblioteca.", "error");
    } finally {
      setLoading(false);
    }
  }, [query, filters, sort, showToast]);

  useEffect(() => {
    void loadCatalog();
  }, [loadCatalog]);

  const selectedBook = books.find((b) => b.id === selectedId) ?? null;

  const handleSelectFolder = async (path: string) => {
    setFolderModalOpen(false);
    setScanning(true);
    try {
      const res = await api.setLibraryFolder(path);
      setBooksRoot(res.booksRoot);
      const { added, updated, errors } = res.scan;
      showToast(
        `Carpeta escaneada: ${added} nuevos, ${updated} actualizados${errors.length ? `, ${errors.length} con error` : ""}.`,
        errors.length ? "info" : "success"
      );
      await loadCatalog();
    } catch (err) {
      showToast(err instanceof Error ? err.message : "No se pudo configurar la carpeta.", "error");
    } finally {
      setScanning(false);
    }
  };

  const handleScan = async () => {
    setScanning(true);
    try {
      const summary = await api.scanLibraryFolder();
      showToast(
        `Escaneo completado: ${summary.added} nuevos, ${summary.updated} actualizados, ${summary.removed} quitados.`,
        "success"
      );
      await loadCatalog();
    } catch (err) {
      showToast(err instanceof Error ? err.message : "No se pudo escanear la carpeta.", "error");
    } finally {
      setScanning(false);
    }
  };

  const handleImportFiles = async (fileList: FileList | File[]) => {
    const epubs = Array.from(fileList).filter((f) => f.name.toLowerCase().endsWith(".epub"));
    if (epubs.length === 0) {
      showToast("Selecciona uno o más archivos .epub.", "info");
      return;
    }

    if (!booksRoot) {
      showToast("Elige primero una carpeta de libros donde guardar y leer tus EPUBs.", "info");
      setFolderModalOpen(true);
      return;
    }

    setImporting(true);
    try {
      const { results } = await api.importBooks(epubs);
      const imported = results.filter((r) => r.status === "imported").length;
      const skipped = results.filter((r) => r.status === "skipped").length;
      const errors = results.filter((r) => r.status === "error").length;
      const parts = [
        imported > 0 ? `${imported} añadido(s)` : null,
        skipped > 0 ? `${skipped} duplicado(s) omitido(s)` : null,
        errors > 0 ? `${errors} con error` : null,
      ].filter(Boolean);
      showToast(parts.join(" · ") || "Sin cambios.", errors > 0 ? "info" : "success");
      await loadCatalog();
    } catch (err) {
      showToast(err instanceof Error ? err.message : "No se pudieron añadir los libros.", "error");
    } finally {
      setImporting(false);
      if (fileInputRef.current) fileInputRef.current.value = "";
    }
  };

  const handleOpenFolder = async (book: CatalogBookDto) => {
    setOpeningId(book.id);
    try {
      await api.openLibraryItem(book.id);
    } catch (err) {
      showToast(err instanceof Error ? err.message : "No se pudo abrir la carpeta.", "error");
    } finally {
      setOpeningId(null);
    }
  };

  const handleDelete = async (book: CatalogBookDto) => {
    const confirmed = window.confirm(
      `¿Quitar "${book.title}" del catálogo? Tu archivo EPUB original en disco no se borrará.`
    );
    if (!confirmed) return;

    setDeletingId(book.id);
    try {
      await api.deleteBook(book.id);
      showToast(`"${book.title}" quitado del catálogo.`, "success");
      await loadCatalog();
    } catch (err) {
      showToast(err instanceof Error ? err.message : "No se pudo quitar el libro.", "error");
    } finally {
      setDeletingId(null);
    }
  };

  return (
    <div
      className="space-y-4 relative"
      onDragOver={(e) => {
        e.preventDefault();
        setDragActive(true);
      }}
      onDragLeave={(e) => {
        if (e.currentTarget === e.target) setDragActive(false);
      }}
      onDrop={(e) => {
        e.preventDefault();
        setDragActive(false);
        if (e.dataTransfer.files?.length) {
          void handleImportFiles(e.dataTransfer.files);
        }
      }}
    >
      {dragActive && (
        <div className="fixed inset-0 z-40 bg-[#0B0D12]/80 backdrop-blur-xs flex items-center justify-center pointer-events-none">
          <div className="rounded-2xl border-2 border-dashed border-[#F59E0B] bg-[#131720] px-10 py-8 text-center">
            <Upload className="w-10 h-10 text-[#F59E0B] mx-auto mb-3" />
            <p className="text-base font-semibold text-[#EDF0F7]">
              Suelta tus archivos .epub para añadirlos a tu carpeta
            </p>
          </div>
        </div>
      )}

      <input
        ref={fileInputRef}
        type="file"
        accept=".epub"
        multiple
        className="hidden"
        onChange={(e) => {
          if (e.target.files?.length) void handleImportFiles(e.target.files);
        }}
      />

      <header className="flex flex-col gap-3">
        <div className="flex flex-col lg:flex-row lg:items-center justify-between gap-3">
          <div>
            <h1 className="text-2xl font-bold tracking-tight text-[#EDF0F7]">Biblioteca</h1>
            <div className="flex flex-wrap items-center gap-2 mt-1 text-xs text-[#93A0B8]">
              <span>
                {loading
                  ? "Cargando catálogo…"
                  : `${facets.counts.all} ${facets.counts.all === 1 ? "libro" : "libros"} en biblioteca`}
              </span>
              <span className="text-[#2A3347]">•</span>
              <span className="truncate max-w-md" title={booksRoot || "Sin carpeta seleccionada"}>
                Carpeta: <strong className="text-[#CBD5E1]">{booksRoot || "Sin seleccionar"}</strong>
              </span>
            </div>
          </div>

          <div className="flex flex-wrap items-center gap-2">
            <Button
              variant="secondary"
              size="sm"
              icon={<FolderOpen className="w-4 h-4" />}
              onClick={() => setFolderModalOpen(true)}
            >
              Elegir carpeta
            </Button>

            <Button
              variant="secondary"
              size="sm"
              loading={scanning}
              icon={<RefreshCw className="w-4 h-4" />}
              onClick={() => void handleScan()}
              disabled={!booksRoot}
            >
              Escanear
            </Button>

            <Button
              variant="primary"
              size="sm"
              loading={importing}
              icon={<Plus className="w-4 h-4" />}
              onClick={() => fileInputRef.current?.click()}
            >
              Añadir libros
            </Button>
          </div>
        </div>

        <div className="flex flex-col sm:flex-row items-stretch sm:items-center justify-between gap-3">
          <div className="relative flex-1 max-w-lg">
            <Search className="w-4 h-4 text-[#64728C] absolute left-3.5 top-1/2 -translate-y-1/2" />
            <input
              type="text"
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              placeholder="Buscar por título, autor, serie, etiqueta o sinopsis…"
              className="w-full pl-10 pr-4 py-2 rounded-xl bg-[#131720] border border-[#252C3D] focus:border-[#F59E0B] focus:outline-none text-sm text-[#EDF0F7] placeholder:text-[#64728C]"
            />
          </div>

          <div className="flex items-center gap-2">
            <select
              aria-label="Ordenar por"
              value={sort}
              onChange={(e) => setSort(e.target.value as SortField)}
              className="px-3 py-2 rounded-xl bg-[#131720] border border-[#252C3D] text-xs text-[#EDF0F7] focus:border-[#F59E0B] focus:outline-none"
            >
              <option value="title">Orden: Título</option>
              <option value="author">Orden: Autor</option>
              <option value="series">Orden: Serie</option>
              <option value="created_at">Orden: Recientes</option>
              <option value="last_read_at">Orden: Última lectura</option>
            </select>

            <div className="inline-flex items-center p-1 rounded-xl bg-[#131720] border border-[#252C3D]">
              <button
                type="button"
                aria-label="Vista tabla"
                onClick={() => setViewMode("table")}
                className={`p-1.5 rounded-lg transition-colors cursor-pointer ${
                  viewMode === "table"
                    ? "bg-[#1B2130] text-[#EDF0F7]"
                    : "text-[#93A0B8] hover:text-[#EDF0F7]"
                }`}
              >
                <List className="w-4 h-4" />
              </button>
              <button
                type="button"
                aria-label="Vista portadas"
                onClick={() => setViewMode("grid")}
                className={`p-1.5 rounded-lg transition-colors cursor-pointer ${
                  viewMode === "grid"
                    ? "bg-[#1B2130] text-[#EDF0F7]"
                    : "text-[#93A0B8] hover:text-[#EDF0F7]"
                }`}
              >
                <LayoutGrid className="w-4 h-4" />
              </button>
            </div>
          </div>
        </div>
      </header>

      {!loading && facets.counts.all === 0 ? (
        <Card className="py-16 text-center flex flex-col items-center justify-center">
          <div className="w-14 h-14 rounded-2xl bg-[#1B2130] border border-[#2A3347] flex items-center justify-center mb-4">
            <FolderOpen className="w-7 h-7 text-[#F59E0B]" />
          </div>
          <h2 className="text-lg font-semibold text-[#EDF0F7]">
            {booksRoot ? "No se encontraron libros en esta carpeta" : "Selecciona tu carpeta de libros"}
          </h2>
          <p className="text-sm text-[#93A0B8] max-w-md mt-1.5">
            {booksRoot
              ? "Arrastra archivos .epub aquí o pulsa Añadir libros para empezar a llenar tu biblioteca."
              : "Elige una carpeta de tu PC con archivos .epub (incluso en subcarpetas) para indexarlos sin duplicarlos."}
          </p>
          <div className="mt-6 flex items-center gap-3">
            <Button
              variant="primary"
              icon={<FolderOpen className="w-4 h-4" />}
              onClick={() => setFolderModalOpen(true)}
            >
              Seleccionar carpeta de libros
            </Button>
            {booksRoot && (
              <Button
                variant="secondary"
                icon={<Plus className="w-4 h-4" />}
                onClick={() => fileInputRef.current?.click()}
              >
                Añadir libros
              </Button>
            )}
          </div>
        </Card>
      ) : (
        <div className="grid grid-cols-1 lg:grid-cols-[220px_1fr_300px] gap-4 items-start">
          <LibraryFacetsSidebar
            facets={facets}
            filters={filters}
            onChange={(patch) => setFilters((prev) => ({ ...prev, ...patch }))}
            onClearAll={() => setFilters({ audioStatus: "", author: "", series: "", tag: "" })}
          />

          <section aria-label="Catálogo de libros" className="min-w-0">
            {loading ? (
              <Card className="py-12 text-center text-sm text-[#93A0B8]">Cargando libros…</Card>
            ) : books.length === 0 ? (
              <Card className="py-12 text-center text-sm text-[#93A0B8]">
                No hay libros que coincidan con el filtro actual.
              </Card>
            ) : viewMode === "table" ? (
              <LibraryTableView
                books={books}
                selectedId={selectedId}
                onSelect={(b) => setSelectedId(b.id)}
                onOpenReader={(b) => navigate(`/biblioteca/${encodeURIComponent(b.id)}/leer`)}
              />
            ) : (
              <div className="grid grid-cols-2 sm:grid-cols-3 xl:grid-cols-4 gap-4">
                {books.map((book) => {
                  const selected = book.id === selectedId;
                  return (
                    <button
                      key={book.id}
                      type="button"
                      onClick={() => setSelectedId(book.id)}
                      onDoubleClick={() => {
                        if (book.hasReader) navigate(`/biblioteca/${encodeURIComponent(book.id)}/leer`);
                      }}
                      className={`text-left rounded-2xl bg-[#131720] border overflow-hidden transition-all cursor-pointer ${
                        selected
                          ? "border-[#F59E0B] ring-1 ring-[#F59E0B]/40"
                          : "border-[#252C3D] hover:border-[#3A4560]"
                      }`}
                    >
                      <div className="aspect-[3/4] bg-[#1B2130] relative flex items-center justify-center overflow-hidden">
                        {book.coverUrl ? (
                          <img
                            src={book.coverUrl}
                            alt={`Portada de ${book.title}`}
                            className="w-full h-full object-cover"
                            loading="lazy"
                          />
                        ) : (
                          <BookOpen className="w-10 h-10 text-[#64728C]" />
                        )}
                        {book.audioStatus === "ready" && (
                          <span className="absolute top-2 right-2 px-2 py-0.5 rounded-full text-[10px] font-semibold bg-emerald-500/90 text-[#0B0D12] inline-flex items-center gap-1">
                            <Headphones className="w-3 h-3" />
                            {(book.audioFormat ?? "audio").toUpperCase()}
                          </span>
                        )}
                      </div>
                      <div className="p-3">
                        <p className="text-sm font-semibold text-[#EDF0F7] truncate">{book.title}</p>
                        <p className="text-xs text-[#93A0B8] truncate mt-0.5">{book.author}</p>
                      </div>
                    </button>
                  );
                })}
              </div>
            )}
          </section>

          <BookInspectorPanel
            book={selectedBook}
            onEdit={(b) => setEditingBook(b)}
            onOpenFolder={(b) => void handleOpenFolder(b)}
            onDelete={(b) => void handleDelete(b)}
            opening={openingId === selectedBook?.id}
            deleting={deletingId === selectedBook?.id}
          />
        </div>
      )}

      <FolderBrowserModal
        open={folderModalOpen}
        initialPath={booksRoot || undefined}
        title="Seleccionar carpeta de libros (EPUB)"
        onSelect={(path) => void handleSelectFolder(path)}
        onClose={() => setFolderModalOpen(false)}
      />

      <EditMetadataModal
        book={editingBook}
        onClose={() => setEditingBook(null)}
        onSaved={() => {
          setEditingBook(null);
          showToast("Metadatos actualizados.", "success");
          void loadCatalog();
        }}
      />
    </div>
  );
}
