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
