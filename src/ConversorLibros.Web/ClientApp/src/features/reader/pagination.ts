/** Viewport width (px) from which a two-page spread is shown. */
export const SPREAD_MIN_WIDTH = 1100;

export interface ColumnMetrics {
  colW: number;
  stride: number;
  contentWidth: number;
}

export function pagesPerView(width: number): 1 | 2 {
  return width >= SPREAD_MIN_WIDTH ? 2 : 1;
}

export function columnMetrics(width: number, gap: number, ppv: number): ColumnMetrics {
  const colW = Math.max(1, Math.floor((width - gap * (ppv - 1)) / ppv));
  return { colW, stride: colW + gap, contentWidth: ppv * colW + (ppv - 1) * gap };
}

/** scrollWidth of n columns is n*stride - gap. */
export function pageCount(scrollWidth: number, metrics: ColumnMetrics, gap: number): number {
  return Math.max(1, Math.ceil((scrollWidth + gap) / metrics.stride - 1e-6));
}

export function pageOfRect(rectLeft: number, contentLeft: number, stride: number): number {
  return Math.max(0, Math.floor((rectLeft - contentLeft) / stride));
}

export function clampPage(page: number, count: number): number {
  return Math.min(Math.max(0, page), Math.max(0, count - 1));
}

export function spreadStart(page: number, ppv: number): number {
  return page - (page % ppv);
}
