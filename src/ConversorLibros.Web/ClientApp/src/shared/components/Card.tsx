import type { HTMLAttributes, ReactNode } from "react";

interface CardProps extends HTMLAttributes<HTMLDivElement> {
  children: ReactNode;
  padded?: boolean;
}

export function Card({ children, padded = true, className = "", ...props }: CardProps) {
  return (
    <div
      className={`bg-[#131720] border border-[#252C3D] rounded-2xl shadow-lg shadow-black/20 ${
        padded ? "p-5" : ""
      } ${className}`}
      {...props}
    >
      {children}
    </div>
  );
}
