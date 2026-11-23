import { spreadStart } from "./pagination";

export function PageIndicator({ page, count, ppv }: { page: number; count: number; ppv: number }) {
  return (
    <span className="reader-page-indicator">
      Página {spreadStart(page, ppv) + 1} de {count}
    </span>
  );
}
