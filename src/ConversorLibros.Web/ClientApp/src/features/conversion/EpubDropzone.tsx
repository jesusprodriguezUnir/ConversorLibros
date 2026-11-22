import { useRef, useState, type DragEvent, type ChangeEvent } from "react";
import { Upload, RefreshCcw, Loader2, CheckCircle2 } from "lucide-react";
import type { InspectedBookDto } from "../../shared/api/types";
import { Button } from "../../shared/components/Button";
import { Card } from "../../shared/components/Card";

interface EpubDropzoneProps {
  book: InspectedBookDto | null;
  loading: boolean;
  disabled?: boolean;
  onSelectFile: (file: File) => void;
}

export function EpubDropzone({ book, loading, disabled = false, onSelectFile }: EpubDropzoneProps) {
  const inputRef = useRef<HTMLInputElement | null>(null);
  const [dragging, setDragging] = useState(false);

  const handleFile = (file: File | undefined) => {
    if (!file || disabled || loading) return;
    onSelectFile(file);
  };

  const handleChange = (e: ChangeEvent<HTMLInputElement>) => {
    handleFile(e.target.files?.[0]);
    e.target.value = "";
  };

  const handleDrop = (e: DragEvent<HTMLDivElement>) => {
    e.preventDefault();
    setDragging(false);
    handleFile(e.dataTransfer.files?.[0]);
  };

  return (
    <Card>
      <input
        ref={inputRef}
        type="file"
        accept=".epub"
        aria-label="Seleccionar archivo EPUB"
        onChange={handleChange}
        className="sr-only"
      />

      {!book ? (
        <div
          onDragOver={(e) => {
            e.preventDefault();
            if (!disabled) setDragging(true);
          }}
          onDragLeave={() => setDragging(false)}
          onDrop={handleDrop}
          onClick={() => !disabled && !loading && inputRef.current?.click()}
          className={`rounded-xl border-2 border-dashed p-10 text-center transition-all cursor-pointer ${
            dragging
              ? "border-[#F59E0B] bg-[#F59E0B]/10"
              : "border-[#2A3347] hover:border-[#F59E0B]/60 bg-[#0E1118]"
          } ${disabled ? "opacity-50 cursor-not-allowed" : ""}`}
        >
          <div className="w-12 h-12 rounded-2xl bg-[#1B2130] border border-[#2A3347] flex items-center justify-center mx-auto mb-3">
            {loading ? (
              <Loader2 className="w-6 h-6 text-[#F59E0B] animate-spin" />
            ) : (
              <Upload className="w-6 h-6 text-[#F59E0B]" />
            )}
          </div>
          <p className="text-sm font-semibold text-[#EDF0F7]">
            {loading ? "Analizando EPUB…" : "Arrastra tu archivo EPUB aquí"}
          </p>
          <p className="text-xs text-[#93A0B8] mt-1">
            o haz clic para elegir un libro sin DRM desde tu equipo
          </p>
        </div>
      ) : (
        <div className="flex items-center justify-between gap-3">
          <p className="flex items-center gap-2 text-xs text-[#93A0B8]">
            {loading ? (
              <Loader2 className="w-3.5 h-3.5 text-[#F59E0B] animate-spin" />
            ) : (
              <CheckCircle2 className="w-3.5 h-3.5 text-emerald-400" />
            )}
            {loading ? "Analizando EPUB…" : "Libro cargado"}
          </p>

          <Button
            variant="secondary"
            size="sm"
            disabled={disabled || loading}
            loading={loading}
            icon={<RefreshCcw className="w-3.5 h-3.5" />}
            onClick={() => inputRef.current?.click()}
          >
            Cambiar libro
          </Button>
        </div>
      )}
    </Card>
  );
}
