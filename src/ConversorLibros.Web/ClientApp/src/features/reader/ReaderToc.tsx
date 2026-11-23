import type { ReaderIndexChapterDto } from "../../shared/api/types";

interface Props {
  chapters: ReaderIndexChapterDto[];
  current: number | null;
  onSelect: (index: number) => void;
}

/**
 * Lists the indexed chapters in order. Each run of missing indexes (subset conversions) becomes one disabled
 * entry, so the list stays bounded by the number of indexed chapters however sparse the indexes are.
 */
export function ReaderToc({ chapters, current, onSelect }: Props) {
  const sorted = [...chapters].sort((a, b) => a.index - b.index);
  const items: { index: number; title: string; available: boolean }[] = [];
  sorted.forEach((c, i) => {
    const prev = sorted[i - 1]?.index;
    if (prev !== undefined && c.index - prev > 1) {
      const from = prev + 1;
      const to = c.index - 1;
      items.push({ index: from, title: from === to ? `Capítulo ${from}` : `Capítulos ${from}–${to}`, available: false });
    }
    items.push({ index: c.index, title: c.title, available: true });
  });

  return (
    <nav className="reader-toc" aria-label="Capítulos">
      {items.map((item) => (
        <button
          key={item.index}
          type="button"
          disabled={!item.available}
          aria-current={item.index === current ? "true" : undefined}
          title={item.available ? undefined : "Sin datos de lectura"}
          onClick={() => onSelect(item.index)}
        >
          {item.title}
        </button>
      ))}
    </nav>
  );
}
