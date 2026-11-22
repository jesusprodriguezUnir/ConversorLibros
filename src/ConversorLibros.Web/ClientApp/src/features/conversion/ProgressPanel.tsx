import { useState } from "react";
import { Play, Square, Terminal, ChevronDown, ChevronUp, AlertTriangle, CheckCircle2 } from "lucide-react";
import type { ConversionStateDto } from "../../shared/api/types";
import { Badge } from "../../shared/components/Badge";
import { Button } from "../../shared/components/Button";
import { Card } from "../../shared/components/Card";

interface ProgressPanelProps {
  state: ConversionStateDto;
  canStart: boolean;
  isRunning: boolean;
  onStart: () => void;
  onCancel: () => void;
}

export function ProgressPanel({
  state,
  canStart,
  isRunning,
  onStart,
  onCancel,
}: ProgressPanelProps) {
  const [showLogs, setShowLogs] = useState(false);
  const percent = Math.round(state.progressFraction * 100);

  const badgeTone =
    state.status === "completed"
      ? "success"
      : state.status === "error"
      ? "error"
      : state.status === "running"
      ? "accent"
      : "neutral";

  return (
    <Card className="space-y-4">
      <div className="flex items-center justify-between gap-2">
        <div className="flex items-center gap-2">
          <h3 className="text-sm font-semibold text-[#EDF0F7]">Progreso</h3>
          <Badge tone={badgeTone}>{state.stage}</Badge>
        </div>
        <span className="font-mono text-sm font-semibold text-[#EDF0F7]">{percent}%</span>
      </div>

      <div className="h-2.5 rounded-full bg-[#0B0D12] border border-[#252C3D] overflow-hidden">
        <div
          className={`h-full rounded-full transition-all duration-300 ${
            state.status === "completed"
              ? "bg-emerald-400"
              : state.status === "error"
              ? "bg-rose-500"
              : "bg-gradient-to-r from-[#F59E0B] to-[#FBBF24]"
          }`}
          style={{ width: `${percent}%` }}
        />
      </div>

      {(state.currentChapter || state.timing) && (
        <div className="flex flex-wrap items-center justify-between gap-2 text-xs text-[#93A0B8]">
          <span>{state.currentChapter}</span>
          <span className="font-mono">{state.timing}</span>
        </div>
      )}

      {state.warning && (
        <div className="p-3 rounded-xl bg-amber-500/10 border border-amber-500/30 text-xs text-amber-200 flex items-start gap-2">
          <AlertTriangle className="w-4 h-4 text-amber-400 shrink-0 mt-0.5" />
          <span>{state.warning}</span>
        </div>
      )}

      {state.error && (
        <div className="p-3 rounded-xl bg-rose-500/10 border border-rose-500/30 text-xs text-rose-200 flex items-start gap-2">
          <AlertTriangle className="w-4 h-4 text-rose-400 shrink-0 mt-0.5" />
          <span>{state.error}</span>
        </div>
      )}

      {state.status === "completed" && state.outputPath && (
        <div className="p-3 rounded-xl bg-emerald-500/10 border border-emerald-500/30 text-xs text-emerald-200 flex items-start gap-2">
          <CheckCircle2 className="w-4 h-4 text-emerald-400 shrink-0 mt-0.5" />
          <span className="font-mono break-all">{state.outputPath}</span>
        </div>
      )}

      <div className="flex flex-wrap items-center gap-2 pt-1">
        <Button
          variant="primary"
          size="md"
          disabled={!canStart}
          icon={<Play className="w-4 h-4" />}
          onClick={onStart}
          className="flex-1"
        >
          Empezar
        </Button>

        {isRunning && (
          <Button
            variant="danger"
            size="md"
            icon={<Square className="w-3.5 h-3.5" />}
            onClick={onCancel}
          >
            Cancelar
          </Button>
        )}

        <Button
          variant="secondary"
          size="md"
          icon={<Terminal className="w-4 h-4" />}
          onClick={() => setShowLogs((v) => !v)}
        >
          Registro
          {showLogs ? <ChevronUp className="w-3.5 h-3.5" /> : <ChevronDown className="w-3.5 h-3.5" />}
        </Button>
      </div>

      {showLogs && (
        <div className="rounded-xl bg-[#090B0F] border border-[#252C3D] p-3 max-h-48 overflow-y-auto font-mono text-[11px] text-[#93A0B8] space-y-1">
          {state.logs.length === 0 ? (
            <p className="text-[#64728C]">Sin entradas todavía.</p>
          ) : (
            state.logs.map((line, i) => (
              <div key={i} className="whitespace-pre-wrap break-words">
                {line}
              </div>
            ))
          )}
        </div>
      )}
    </Card>
  );
}
