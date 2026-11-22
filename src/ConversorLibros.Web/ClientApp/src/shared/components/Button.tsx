import type { ButtonHTMLAttributes, ReactNode } from "react";
import { Loader2 } from "lucide-react";

type Variant = "primary" | "secondary" | "ghost" | "danger";
type Size = "sm" | "md" | "lg";

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: Variant;
  size?: Size;
  loading?: boolean;
  icon?: ReactNode;
}

const variantClasses: Record<Variant, string> = {
  primary:
    "bg-[#F59E0B] text-[#0B0D12] font-semibold hover:bg-[#FBBF24] active:bg-[#D97706] shadow-sm shadow-[#F59E0B]/10",
  secondary:
    "bg-[#1B2130] text-[#EDF0F7] border border-[#2A3347] hover:bg-[#232B3E] hover:border-[#3A4660]",
  ghost:
    "bg-transparent text-[#93A0B8] hover:text-[#EDF0F7] hover:bg-[#1B2130]/70",
  danger:
    "bg-rose-500/15 text-rose-300 border border-rose-500/30 hover:bg-rose-500/25",
};

const sizeClasses: Record<Size, string> = {
  sm: "px-3 py-1.5 text-xs rounded-lg gap-1.5",
  md: "px-4 py-2 text-sm rounded-lg gap-2",
  lg: "px-5 py-2.5 text-sm rounded-xl gap-2",
};

export function Button({
  variant = "primary",
  size = "md",
  loading = false,
  icon,
  disabled,
  className = "",
  children,
  ...props
}: ButtonProps) {
  return (
    <button
      disabled={disabled || loading}
      className={`inline-flex items-center justify-center transition-all duration-150 cursor-pointer disabled:opacity-45 disabled:cursor-not-allowed ${variantClasses[variant]} ${sizeClasses[size]} ${className}`}
      {...props}
    >
      {loading ? <Loader2 className="w-4 h-4 animate-spin" /> : icon}
      {children}
    </button>
  );
}
