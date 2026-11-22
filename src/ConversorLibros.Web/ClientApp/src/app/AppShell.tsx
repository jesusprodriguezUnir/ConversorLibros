import { NavLink, Outlet } from "react-router-dom";
import { BookHeadphones, Library, Sparkles, Settings, Activity } from "lucide-react";
import { useConversion } from "./ConversionContext";

const navItems = [
  { to: "/biblioteca", label: "Biblioteca", icon: Library },
  { to: "/convertir", label: "Nueva conversión", icon: Sparkles },
  { to: "/ajustes", label: "Ajustes", icon: Settings },
];

export function AppShell() {
  const { state, isRunning } = useConversion();
  const percent = Math.round(state.progressFraction * 100);

  return (
    <div className="min-h-screen flex bg-[#0B0D12] text-[#EDF0F7]">
      <aside className="w-64 shrink-0 bg-[#10131B] border-r border-[#1F2536] flex flex-col justify-between sticky top-0 h-screen">
        <div>
          <div className="px-6 py-6 flex items-center gap-3 border-b border-[#1F2536]">
            <div className="w-9 h-9 rounded-xl bg-gradient-to-br from-[#F59E0B] to-[#D97706] flex items-center justify-center shadow-lg shadow-[#F59E0B]/20">
              <BookHeadphones className="w-5 h-5 text-[#0B0D12]" />
            </div>
            <div>
              <div className="text-sm font-bold tracking-tight text-[#EDF0F7]">
                ConversorLibros
              </div>
              <div className="text-[11px] text-[#64728C] font-medium">
                Estudio de Audiolibros
              </div>
            </div>
          </div>

          <nav className="p-3 space-y-1" aria-label="Navegación principal">
            {navItems.map(({ to, label, icon: Icon }) => (
              <NavLink
                key={to}
                to={to}
                className={({ isActive }) =>
                  `group flex items-center gap-3 px-3.5 py-2.5 rounded-xl text-sm font-medium transition-all duration-150 ${
                    isActive
                      ? "bg-[#1B2130] text-[#EDF0F7] shadow-sm border border-[#2A3347]"
                      : "text-[#93A0B8] hover:text-[#EDF0F7] hover:bg-[#161B26] border border-transparent"
                  }`
                }
              >
                {({ isActive }) => (
                  <>
                    <Icon
                      className={`w-4 h-4 transition-colors ${
                        isActive ? "text-[#F59E0B]" : "text-[#64728C] group-hover:text-[#93A0B8]"
                      }`}
                    />
                    <span>{label}</span>
                  </>
                )}
              </NavLink>
            ))}
          </nav>
        </div>

        <div className="p-3 border-t border-[#1F2536]">
          {isRunning ? (
            <NavLink
              to="/convertir"
              className="block p-3.5 rounded-xl bg-[#161C28] border border-[#F59E0B]/35 hover:border-[#F59E0B]/60 transition-colors"
            >
              <div className="flex items-center justify-between gap-2 mb-1.5">
                <span className="inline-flex items-center gap-1.5 text-xs font-semibold text-[#FBBF24]">
                  <Activity className="w-3.5 h-3.5 animate-pulse" />
                  {state.stage}
                </span>
                <span className="font-mono text-xs text-[#EDF0F7]">{percent}%</span>
              </div>
              {state.bookTitle && (
                <p className="text-xs text-[#93A0B8] truncate mb-2">{state.bookTitle}</p>
              )}
              <div className="h-1.5 rounded-full bg-[#0B0D12] overflow-hidden">
                <div
                  className="h-full rounded-full bg-gradient-to-r from-[#F59E0B] to-[#FBBF24] transition-all duration-300"
                  style={{ width: `${Math.max(4, percent)}%` }}
                />
              </div>
            </NavLink>
          ) : (
            <div className="px-3 py-2 text-[11px] text-[#64728C] flex items-center justify-between">
              <span>Modo local offline</span>
              <span className="font-mono">es-ES</span>
            </div>
          )}
        </div>
      </aside>

      <main className="flex-1 min-w-0 overflow-y-auto">
        <div className="max-w-7xl mx-auto px-6 lg:px-10 py-8">
          <Outlet />
        </div>
      </main>
    </div>
  );
}
