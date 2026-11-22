import type { ReactNode } from "react";

type Tone = "success" | "warning" | "error" | "accent" | "neutral";

interface BadgeProps {
  tone?: Tone;
  children: ReactNode;
  className?: string;
}

const toneClasses: Record<Tone, string> = {
  success: "bg-emerald-500/15 text-emerald-300 border-emerald-500/30",
  warning: "bg-amber-500/15 text-amber-300 border-amber-500/30",
  error: "bg-rose-500/15 text-rose-300 border-rose-500/30",
  accent: "bg-[#F59E0B]/15 text-[#FBBF24] border-[#F59E0B]/30",
  neutral: "bg-[#1B2130] text-[#93A0B8] border-[#2A3347]",
};

export function Badge({ tone = "neutral", children, className = "" }: BadgeProps) {
  return (
    <span
      className={`inline-flex items-center gap-1.5 px-2.5 py-0.5 text-xs font-medium rounded-full border ${toneClasses[tone]} ${className}`}
    >
      {children}
    </span>
  );
}
