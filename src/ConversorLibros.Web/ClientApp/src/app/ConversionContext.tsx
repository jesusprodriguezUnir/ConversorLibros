import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useState,
  type ReactNode,
} from "react";
import { api } from "../shared/api/client";
import type { ConversionStateDto, StartConversionRequest } from "../shared/api/types";
import { useToast } from "../shared/components/Toast";

const initialState: ConversionStateDto = {
  status: "idle",
  stage: "Inactivo",
  progressFraction: 0,
  currentChapter: "",
  timing: "",
  bookTitle: null,
  outputPath: null,
  warning: null,
  error: null,
  logs: [],
};

interface ConversionContextValue {
  state: ConversionStateDto;
  isRunning: boolean;
  refreshState: () => Promise<void>;
  startConversion: (request: StartConversionRequest) => Promise<ConversionStateDto>;
  cancelConversion: () => Promise<void>;
}

const ConversionContext = createContext<ConversionContextValue | null>(null);

export function ConversionProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<ConversionStateDto>(initialState);
  const { showToast } = useToast();

  const refreshState = useCallback(async () => {
    try {
      const current = await api.getCurrentConversion();
      setState(current);
    } catch (err) {
      showToast(
        err instanceof Error ? err.message : "No se pudo obtener el estado de la conversión.",
        "error"
      );
    }
  }, [showToast]);

  useEffect(() => {
    void refreshState();

    // EventSource reconnects on its own, so transport errors stay quiet; only bad payloads are surfaced.
    const source = new EventSource("/api/conversions/events");
    const handleEvent = (event: MessageEvent) => {
      try {
        const next = JSON.parse(event.data) as ConversionStateDto;
        setState(next);
      } catch {
        showToast("Se recibió un evento de conversión no válido.", "error");
      }
    };

    for (const type of ["snapshot", "progress", "log", "completed", "cancelled", "error"]) {
      source.addEventListener(type, handleEvent);
    }

    return () => {
      source.close();
    };
  }, [refreshState, showToast]);

  const startConversion = useCallback(async (request: StartConversionRequest) => {
    const next = await api.startConversion(request);
    setState(next);
    return next;
  }, []);

  const cancelConversion = useCallback(async () => {
    const next = await api.cancelConversion();
    setState(next);
  }, []);

  return (
    <ConversionContext.Provider
      value={{
        state,
        isRunning: state.status === "running",
        refreshState,
        startConversion,
        cancelConversion,
      }}
    >
      {children}
    </ConversionContext.Provider>
  );
}

export function useConversion() {
  const ctx = useContext(ConversionContext);
  if (!ctx) {
    throw new Error("useConversion must be used within ConversionProvider");
  }
  return ctx;
}
