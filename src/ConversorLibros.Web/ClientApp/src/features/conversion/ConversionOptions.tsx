import { Gauge, Languages, FolderArchive, FileAudio } from "lucide-react";
import { Card } from "../../shared/components/Card";

const speedPresets = [0.85, 1.0, 1.15, 1.25];

interface ConversionOptionsProps {
  speed: number;
  translate: boolean;
  sourceLanguage: string;
  format: "opus" | "m4b";
  disabled?: boolean;
  onSpeedChange: (speed: number) => void;
  onTranslateChange: (translate: boolean) => void;
  onSourceLanguageChange: (lang: string) => void;
  onFormatChange: (format: "opus" | "m4b") => void;
}

export function ConversionOptions({
  speed,
  translate,
  sourceLanguage,
  format,
  disabled = false,
  onSpeedChange,
  onTranslateChange,
  onSourceLanguageChange,
  onFormatChange,
}: ConversionOptionsProps) {
  return (
    <Card className="space-y-5">
      <div>
        <div className="flex items-center justify-between mb-2">
          <label className="inline-flex items-center gap-2 text-sm font-semibold text-[#EDF0F7]">
            <Gauge className="w-4 h-4 text-[#F59E0B]" />
            Velocidad de narración
          </label>
          <span className="font-mono text-xs font-semibold text-[#FBBF24]">
            {speed.toFixed(2)}x
          </span>
        </div>

        <div className="grid grid-cols-4 gap-1.5 mb-3">
          {speedPresets.map((preset) => {
            const active = Math.abs(speed - preset) < 0.01;
            return (
              <button
                key={preset}
                type="button"
                disabled={disabled}
                onClick={() => onSpeedChange(preset)}
                className={`py-1.5 rounded-lg text-xs font-mono transition-colors cursor-pointer disabled:opacity-50 ${
                  active
                    ? "bg-[#F59E0B] text-[#0B0D12] font-semibold"
                    : "bg-[#0E1118] text-[#93A0B8] border border-[#252C3D] hover:text-[#EDF0F7]"
                }`}
              >
                {preset.toFixed(preset === 1 ? 1 : 2)}x
              </button>
            );
          })}
        </div>

        <input
          type="range"
          min={0.5}
          max={1.5}
          step={0.05}
          value={speed}
          disabled={disabled}
          onChange={(e) => onSpeedChange(Number(e.target.value))}
          aria-label="Velocidad"
          className="w-full accent-[#F59E0B] cursor-pointer"
        />
      </div>

      <div className="pt-4 border-t border-[#252C3D]">
        <label className="flex items-center justify-between gap-3 cursor-pointer">
          <span className="inline-flex items-center gap-2 text-sm font-semibold text-[#EDF0F7]">
            <Languages className="w-4 h-4 text-[#F59E0B]" />
            Traducir con Ollama
          </span>
          <input
            type="checkbox"
            checked={translate}
            disabled={disabled}
            onChange={(e) => onTranslateChange(e.target.checked)}
            className="w-4 h-4 rounded accent-[#F59E0B] cursor-pointer"
          />
        </label>

        {translate && (
          <div className="mt-3 flex items-center gap-2 text-xs text-[#93A0B8]">
            <span>De</span>
            <input
              type="text"
              value={sourceLanguage}
              disabled={disabled}
              onChange={(e) => onSourceLanguageChange(e.target.value)}
              aria-label="Idioma de origen"
              className="w-20 px-2.5 py-1 rounded-lg bg-[#0E1118] border border-[#252C3D] focus:border-[#F59E0B] focus:outline-none font-mono text-xs text-[#EDF0F7] text-center"
            />
            <span>→</span>
            <span className="px-2.5 py-1 rounded-lg bg-[#1B2130] border border-[#2A3347] font-mono text-[#FBBF24]">
              es
            </span>
          </div>
        )}
      </div>

      <div className="pt-4 border-t border-[#252C3D]">
        <p className="text-sm font-semibold text-[#EDF0F7] mb-2.5">Formato de salida</p>
        <div className="grid grid-cols-2 gap-2.5">
          <button
            type="button"
            disabled={disabled}
            onClick={() => onFormatChange("opus")}
            className={`text-left p-3 rounded-xl border transition-all cursor-pointer disabled:opacity-50 ${
              format === "opus"
                ? "bg-[#1B2130] border-[#F59E0B] shadow-sm shadow-[#F59E0B]/10"
                : "bg-[#0E1118] border-[#252C3D] hover:border-[#3A4660]"
            }`}
          >
            <div className="flex items-center gap-2 text-xs font-semibold text-[#EDF0F7]">
              <FolderArchive className="w-4 h-4 text-[#F59E0B]" />
              Carpeta Opus
            </div>
            <p className="text-[11px] text-[#93A0B8] mt-1 leading-snug">
              Un .opus ligero por capítulo con portada.
            </p>
          </button>

          <button
            type="button"
            disabled={disabled}
            onClick={() => onFormatChange("m4b")}
            className={`text-left p-3 rounded-xl border transition-all cursor-pointer disabled:opacity-50 ${
              format === "m4b"
                ? "bg-[#1B2130] border-[#F59E0B] shadow-sm shadow-[#F59E0B]/10"
                : "bg-[#0E1118] border-[#252C3D] hover:border-[#3A4660]"
            }`}
          >
            <div className="flex items-center gap-2 text-xs font-semibold text-[#EDF0F7]">
              <FileAudio className="w-4 h-4 text-[#F59E0B]" />
              Fichero M4B
            </div>
            <p className="text-[11px] text-[#93A0B8] mt-1 leading-snug">
              Un solo archivo AAC con marcadores.
            </p>
          </button>
        </div>
      </div>
    </Card>
  );
}
