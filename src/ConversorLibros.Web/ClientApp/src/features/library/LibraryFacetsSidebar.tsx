import { useState } from "react";
import { BookOpen, ChevronDown, ChevronRight, Headphones, Library, Loader2, Tag, Users, Layers, X } from "lucide-react";
import type { FacetItemDto, LibraryFacetsDto } from "../../shared/api/types";

export interface LibraryFilters {
  audioStatus: string;
  author: string;
  series: string;
  tag: string;
}

interface LibraryFacetsSidebarProps {
  facets: LibraryFacetsDto;
  filters: LibraryFilters;
  onChange: (next: Partial<LibraryFilters>) => void;
  onClearAll: () => void;
}

export function LibraryFacetsSidebar({
  facets,
  filters,
  onChange,
  onClearAll,
}: LibraryFacetsSidebarProps) {
  const [openSections, setOpenSections] = useState({
    authors: true,
    series: true,
    tags: true,
  });

  const hasActiveFilter = Boolean(
    filters.audioStatus || filters.author || filters.series || filters.tag
  );

  const toggleSection = (key: keyof typeof openSections) => {
    setOpenSections((prev) => ({ ...prev, [key]: !prev[key] }));
  };

  const quickFilters = [
    { id: "", label: "Todos los libros", count: facets.counts.all, icon: Library },
    { id: "ready", label: "Con audiolibro", count: facets.counts.withAudio, icon: Headphones },
    { id: "none", label: "Solo lectura", count: facets.counts.textOnly, icon: BookOpen },
    { id: "converting", label: "En conversión", count: facets.counts.converting, icon: Loader2 },
  ];

  return (
    <aside className="rounded-2xl bg-[#131720] border border-[#252C3D] p-3 space-y-4 h-fit">
      <div className="flex items-center justify-between px-1">
        <span className="text-xs font-semibold uppercase tracking-wider text-[#93A0B8]">
          Explorar
        </span>
        {hasActiveFilter && (
          <button
            type="button"
            onClick={onClearAll}
            className="inline-flex items-center gap-1 text-xs text-[#F59E0B] hover:text-[#FBBF24] cursor-pointer"
          >
            <X className="w-3 h-3" />
            Limpiar
          </button>
        )}
      </div>

      <nav className="space-y-1" aria-label="Estado de libros">
        {quickFilters.map((item) => {
          const Icon = item.icon;
          const active = filters.audioStatus === item.id;
          return (
            <button
              key={item.id || "all"}
              type="button"
              onClick={() => onChange({ audioStatus: item.id })}
              className={`w-full flex items-center justify-between gap-2 px-2.5 py-1.5 rounded-lg text-xs font-medium transition-colors cursor-pointer ${
                active
                  ? "bg-[#F59E0B]/15 text-[#F59E0B]"
                  : "text-[#CBD5E1] hover:bg-[#1B2130]"
              }`}
            >
              <span className="inline-flex items-center gap-2 truncate">
                <Icon className="w-3.5 h-3.5 shrink-0" />
                <span className="truncate">{item.label}</span>
              </span>
              <span className="text-[11px] opacity-75 tabular-nums">{item.count}</span>
            </button>
          );
        })}
      </nav>

      <FacetGroup
        title="Autores"
        icon={<Users className="w-3.5 h-3.5" />}
        items={facets.authors}
        selected={filters.author}
        open={openSections.authors}
        onToggle={() => toggleSection("authors")}
        onSelect={(name) => onChange({ author: filters.author === name ? "" : name })}
      />

      <FacetGroup
        title="Series"
        icon={<Layers className="w-3.5 h-3.5" />}
        items={facets.series}
        selected={filters.series}
        open={openSections.series}
        onToggle={() => toggleSection("series")}
        onSelect={(name) => onChange({ series: filters.series === name ? "" : name })}
      />

      <FacetGroup
        title="Etiquetas"
        icon={<Tag className="w-3.5 h-3.5" />}
        items={facets.tags}
        selected={filters.tag}
        open={openSections.tags}
        onToggle={() => toggleSection("tags")}
        onSelect={(name) => onChange({ tag: filters.tag === name ? "" : name })}
      />
    </aside>
  );
}

interface FacetGroupProps {
  title: string;
  icon: React.ReactNode;
  items: FacetItemDto[];
  selected: string;
  open: boolean;
  onToggle: () => void;
  onSelect: (name: string) => void;
}

function FacetGroup({
  title,
  icon,
  items,
  selected,
  open,
  onToggle,
  onSelect,
}: FacetGroupProps) {
  if (items.length === 0) return null;

  return (
    <div className="border-t border-[#252C3D] pt-3">
      <button
        type="button"
        onClick={onToggle}
        className="w-full flex items-center justify-between px-1 text-xs font-semibold uppercase tracking-wider text-[#93A0B8] hover:text-[#EDF0F7] cursor-pointer"
      >
        <span className="inline-flex items-center gap-1.5">
          {icon}
          {title}
        </span>
        {open ? <ChevronDown className="w-3.5 h-3.5" /> : <ChevronRight className="w-3.5 h-3.5" />}
      </button>

      {open && (
        <ul className="mt-1.5 space-y-0.5 max-h-44 overflow-y-auto pr-1">
          {items.map((item) => {
            const active = selected.toLowerCase() === item.name.toLowerCase();
            return (
              <li key={item.name}>
                <button
                  type="button"
                  onClick={() => onSelect(item.name)}
                  className={`w-full flex items-center justify-between gap-2 px-2 py-1 rounded-md text-xs transition-colors cursor-pointer ${
                    active
                      ? "bg-[#F59E0B]/15 text-[#F59E0B] font-medium"
                      : "text-[#93A0B8] hover:text-[#EDF0F7] hover:bg-[#1B2130]"
                  }`}
                >
                  <span className="truncate text-left">{item.name}</span>
                  <span className="text-[11px] opacity-70 tabular-nums shrink-0">{item.count}</span>
                </button>
              </li>
            );
          })}
        </ul>
      )}
    </div>
  );
}
