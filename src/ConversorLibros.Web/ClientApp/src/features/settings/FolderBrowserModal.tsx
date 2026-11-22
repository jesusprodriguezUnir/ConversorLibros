import { useCallback, useEffect, useState } from "react";
import { Folder, HardDrive, ArrowUp, Loader2 } from "lucide-react";
import { api } from "../../shared/api/client";
import type { FileSystemBrowseResponse } from "../../shared/api/types";
import { Button } from "../../shared/components/Button";
import { Modal } from "../../shared/components/Modal";

interface FolderBrowserModalProps {
  open: boolean;
  initialPath?: string;
  title: string;
  onSelect: (path: string) => void;
  onClose: () => void;
}

export function FolderBrowserModal({
  open,
  initialPath,
  title,
  onSelect,
  onClose,
}: FolderBrowserModalProps) {
  const [data, setData] = useState<FileSystemBrowseResponse | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const browse = useCallback(async (path?: string) => {
    setLoading(true);
    setError(null);
    try {
      const res = await api.browseFileSystem(path);
      setData(res);
    } catch (err) {
      const message = err instanceof Error ? err.message : "No se pudo explorar el equipo.";
      // Fall back to the drive list when the requested path can't be opened, and say so.
      if (path) {
        try {
          const drives = await api.browseFileSystem();
          setData(drives);
          setError(`${message} Se muestran las unidades del equipo.`);
        } catch (driveErr) {
          setError(driveErr instanceof Error ? driveErr.message : "No se pudo explorar el equipo.");
        }
      } else {
        setError("No se pudo explorar el equipo.");
      }
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    if (open) {
      void browse(initialPath?.trim() || undefined);
    }
  }, [open, initialPath, browse]);

  return (
    <Modal
      open={open}
      title={title}
      onClose={onClose}
      footer={
        <>
          <Button variant="ghost" size="sm" onClick={onClose}>
            Cancelar
          </Button>
          <Button
            variant="primary"
            size="sm"
            disabled={!data?.currentPath}
            onClick={() => {
              if (data?.currentPath) {
                onSelect(data.currentPath);
                onClose();
              }
            }}
          >
            Seleccionar esta carpeta
          </Button>
        </>
      }
    >
      <div className="space-y-3">
        <div className="flex items-center gap-2">
          <Button
            variant="secondary"
            size="sm"
            disabled={loading || !data?.currentPath}
            icon={<ArrowUp className="w-3.5 h-3.5" />}
            onClick={() => void browse(data?.parentPath ?? undefined)}
          >
            Subir
          </Button>
          <Button
            variant="ghost"
            size="sm"
            disabled={loading || !data?.currentPath}
            icon={<HardDrive className="w-3.5 h-3.5" />}
            onClick={() => void browse(undefined)}
          >
            Unidades
          </Button>
          <div className="flex-1 px-3 py-1.5 rounded-lg bg-[#0B0D12] border border-[#252C3D] font-mono text-xs text-[#93A0B8] truncate">
            {data?.currentPath ?? "Equipo (unidades)"}
          </div>
        </div>

        {error && <p className="text-xs text-rose-300">{error}</p>}

        <div className="border border-[#252C3D] rounded-xl bg-[#0B0D12] max-h-72 overflow-y-auto divide-y divide-[#1A2030]">
          {loading ? (
            <div className="py-10 flex items-center justify-center text-xs text-[#93A0B8] gap-2">
              <Loader2 className="w-4 h-4 animate-spin text-[#F59E0B]" />
              Leyendo carpetas…
            </div>
          ) : !data || data.directories.length === 0 ? (
            <div className="py-10 text-center text-xs text-[#64728C]">
              No hay subcarpetas en esta ubicación.
            </div>
          ) : (
            data.directories.map((dir) => (
              <button
                key={dir.path}
                type="button"
                onClick={() => void browse(dir.path)}
                className="w-full flex items-center gap-3 px-3.5 py-2.5 text-left text-sm text-[#EDF0F7] hover:bg-[#161C28] transition-colors cursor-pointer"
              >
                {data.currentPath ? (
                  <Folder className="w-4 h-4 text-[#F59E0B] shrink-0" />
                ) : (
                  <HardDrive className="w-4 h-4 text-[#F59E0B] shrink-0" />
                )}
                <span className="truncate">{dir.name}</span>
              </button>
            ))
          )}
        </div>
      </div>
    </Modal>
  );
}
