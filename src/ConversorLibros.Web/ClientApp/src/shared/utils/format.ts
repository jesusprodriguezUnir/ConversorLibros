export function formatDuration(totalSeconds: number | null): string | null {
  if (totalSeconds === null || !Number.isFinite(totalSeconds) || totalSeconds <= 0) {
    return null;
  }

  const rounded = Math.round(totalSeconds);
  const hours = Math.floor(rounded / 3600);
  const minutes = Math.floor((rounded % 3600) / 60);
  const seconds = rounded % 60;

  if (hours > 0) {
    return `${hours}h ${String(minutes).padStart(2, "0")}m`;
  }

  return `${minutes}:${String(seconds).padStart(2, "0")}`;
}

/** Spanish duration for chapter and book estimates, e.g. "45 s", "12 min", "2 h 05 min". */
export function formatSpanishDuration(totalSeconds: number): string {
  const rounded = Math.max(0, Math.round(totalSeconds));
  if (rounded < 60) {
    return `${rounded} s`;
  }

  const totalMinutes = Math.round(rounded / 60);
  if (totalMinutes < 60) {
    return `${totalMinutes} min`;
  }

  const hours = Math.floor(totalMinutes / 60);
  const minutes = totalMinutes % 60;
  return `${hours} h ${String(minutes).padStart(2, "0")} min`;
}

export function formatCharEstimate(charCount: number): string {
  if (charCount < 1000) {
    return `${charCount} car.`;
  }

  const approxMinutes = Math.max(1, Math.round(charCount / 900));
  return `~${approxMinutes} min`;
}

export function getBookInitials(title: string): string {
  const words = title
    .trim()
    .split(/\s+/)
    .filter((w) => w.length > 0);
  if (words.length === 0) return "CL";
  if (words.length === 1) return words[0].slice(0, 2).toUpperCase();
  return (words[0][0] + words[1][0]).toUpperCase();
}
