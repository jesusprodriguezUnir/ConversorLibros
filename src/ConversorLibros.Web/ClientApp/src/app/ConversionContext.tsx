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

  const refreshState = useCallback(async () => {
    try {
      const current = await api.getCurrentConversion();
      setState(current);
    } catch {
      // Ignore transient startup errors; SSE will resync once backend is reachable.
    }
  }, []);

  useEffect(() => {
    void refreshState();

    const source = new EventSource("/api/conversions/events");
    const handleEvent = (event: MessageEvent) => {
      try {
        const next = JSON.parse(event.data) as ConversionStateDto;
        setState(next);
      } catch {
        // Ignore malformed payloads.
      }
    };

    for (const type of ["snapshot", "progress", "log", "completed", "cancelled", "error"]) {
      source.addEventListener(type, handleEvent);
    }

    return () => {
      source.close();
    };
  }, [refreshState]);

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
