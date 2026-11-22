import { useEffect, useState } from "react";
import { FolderSearch, Save, Plug, Cpu, HardDrive, Bot } from "lucide-react";
import { api } from "../../shared/api/client";
import type { SettingsResponse, TestOllamaResponse, WebSettings } from "../../shared/api/types";
import { Button } from "../../shared/components/Button";
import { Card } from "../../shared/components/Card";
import { useToast } from "../../shared/components/Toast";
import { HealthBadge } from "./HealthBadge";
import { FolderBrowserModal } from "./FolderBrowserModal";

type BrowseTarget = "piper" | "output" | null;

export function SettingsPage() {
  const { showToast } = useToast();

  // Real defaults (Ollama URL/model, speed, format) come from GET /api/settings; this is only the pre-load shape.
  const [form, setForm] = useState<WebSettings>({
    piperModelDir: "",
    outputRoot: "",
    ollamaUrl: "",
    ollamaModel: "",
    ffmpegOverride: "",
    defaultSpeed: 1.0,
    defaultFormat: "opus",
  });
  const [status, setStatus] = useState<SettingsResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);

  const [testingOllama, setTestingOllama] = useState(false);
  const [ollamaResult, setOllamaResult] = useState<TestOllamaResponse | null>(null);
  const [browseTarget, setBrowseTarget] = useState<BrowseTarget>(null);

  useEffect(() => {
    api
      .getSettings()
      .then((res) => {
        setStatus(res);
        setForm(res.settings);
      })
      .catch((err) => {
        showToast(err instanceof Error ? err.message : "No se pudieron cargar los ajustes.", "error");
      })
      .finally(() => setLoading(false));
  }, [showToast]);

  const handleSave = async () => {
    setSaving(true);
    try {
      const updated = await api.saveSettings(form);
      setStatus(updated);
      setForm(updated.settings);
      showToast(
        updated.piper.isValid
          ? "Ajustes guardados correctamente."
          : `Ajustes guardados: ${updated.piper.message}`,
        updated.piper.isValid ? "success" : "info"
      );
    } catch (err) {
      showToast(err instanceof Error ? err.message : "No se pudieron guardar los ajustes.", "error");
    } finally {
      setSaving(false);
    }
  };

  const handleTestOllama = async () => {
    setTestingOllama(true);
    try {
      const result = await api.testOllama(form.ollamaUrl, form.ollamaModel);
      setOllamaResult(result);
    } catch (err) {
      setOllamaResult({
        connected: false,
        modelInstalled: false,
        models: [],
        message: err instanceof Error ? err.message : "Error al probar conexión.",
      });
    } finally {
      setTestingOllama(false);
    }
  };

  return (
    <div className="space-y-6 max-w-4xl">
      <header className="flex flex-col sm:flex-row sm:items-end justify-between gap-4">
        <div>
          <h1 className="text-2xl font-bold tracking-tight text-[#EDF0F7]">Ajustes</h1>
          <p className="text-sm text-[#93A0B8] mt-1">
            Configura el motor de voz Piper, la carpeta de salida y la traducción local con Ollama.
          </p>
        </div>

        <Button
          variant="primary"
          size="md"
          loading={saving}
          disabled={loading}
          icon={<Save className="w-4 h-4" />}
          onClick={() => void handleSave()}
        >
          Guardar cambios
        </Button>
      </header>

      <Card className="space-y-4">
        <div className="flex items-center justify-between gap-3">
          <div className="flex items-center gap-2.5">
            <div className="w-8 h-8 rounded-lg bg-[#1B2130] border border-[#2A3347] flex items-center justify-center">
              <Cpu className="w-4 h-4 text-[#F59E0B]" />
            </div>
            <div>
              <h2 className="text-sm font-semibold text-[#EDF0F7]">Motor de voz (Piper)</h2>
              <p className="text-xs text-[#93A0B8]">
                Carpeta con el modelo <code className="font-mono">.onnx</code>,{" "}
                <code className="font-mono">tokens.txt</code> y{" "}
                <code className="font-mono">espeak-ng-data</code>
              </p>
            </div>
          </div>
          {status && <HealthBadge status={status.piper.isValid ? "ready" : "warning"} />}
        </div>

        <div className="flex gap-2">
          <input
            type="text"
            value={form.piperModelDir}
            onChange={(e) => setForm((f) => ({ ...f, piperModelDir: e.target.value }))}
            placeholder="D:\Modelos\es_ES-davefx-medium"
            aria-label="Carpeta del modelo Piper"
            className="flex-1 px-3.5 py-2 rounded-xl bg-[#0B0D12] border border-[#252C3D] focus:border-[#F59E0B] focus:outline-none font-mono text-xs text-[#EDF0F7]"
          />
          <Button
            variant="secondary"
            size="md"
            icon={<FolderSearch className="w-4 h-4" />}
            onClick={() => setBrowseTarget("piper")}
          >
            Examinar
          </Button>
        </div>

        {status && (
          <p className="text-xs text-sky-300">
            {status.piperAutoDetected
              ? `Detectado automáticamente: ${status.effectivePiperModelDir}`
              : status.effectivePiperModelDir
                ? `Configurado manualmente: ${status.effectivePiperModelDir}`
                : "Sin modelo configurado."}
          </p>
        )}

        {status && (
          <p className={`text-xs ${status.piper.isValid ? "text-emerald-300" : "text-amber-300"}`}>
            {status.piper.message}
          </p>
        )}
      </Card>

      <Card className="space-y-4">
        <div className="flex items-center justify-between gap-3">
          <div className="flex items-center gap-2.5">
            <div className="w-8 h-8 rounded-lg bg-[#1B2130] border border-[#2A3347] flex items-center justify-center">
              <HardDrive className="w-4 h-4 text-[#F59E0B]" />
            </div>
            <div>
              <h2 className="text-sm font-semibold text-[#EDF0F7]">Carpeta de salida y ffmpeg</h2>
              <p className="text-xs text-[#93A0B8]">
                Dónde se guardan los audiolibros, la caché y el codificador de audio
              </p>
            </div>
          </div>
          {status && <HealthBadge status={status.ffmpeg.found ? "ready" : "missing"} />}
        </div>

        <div className="space-y-3">
          <div>
            <label className="block text-xs text-[#93A0B8] mb-1.5">
              Carpeta de salida (audiolibros y caché)
            </label>
            <div className="flex gap-2">
              <input
                type="text"
                value={form.outputRoot}
                onChange={(e) => setForm((f) => ({ ...f, outputRoot: e.target.value }))}
                aria-label="Carpeta de salida"
                className="flex-1 px-3.5 py-2 rounded-xl bg-[#0B0D12] border border-[#252C3D] focus:border-[#F59E0B] focus:outline-none font-mono text-xs text-[#EDF0F7]"
              />
              <Button
                variant="secondary"
                size="md"
                icon={<FolderSearch className="w-4 h-4" />}
                onClick={() => setBrowseTarget("output")}
              >
                Examinar
              </Button>
            </div>
          </div>

          <div>
            <label className="block text-xs text-[#93A0B8] mb-1.5">
              Ruta manual de ffmpeg (vacío = detección automática)
            </label>
            <input
              type="text"
              value={form.ffmpegOverride}
              onChange={(e) => setForm((f) => ({ ...f, ffmpegOverride: e.target.value }))}
              placeholder="Automático (PATH / WinGet)"
              aria-label="Ruta manual de ffmpeg"
              className="w-full px-3.5 py-2 rounded-xl bg-[#0B0D12] border border-[#252C3D] focus:border-[#F59E0B] focus:outline-none font-mono text-xs text-[#EDF0F7]"
            />
            {status && (
              <p
                className={`text-xs mt-1.5 font-mono ${
                  status.ffmpeg.found ? "text-[#93A0B8]" : "text-rose-300"
                }`}
              >
                {status.ffmpeg.message}
              </p>
            )}
          </div>

          <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
            <div>
              <label className="block text-xs text-[#93A0B8] mb-1.5">Velocidad por defecto</label>
              <input
                type="number"
                min={0.5}
                max={1.5}
                step={0.05}
                value={form.defaultSpeed}
                onChange={(e) =>
                  setForm((f) => ({ ...f, defaultSpeed: Number(e.target.value) }))
                }
                aria-label="Velocidad por defecto"
                className="w-full px-3.5 py-2 rounded-xl bg-[#0B0D12] border border-[#252C3D] focus:border-[#F59E0B] focus:outline-none font-mono text-xs text-[#EDF0F7]"
              />
            </div>

            <div>
              <label className="block text-xs text-[#93A0B8] mb-1.5">Formato por defecto</label>
              <select
                value={form.defaultFormat}
                onChange={(e) =>
                  setForm((f) => ({ ...f, defaultFormat: e.target.value as WebSettings["defaultFormat"] }))
                }
                aria-label="Formato por defecto"
                className="w-full px-3.5 py-2 rounded-xl bg-[#0B0D12] border border-[#252C3D] focus:border-[#F59E0B] focus:outline-none font-mono text-xs text-[#EDF0F7]"
              >
                <option value="opus">Opus (carpeta)</option>
                <option value="m4b">M4B (archivo único)</option>
              </select>
            </div>
          </div>
        </div>
      </Card>

      <Card className="space-y-4">
        <div className="flex items-center justify-between gap-3">
          <div className="flex items-center gap-2.5">
            <div className="w-8 h-8 rounded-lg bg-[#1B2130] border border-[#2A3347] flex items-center justify-center">
              <Bot className="w-4 h-4 text-[#F59E0B]" />
            </div>
            <div>
              <h2 className="text-sm font-semibold text-[#EDF0F7]">Traducción local (Ollama)</h2>
              <p className="text-xs text-[#93A0B8]">
                Opcional para traducir libros de otro idioma al español antes de sintetizar
              </p>
            </div>
          </div>
          {ollamaResult && (
            <HealthBadge
              status={
                ollamaResult.connected
                  ? ollamaResult.modelInstalled
                    ? "ready"
                    : "warning"
                  : "missing"
              }
              label={
                ollamaResult.connected
                  ? ollamaResult.modelInstalled
                    ? "Conectado"
                    : "Modelo faltante"
                  : "Sin conexión"
              }
            />
          )}
        </div>

        <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
          <div>
            <label className="block text-xs text-[#93A0B8] mb-1.5">Endpoint</label>
            <input
              type="text"
              value={form.ollamaUrl}
              onChange={(e) => setForm((f) => ({ ...f, ollamaUrl: e.target.value }))}
              aria-label="Endpoint de Ollama"
              className="w-full px-3.5 py-2 rounded-xl bg-[#0B0D12] border border-[#252C3D] focus:border-[#F59E0B] focus:outline-none font-mono text-xs text-[#EDF0F7]"
            />
          </div>

          <div>
            <label className="block text-xs text-[#93A0B8] mb-1.5">Modelo</label>
            <input
              type="text"
              value={form.ollamaModel}
              onChange={(e) => setForm((f) => ({ ...f, ollamaModel: e.target.value }))}
              aria-label="Modelo de Ollama"
              className="w-full px-3.5 py-2 rounded-xl bg-[#0B0D12] border border-[#252C3D] focus:border-[#F59E0B] focus:outline-none font-mono text-xs text-[#EDF0F7]"
            />
          </div>
        </div>

        {ollamaResult && ollamaResult.models.length > 0 && (
          <div>
            <label className="block text-xs text-[#93A0B8] mb-1.5">
              Modelos detectados en Ollama
            </label>
            <select
              value={form.ollamaModel}
              onChange={(e) => setForm((f) => ({ ...f, ollamaModel: e.target.value }))}
              aria-label="Modelos detectados en Ollama"
              className="w-full px-3.5 py-2 rounded-xl bg-[#0B0D12] border border-[#252C3D] focus:border-[#F59E0B] focus:outline-none font-mono text-xs text-[#EDF0F7]"
            >
              {ollamaResult.models.map((m) => (
                <option key={m} value={m}>
                  {m}
                </option>
              ))}
            </select>
          </div>
        )}

        <div className="flex flex-wrap items-center gap-3">
          <Button
            variant="secondary"
            size="sm"
            loading={testingOllama}
            icon={<Plug className="w-3.5 h-3.5" />}
            onClick={() => void handleTestOllama()}
          >
            Probar conexión
          </Button>
          {ollamaResult && (
            <span
              className={`text-xs ${
                ollamaResult.connected ? "text-emerald-300" : "text-rose-300"
              }`}
            >
              {ollamaResult.message}
            </span>
          )}
        </div>
      </Card>

      <FolderBrowserModal
        open={browseTarget !== null}
        title={
          browseTarget === "piper"
            ? "Seleccionar carpeta del modelo Piper"
            : "Seleccionar carpeta de salida"
        }
        initialPath={browseTarget === "piper" ? form.piperModelDir : form.outputRoot}
        onClose={() => setBrowseTarget(null)}
        onSelect={(path) => {
          if (browseTarget === "piper") {
            setForm((f) => ({ ...f, piperModelDir: path }));
          } else if (browseTarget === "output") {
            setForm((f) => ({ ...f, outputRoot: path }));
          }
        }}
      />
    </div>
  );
}
