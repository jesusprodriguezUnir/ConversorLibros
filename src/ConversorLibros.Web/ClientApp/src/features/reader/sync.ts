import { entryAtMs } from "./entries";

export const MIN_SPEED = 0.75;
export const MAX_SPEED = 2;
export const SPEED_STEP = 0.25;
export const SKIP_SECONDS = 15;

/** Active entry for the playback time: binary search, stays on the last entry across gaps; -1 before the first. */
export const findActiveEntry = entryAtMs;

/**
 * Page to show for an entry whose lines (client rects) sit on `rectPages`.
 * The page turns when the playback progress reaches the share of lines already read.
 */
export function pageAtProgress(rectPages: readonly number[], progress: number): number {
  const last = rectPages.length - 1;
  return rectPages[Math.min(last, Math.max(0, Math.floor(progress * rectPages.length)))];
}

export function skipMs(currentMs: number, deltaMs: number, durationMs: number): number {
  return Math.min(Math.max(0, currentMs + deltaMs), durationMs);
}

export function clampSpeed(speed: number): number {
  return Math.min(MAX_SPEED, Math.max(MIN_SPEED, speed));
}

/** Whole-book progress (0-100) from chapter durations, the current chapter position and its ms. */
export function bookPercent(durationsMs: readonly number[], position: number, ms: number): number {
  const total = durationsMs.reduce((sum, d) => sum + d, 0);
  if (total <= 0) return 0;
  const before = durationsMs.slice(0, position).reduce((sum, d) => sum + d, 0);
  const inside = Math.min(Math.max(0, ms), durationsMs[position] ?? 0);
  return Math.round(((before + inside) / total) * 100);
}
