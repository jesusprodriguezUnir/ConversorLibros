import { BrowserRouter, Navigate, Route, Routes } from "react-router-dom";
import { ToastProvider } from "../shared/components/Toast";
import { ConversionProvider } from "./ConversionContext";
import { AppShell } from "./AppShell";
import { LibraryPage } from "../features/library/LibraryPage";
import { NewConversionPage } from "../features/conversion/NewConversionPage";
import { SettingsPage } from "../features/settings/SettingsPage";

export function App() {
  return (
    <BrowserRouter>
      <ToastProvider>
        <ConversionProvider>
          <Routes>
            <Route element={<AppShell />}>
              <Route path="/" element={<Navigate to="/biblioteca" replace />} />
              <Route path="/biblioteca" element={<LibraryPage />} />
              <Route path="/convertir" element={<NewConversionPage />} />
              <Route path="/ajustes" element={<SettingsPage />} />
              <Route path="*" element={<Navigate to="/biblioteca" replace />} />
            </Route>
          </Routes>
        </ConversionProvider>
      </ToastProvider>
    </BrowserRouter>
  );
}
