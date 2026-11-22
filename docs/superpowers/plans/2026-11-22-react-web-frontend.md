# Plan de implementación: Nueva interfaz web en React para ConversorLibros (Fase 1)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Construir el nuevo host web local en ASP.NET Core (`ConversorLibros.Web`) y el frontend modular en React + TypeScript + Tailwind CSS con estética "Dark Studio" editorial, cubriendo Biblioteca, Nueva conversión y Ajustes sobre `ConversorLibros.Core`.

**Architecture:** Un proyecto ASP.NET Core Minimal API (`src/ConversorLibros.Web`, `net10.0`) reutiliza `ConversorLibros.Core`, persiste ajustes en JSON local, ejecuta conversiones en segundo plano difundiendo eventos por SSE y sirve el build estático de una SPA React/Vite/Tailwind (`src/ConversorLibros.Web/ClientApp`) desde `wwwroot/`. `ConversorLibros.App` (MAUI) permanece intacto como respaldo.

**Tech Stack:** .NET 10, ASP.NET Core Minimal API, xUnit, `Microsoft.AspNetCore.Mvc.Testing`, React 19, TypeScript, Vite, Tailwind CSS v4, `lucide-react`, `react-router-dom`, Vitest, Testing Library.

**Spec:** `docs/superpowers/specs/2026-11-22-react-web-frontend-design.md`

## Global Constraints

- Plataforma: Windows x64, uso personal 100 % offline, .NET 10 (`net10.0`).
- Dependencia fijada: `org.k2fsa.sherpa.onnx.runtime.win-x64` en versión exacta `1.13.8` (en sincronía con `ConversorLibros.Core`).
- Idioma: interfaz de usuario y documentación en español; código, identificadores y comentarios en inglés.
- No duplicar lógica del pipeline fuera de `ConversorLibros.Core` ni alterar invariantes de caché/hashes ni los tests de `ConversorLibros.Core.Tests`.
- Piper TTS es síncrono: ejecutar el pipeline dentro de `Task.Run`.
- Procesos externos (`explorer.exe`, `ffmpeg`): usar siempre `ProcessStartInfo.ArgumentList`, nunca cadenas concatenadas.
- No modificar ni eliminar `src/ConversorLibros.App`.
- Puerto por defecto del backend: `http://localhost:5180`; Vite dev en `http://localhost:5173` con proxy `/api`.

## Review Focus

1. **EPUB corrupto o no válido en `POST /api/books/inspect`:** debe responder HTTP `400` con mensaje claro en español y limpiar el archivo temporal sin lanzar `500` (cubierto en Task 2).
2. **Doble inicio de conversión en `POST /api/conversions`:** iniciar una conversión mientras otra está en curso debe responder HTTP `409 Conflict` sin alterar el trabajo activo (cubierto en Task 3).
3. **Recarga del navegador o cambio de vista durante una conversión:** `ConversionContext` debe hidratar estado, progreso y logs desde `GET /api/conversions/current` y reconectarse a SSE sin perder datos (cubierto en Task 4).
4. **Ruta inexistente o inaccesible en `GET /api/fs/browse`:** debe responder HTTP `400` con mensaje controlado en lugar de `500` (cubierto en Task 1).
5. **Subcarpeta de modelo Piper (p. ej. `espeak-ng-data`) o carpeta incompleta en `PUT /api/settings`:** debe normalizar a la carpeta padre con `.onnx` cuando corresponda e informar exactamente qué archivos faltan cuando sea inválida (cubierto en Task 1).

---

## Mapa de archivos

### Backend (`src/ConversorLibros.Web` y `tests/ConversorLibros.Web.Tests`)
- `src/ConversorLibros.Web/ConversorLibros.Web.csproj` — Proyecto ASP.NET Core `net10.0`, referencia `ConversorLibros.Core` y `org.k2fsa.sherpa.onnx.runtime.win-x64` (`1.13.8`).
- `src/ConversorLibros.Web/Program.cs` — Configuración DI, endpoints `/api`, archivos estáticos (`wwwroot/`) y fallback SPA.
- `src/ConversorLibros.Web/Models/ApiDtos.cs` — Contratos JSON de entrada/salida para Settings, FileSystem, Library, Books y Conversions.
- `src/ConversorLibros.Web/Services/JsonSettingsStore.cs` — Persistencia en `%LocalAppData%\ConversorLibros\settings.json`, normalización y validación de Piper y `ffmpeg`.
- `src/ConversorLibros.Web/Services/UploadedBookStore.cs` — Almacenamiento temporal de `.epub` en `%TEMP%\ConversorLibros\uploads\`, parseo con `EpubBookSource` y limpieza.
- `src/ConversorLibros.Web/Services/ConversionRunner.cs` — Interfaz `IConversionRunner` + implementación real `CoreConversionRunner` que compone el pipeline de `ConversorLibros.Core`.
- `src/ConversorLibros.Web/Services/ConversionJobManager.cs` — Singleton de trabajo único con `Task.Run`, `CancellationTokenSource`, registro en memoria y broadcast SSE vía `Channel<ConversionEvent>`.
- `src/ConversorLibros.Web/Endpoints/SettingsEndpoints.cs` — `GET/PUT /api/settings`, `POST /api/settings/test-ollama`.
- `src/ConversorLibros.Web/Endpoints/FileSystemEndpoints.cs` — `GET /api/fs/browse`.
- `src/ConversorLibros.Web/Endpoints/LibraryEndpoints.cs` — `GET /api/library`, `GET /api/library/{id}/cover`, `POST /api/library/{id}/open`.
- `src/ConversorLibros.Web/Endpoints/BookEndpoints.cs` — `POST /api/books/inspect`, `GET /api/books/{uploadId}/cover`.
- `src/ConversorLibros.Web/Endpoints/ConversionEndpoints.cs` — `POST /api/conversions`, `GET /api/conversions/current`, `GET /api/conversions/events`, `POST /api/conversions/cancel`.
- `tests/ConversorLibros.Web.Tests/` — Pruebas unitarias y de integración HTTP (`WebApplicationFactory<Program>`).

### Frontend (`src/ConversorLibros.Web/ClientApp`)
- `package.json`, `vite.config.ts`, `tsconfig.json`, `index.html`, `src/index.css`
- `src/shared/api/types.ts`, `src/shared/api/client.ts`
- `src/shared/components/Button.tsx`, `Card.tsx`, `Badge.tsx`, `Modal.tsx`, `Toast.tsx`
- `src/shared/utils/format.ts`
- `src/app/ConversionContext.tsx`, `src/app/AppShell.tsx`, `src/app/App.tsx`
- `src/features/library/LibraryPage.tsx`, `BookCard.tsx`
- `src/features/conversion/NewConversionPage.tsx`, `EpubDropzone.tsx`, `ChapterList.tsx`, `ConversionOptions.tsx`, `ProgressPanel.tsx`
- `src/features/settings/SettingsPage.tsx`, `HealthBadge.tsx`, `FolderBrowserModal.tsx`

---

### Task 1: Scaffolding del host ASP.NET Core, `JsonSettingsStore` y explorador de carpetas

**Files:**
- Modify: `.gitignore`
- Modify: `ConversorLibros.sln`
- Create: `src/ConversorLibros.Web/ConversorLibros.Web.csproj`
- Create: `src/ConversorLibros.Web/Models/ApiDtos.cs`
- Create: `src/ConversorLibros.Web/Services/JsonSettingsStore.cs`
- Create: `src/ConversorLibros.Web/Endpoints/SettingsEndpoints.cs`
- Create: `src/ConversorLibros.Web/Endpoints/FileSystemEndpoints.cs`
- Create: `src/ConversorLibros.Web/Program.cs`
- Create: `tests/ConversorLibros.Web.Tests/ConversorLibros.Web.Tests.csproj`
- Create: `tests/ConversorLibros.Web.Tests/SettingsAndFileSystemTests.cs`

**Interfaces:**
- Consumes: `ConversorLibros.Core.Services.FfmpegLocator.Locate()`
- Produces:
  - `WebSettings(string PiperModelDir, string OutputRoot, string OllamaUrl, string OllamaModel, string FfmpegOverride)`
  - `PiperValidationStatus(bool IsValid, string Message)`
  - `FfmpegStatus(bool Found, string? ResolvedPath, string Message)`
  - `SettingsResponse(WebSettings Settings, PiperValidationStatus Piper, FfmpegStatus Ffmpeg)`
  - `JsonSettingsStore` (`Get()`, `Save(WebSettings)`, `ValidatePiper(string)`, `ResolveFfmpeg(string)`)
  - Endpoints `GET /api/settings`, `PUT /api/settings`, `POST /api/settings/test-ollama`, `GET /api/fs/browse`
  - `public partial class Program;` para pruebas con `WebApplicationFactory<Program>`

- [ ] **Step 1: Crear proyectos `.csproj`, agregarlos a `ConversorLibros.sln` y actualizar `.gitignore`**

  - Añadir `node_modules/` y `src/ConversorLibros.Web/wwwroot/` a `.gitignore`.
  - Crear `src/ConversorLibros.Web/ConversorLibros.Web.csproj` (`Microsoft.NET.Sdk.Web`, `net10.0`, referencia a `ConversorLibros.Core` y `org.k2fsa.sherpa.onnx.runtime.win-x64` versión `1.13.8`).
  - Crear `tests/ConversorLibros.Web.Tests/ConversorLibros.Web.Tests.csproj` (`net10.0`, `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `Microsoft.AspNetCore.Mvc.Testing`, referencia a `ConversorLibros.Web`).
  - Registrar ambos proyectos en `ConversorLibros.sln` con `dotnet sln add`.

- [ ] **Step 2: Escribir los tests fallidos en `tests/ConversorLibros.Web.Tests/SettingsAndFileSystemTests.cs`**

  ```csharp
  [Fact] void NormalizePiperDir_SubfolderMapsToParentContainingOnnx()
  [Fact] void ValidatePiper_MissingFilesReportsExactMissingItems()
  [Fact] void SaveAndGet_PersistsSettingsToJsonFile()
  [Fact] async Task GetBrowse_InvalidPathReturnsBadRequest()
  [Fact] async Task GetBrowse_ValidDirectoryListsSubdirectoriesAndParent()
  [Fact] async Task GetBrowse_EmptyPathReturnsLogicalDrives()
  [Fact] async Task TestOllama_UnreachableEndpointReturnsConnectedFalse()
  ```

- [ ] **Step 3: Ejecutar los tests para verificar que fallan**

  Run: `dotnet test tests/ConversorLibros.Web.Tests`
  Expected: FAIL (tipos y endpoints aún no definidos).

- [ ] **Step 4: Implementar `ApiDtos.cs`, `JsonSettingsStore.cs`, `SettingsEndpoints.cs`, `FileSystemEndpoints.cs` y `Program.cs`**

  - `JsonSettingsStore`: constructor recibe ruta del archivo JSON (en producción `%LocalAppData%\ConversorLibros\settings.json`; inyectable para tests). Valores por defecto idénticos a `AppSettings` (`OutputRoot = MyDocuments\ConversorLibros`, `OllamaUrl = http://localhost:11434`, `OllamaModel = qwen3:8b`). `Save` normaliza `PiperModelDir` (si la carpeta no tiene `.onnx` pero su padre sí, usa el padre) y guarda con escritura atómica.
  - `SettingsEndpoints`: `GET /api/settings`, `PUT /api/settings`, `POST /api/settings/test-ollama` (consulta `{url}/api/tags` con timeout de 10 s y devuelve `{ connected, modelInstalled, models, message }`).
  - `FileSystemEndpoints`: `GET /api/fs/browse?path=...` devuelve `{ currentPath, parentPath, directories: [{ name, path }] }`. Si `path` es nulo/vacío devuelve unidades listas (`DriveInfo.GetDrives()`). Si `path` no existe o lanza `UnauthorizedAccessException`/`IOException`, responde `400 BadRequest` con `{ error }`.
  - `Program.cs`: configura puerto por defecto `http://localhost:5180` (respetando `ASPNETCORE_URLS`), JSON camelCase, `UseDefaultFiles()`, `UseStaticFiles()`, mapeo de endpoints `/api` y `MapFallbackToFile("index.html")` fuera de `/api`.

- [ ] **Step 5: Ejecutar tests y verificar que pasan**

  Run: `dotnet test ConversorLibros.sln`
  Expected: PASS en `ConversorLibros.Core.Tests` y `ConversorLibros.Web.Tests`.

- [ ] **Step 6: Commit**

  ```bash
  git add .gitignore ConversorLibros.sln src/ConversorLibros.Web tests/ConversorLibros.Web.Tests
  git commit -m "feat(web): scaffold ASP.NET Core host with settings and filesystem endpoints"
  ```

---

### Task 2: Endpoints de Biblioteca (`/api/library`) e Inspección de EPUB (`/api/books`)

**Files:**
- Modify: `src/ConversorLibros.Web/Models/ApiDtos.cs`
- Modify: `src/ConversorLibros.Web/Program.cs`
- Create: `src/ConversorLibros.Web/Services/UploadedBookStore.cs`
- Create: `src/ConversorLibros.Web/Endpoints/LibraryEndpoints.cs`
- Create: `src/ConversorLibros.Web/Endpoints/BookEndpoints.cs`
- Create: `tests/ConversorLibros.Web.Tests/LibraryAndBookEndpointsTests.cs`

**Interfaces:**
- Consumes:
  - `JsonSettingsStore` (Task 1)
  - `ConversorLibros.Core.Services.AudiobookLibrary.Scan(string)`
  - `ConversorLibros.Core.Services.EpubBookSource.LoadAsync(string, CancellationToken)`
- Produces:
  - `LibraryItemDto(string Id, string Title, string Author, string Format, int ChapterCount, double? DurationSeconds, bool HasCover, string? CoverUrl)`
  - `InspectedBookDto(string UploadId, string Title, string Author, string Language, bool HasCover, string? CoverUrl, IReadOnlyList<ChapterSummaryDto> Chapters)`
  - `ChapterSummaryDto(int Index, string Title, int CharCount)`
  - `UploadedBookStore.TryGet(string uploadId, out UploadedBook? entry)` -> `UploadedBook(string UploadId, string EpubPath, Book Book)`
  - Endpoints `GET /api/library`, `GET /api/library/{id}/cover`, `POST /api/library/{id}/open`, `POST /api/books/inspect`, `GET /api/books/{uploadId}/cover`

- [ ] **Step 1: Escribir tests fallidos en `tests/ConversorLibros.Web.Tests/LibraryAndBookEndpointsTests.cs`**

  ```csharp
  [Fact] async Task GetLibrary_NonExistentOutputRootReturnsEmptyList()
  [Fact] async Task GetLibrary_ScansM4bAndServesStableIds()
  [Fact] async Task GetLibraryCover_UnknownIdReturnsNotFound()
  [Fact] async Task InspectBook_InvalidNonEpubFileReturnsBadRequestAndCleansTempFile()
  [Fact] async Task InspectBook_ValidEpubReturnsMetadataChaptersAndCoverEndpoint()
  [Fact] async Task GetUploadedBookCover_UnknownUploadIdReturnsNotFound()
  ```
  (Para `InspectBook_ValidEpub...`, generar un EPUB mínimo en memoria/archivo temporal reutilizando la misma técnica de `EpubBookSourceTests` en `Core.Tests`.)

- [ ] **Step 2: Ejecutar tests para verificar que fallan**

  Run: `dotnet test tests/ConversorLibros.Web.Tests --filter LibraryAndBookEndpointsTests`
  Expected: FAIL.

- [ ] **Step 3: Implementar `UploadedBookStore.cs`, `LibraryEndpoints.cs` y `BookEndpoints.cs`**

  - `UploadedBookStore`: guarda uploads en `%TEMP%\ConversorLibros\uploads\{guid}.epub` (directorio inyectable en tests), parsea con `EpubBookSource`, elimina el temporal si falla el parseo y limita entradas antiguas (p. ej. conserva los últimos 5 uploads borrando los anteriores).
  - `LibraryEndpoints`: genera IDs determinísticos (SHA-256 Base64Url de la ruta completa) al escanear `OutputRoot`, valida pertenencia estricta al conjunto escaneado antes de servir portada o invocar `explorer.exe` con `ProcessStartInfo.ArgumentList`.
  - `BookEndpoints`: `POST /api/books/inspect` valida presencia de archivo y extensión `.epub`, captura excepciones de parseo devolviendo `400 BadRequest` con `{ error }`, y expone `GET /api/books/{uploadId}/cover`.

- [ ] **Step 4: Ejecutar tests y verificar que pasan**

  Run: `dotnet test ConversorLibros.sln`
  Expected: PASS.

- [ ] **Step 5: Commit**

  ```bash
  git add src/ConversorLibros.Web tests/ConversorLibros.Web.Tests
  git commit -m "feat(web): add library and EPUB inspection endpoints"
  ```

---

### Task 3: `ConversionJobManager` y endpoints de conversión + streaming SSE

**Files:**
- Modify: `src/ConversorLibros.Web/Models/ApiDtos.cs`
- Modify: `src/ConversorLibros.Web/Program.cs`
- Create: `src/ConversorLibros.Web/Services/ConversionRunner.cs`
- Create: `src/ConversorLibros.Web/Services/ConversionJobManager.cs`
- Create: `src/ConversorLibros.Web/Endpoints/ConversionEndpoints.cs`
- Create: `tests/ConversorLibros.Web.Tests/ConversionEndpointsTests.cs`

**Interfaces:**
- Consumes:
  - `JsonSettingsStore` (Task 1)
  - `UploadedBookStore` (Task 2)
  - `BookConversionPipeline`, `AudiobookChapterResolver`, `OpusFolderExporter`, `M4bExporter`, `PiperSherpaTtsEngine`, `OllamaTranslator`, `CachedChapterTranslator`
- Produces:
  - `IConversionRunner.RunAsync(ConversionJobRequest request, WebSettings settings, IProgress<ConversionProgress> progress, CancellationToken ct) -> Task<ConversionRunResult>`
  - `ConversionStateDto(string Status, string Stage, double ProgressFraction, string CurrentChapter, string Timing, string? BookTitle, string? OutputPath, string? Warning, string? Error, IReadOnlyList<string> Logs)`
  - `StartConversionRequest(string UploadId, IReadOnlyList<int> ChapterIndexes, float Speed, bool Translate, string? SourceLanguage, string Format)`
  - Endpoints `POST /api/conversions`, `GET /api/conversions/current`, `GET /api/conversions/events`, `POST /api/conversions/cancel`

- [ ] **Step 1: Escribir tests fallidos en `tests/ConversorLibros.Web.Tests/ConversionEndpointsTests.cs`**

  ```csharp
  [Fact] async Task StartConversion_UnknownUploadIdReturnsBadRequest()
  [Fact] async Task StartConversion_EmptyChaptersReturnsBadRequest()
  [Fact] async Task StartConversion_WhileRunningReturnsConflict()
  [Fact] async Task StartConversion_ReportsProgressAndCompletesAndPersistsLogsInCurrent()
  [Fact] async Task CancelConversion_StopsActiveJobAndTransitionsToCancelled()
  [Fact] async Task ConversionEvents_SseStreamsSnapshotAndProgressEvents()
  ```
  (Sustituir `IConversionRunner` en `WebApplicationFactory` por un fake controlable con `TaskCompletionSource`.)

- [ ] **Step 2: Ejecutar tests para verificar que fallan**

  Run: `dotnet test tests/ConversorLibros.Web.Tests --filter ConversionEndpointsTests`
  Expected: FAIL.

- [ ] **Step 3: Implementar `ConversionRunner.cs`, `ConversionJobManager.cs` y `ConversionEndpoints.cs`**

  - `CoreConversionRunner`: implementa la misma lógica que `ConversorLibros.App/Services/ConversionService.cs` dentro de `Task.Run`, usando `Voice = "0"`, `TargetLanguage = "es"`, timeout de `HttpClient` de 10 minutos, detección de `ffmpeg` y devolución de advertencia si falta `ffmpeg`.
  - `ConversionJobManager`: singleton thread-safe (`SemaphoreSlim`/`lock`), guarda estado actual + lista de logs con timestamp (`HH:mm:ss  mensaje`), calcula porcentaje global + tiempo transcurrido + ETA exactamente como `NewConversionViewModel`, traduce etapas (`Translating` → `"Traduciendo"`, `Synthesizing` → `"Sintetizando"`, `Assembled` → `"Ensamblando"`, `Exporting` → `"Exportando"`), y difunde eventos `snapshot`, `progress`, `log`, `completed`, `cancelled`, `error` a suscriptores SSE.
  - `ConversionEndpoints`: valida `UploadId` existente y `ChapterIndexes.Count > 0` (`400`), devuelve `409 Conflict` si ya está corriendo, `202 Accepted` al iniciar, y sirve `text/event-stream` en `GET /api/conversions/events`.

- [ ] **Step 4: Ejecutar tests y verificar que pasan**

  Run: `dotnet test ConversorLibros.sln`
  Expected: PASS.

- [ ] **Step 5: Commit**

  ```bash
  git add src/ConversorLibros.Web tests/ConversorLibros.Web.Tests
  git commit -m "feat(web): add conversion job manager and SSE progress streaming"
  ```

---

### Task 4: Scaffolding React/Vite/Tailwind, diseño "Dark Studio", `ConversionContext` y `AppShell`

**Files:**
- Create: `src/ConversorLibros.Web/ClientApp/package.json`
- Create: `src/ConversorLibros.Web/ClientApp/vite.config.ts`
- Create: `src/ConversorLibros.Web/ClientApp/tsconfig.json`
- Create: `src/ConversorLibros.Web/ClientApp/index.html`
- Create: `src/ConversorLibros.Web/ClientApp/src/index.css`
- Create: `src/ConversorLibros.Web/ClientApp/src/main.tsx`
- Create: `src/ConversorLibros.Web/ClientApp/src/shared/api/types.ts`
- Create: `src/ConversorLibros.Web/ClientApp/src/shared/api/client.ts`
- Create: `src/ConversorLibros.Web/ClientApp/src/shared/utils/format.ts`
- Create: `src/ConversorLibros.Web/ClientApp/src/shared/components/Button.tsx`
- Create: `src/ConversorLibros.Web/ClientApp/src/shared/components/Card.tsx`
- Create: `src/ConversorLibros.Web/ClientApp/src/shared/components/Badge.tsx`
- Create: `src/ConversorLibros.Web/ClientApp/src/shared/components/Modal.tsx`
- Create: `src/ConversorLibros.Web/ClientApp/src/shared/components/Toast.tsx`
- Create: `src/ConversorLibros.Web/ClientApp/src/app/ConversionContext.tsx`
- Create: `src/ConversorLibros.Web/ClientApp/src/app/AppShell.tsx`
- Create: `src/ConversorLibros.Web/ClientApp/src/app/App.tsx`
- Create: `src/ConversorLibros.Web/ClientApp/src/app/__tests__/AppShell.test.tsx`

**Interfaces:**
- Consumes: Contratos HTTP/SSE definidos en Tasks 1–3.
- Produces:
  - Cliente tipado `api` en `shared/api/client.ts`: `getLibrary()`, `openLibraryItem(id)`, `inspectBook(file)`, `startConversion(req)`, `getCurrentConversion()`, `cancelConversion()`, `getSettings()`, `saveSettings(settings)`, `testOllama(url, model)`, `browseFileSystem(path?)`.
  - Hook `useConversion()` desde `app/ConversionContext.tsx` devolviendo `{ state, isRunning, refreshState, startConversion, cancelConversion }`.
  - Primitivas UI: `Button`, `Card`, `Badge`, `Modal`, `ToastProvider`/`useToast`.
  - `AppShell` con rutas `/biblioteca`, `/convertir`, `/ajustes` (redirección `/` → `/biblioteca`).

- [ ] **Step 1: Inicializar `ClientApp` con Vite + React 19 + TypeScript + Tailwind CSS v4 + `lucide-react` + `react-router-dom` + Vitest/Testing Library**

  - Configurar `vite.config.ts` con proxy `/api` → `http://localhost:5180`, `build.outDir: "../wwwroot"`, `build.emptyOutDir: true`, y configuración `test` (`environment: "jsdom"`, `globals: true`).
  - Definir tokens de color "Dark Studio" (`#0B0D12`, `#131720`, `#1B2130`, `#252C3D`, `#F59E0B`, `#FBBF24`) y fuentes sans + mono en `src/index.css`.

- [ ] **Step 2: Escribir tests fallidos en `src/app/__tests__/AppShell.test.tsx`**

  ```tsx
  test("renders sidebar links for Biblioteca, Nueva conversión and Ajustes")
  test("hydrates active conversion from GET /api/conversions/current and displays sidebar progress widget")
  test("updates progress widget when SSE progress event arrives")
  ```

- [ ] **Step 3: Ejecutar Vitest para verificar que falla**

  Run: `npm test -- --run` (en `src/ConversorLibros.Web/ClientApp`)
  Expected: FAIL.

- [ ] **Step 4: Implementar capa `shared/`, `ConversionContext.tsx`, `AppShell.tsx` y `App.tsx`**

  - `ConversionContext`: al montar llama `GET /api/conversions/current` y abre `EventSource("/api/conversions/events")` escuchando `snapshot`, `progress`, `log`, `completed`, `cancelled`, `error`. Reconecta automáticamente si se pierde el stream.
  - `AppShell`: barra lateral fija (`w-64`), cabecera de marca **ConversorLibros**, navegación con estados activos claros, mini-widget inferior visible cuando `isRunning` (mostrando libro, etapa, porcentaje y barra animada con enlace a `/convertir`) y área principal con scroll limpio.
  - `App.tsx`: `BrowserRouter`, `ToastProvider`, `ConversionProvider` y rutas con placeholders temporales para las 3 páginas hasta Tasks 5–7.

- [ ] **Step 5: Ejecutar tests y build de producción**

  Run: `npm test -- --run && npm run build` (en `src/ConversorLibros.Web/ClientApp`)
  Expected: PASS y generación de `src/ConversorLibros.Web/wwwroot/index.html`.

- [ ] **Step 6: Commit**

  ```bash
  git add src/ConversorLibros.Web/ClientApp
  git commit -m "feat(ui): scaffold React Dark Studio shell, API client and SSE context"
  ```

---

### Task 5: Vista Biblioteca (`LibraryPage`)

**Files:**
- Create: `src/ConversorLibros.Web/ClientApp/src/features/library/BookCard.tsx`
- Create: `src/ConversorLibros.Web/ClientApp/src/features/library/LibraryPage.tsx`
- Create: `src/ConversorLibros.Web/ClientApp/src/features/library/__tests__/LibraryPage.test.tsx`
- Modify: `src/ConversorLibros.Web/ClientApp/src/app/App.tsx`

**Interfaces:**
- Consumes: `api.getLibrary()`, `api.openLibraryItem(id)`, `Button`, `Card`, `Badge`, `useToast`, `formatDuration`.
- Produces: `LibraryPage` montada en `/biblioteca`.

- [ ] **Step 1: Escribir tests fallidos en `LibraryPage.test.tsx`**

  ```tsx
  test("renders actionable empty state linking to /convertir when library is empty")
  test("renders audiobook cards and filters by search text and format (Opus/M4B)")
  test("calls POST /api/library/{id}/open when clicking Abrir carpeta")
  ```

- [ ] **Step 2: Ejecutar Vitest para verificar que falla**

  Run: `npm test -- --run src/features/library/__tests__/LibraryPage.test.tsx`
  Expected: FAIL.

- [ ] **Step 3: Implementar `BookCard.tsx` y `LibraryPage.tsx` y conectar en `App.tsx`**

  - `BookCard`: portada 2:3 (`coverUrl`) o portada tipográfica elegante con degradado editorial e iniciales cuando no hay imagen; badge `OPUS` o `M4B`; título, autor, conteo de capítulos y duración formateada; botón **Abrir carpeta**.
  - `LibraryPage`: encabezado con contador, buscador instantáneo (título/autor), selector de segmento (`Todos`, `Opus`, `M4B`), botón **Actualizar**, grilla responsiva (`grid-cols-1 sm:grid-cols-2 xl:grid-cols-3 2xl:grid-cols-4`), skeletons de carga y estado vacío con botón hacia `/convertir`.

- [ ] **Step 4: Ejecutar tests y verificar que pasan**

  Run: `npm test -- --run src/features/library/__tests__/LibraryPage.test.tsx`
  Expected: PASS.

- [ ] **Step 5: Commit**

  ```bash
  git add src/ConversorLibros.Web/ClientApp/src
  git commit -m "feat(ui): implement Biblioteca page with search, format filter and cover grid"
  ```

---

### Task 6: Vista Nueva conversión (`NewConversionPage`)

**Files:**
- Create: `src/ConversorLibros.Web/ClientApp/src/features/conversion/EpubDropzone.tsx`
- Create: `src/ConversorLibros.Web/ClientApp/src/features/conversion/ChapterList.tsx`
- Create: `src/ConversorLibros.Web/ClientApp/src/features/conversion/ConversionOptions.tsx`
- Create: `src/ConversorLibros.Web/ClientApp/src/features/conversion/ProgressPanel.tsx`
- Create: `src/ConversorLibros.Web/ClientApp/src/features/conversion/NewConversionPage.tsx`
- Create: `src/ConversorLibros.Web/ClientApp/src/features/conversion/__tests__/NewConversionPage.test.tsx`
- Modify: `src/ConversorLibros.Web/ClientApp/src/app/App.tsx`

**Interfaces:**
- Consumes: `api.inspectBook(file)`, `api.getSettings()`, `useConversion()`, `Button`, `Card`, `Badge`, `useToast`.
- Produces: `NewConversionPage` montada en `/convertir`.

- [ ] **Step 1: Escribir tests fallidos en `NewConversionPage.test.tsx`**

  ```tsx
  test("shows warning banner linking to /ajustes when Piper model is invalid")
  test("uploading an EPUB displays book metadata, cover and selects all chapters by default")
  test("Ninguno/Todos buttons update selected chapter count and disable Empezar when none selected")
  test("submits selected chapters, speed, translation and format to startConversion")
  ```

- [ ] **Step 2: Ejecutar Vitest para verificar que falla**

  Run: `npm test -- --run src/features/conversion/__tests__/NewConversionPage.test.tsx`
  Expected: FAIL.

- [ ] **Step 3: Implementar los componentes de `features/conversion/` y conectar en `App.tsx`**

  - Layout de dos columnas en escritorio (`grid-cols-1 lg:grid-cols-12 gap-6`):
    - **Columna principal (`lg:col-span-7`):** `EpubDropzone` (drag & drop + selector de archivo `.epub`; al cargar muestra tarjeta compacta con portada, título, autor, idioma detectado y botón **Cambiar libro**) + `ChapterList` (buscador de capítulos, botones **Todos** / **Ninguno**, contador `X de Y seleccionados`, lista desplazable con checkboxes y longitud aproximada en caracteres/palabras).
    - **Columna lateral (`lg:col-span-5`):** `ConversionOptions` (presets rápidos de velocidad `0.85x`, `1.0x`, `1.15x`, `1.25x` + slider `0.5x–1.5x`; toggle de traducción con input de idioma origen → `es`; tarjetas seleccionables de formato **Carpeta Opus** vs **M4B**) + `ProgressPanel` (etapa actual, barra animada con porcentaje monoespaciado, capítulo actual, tiempo transcurrido/ETA, advertencia si falta `ffmpeg`, botones **Empezar** / **Cancelar** y consola de registro colapsable con scroll automático).
  - Banner de aviso con enlace directo a `/ajustes` si `settings.piper.isValid === false`.

- [ ] **Step 4: Ejecutar tests y verificar que pasan**

  Run: `npm test -- --run src/features/conversion/__tests__/NewConversionPage.test.tsx`
  Expected: PASS.

- [ ] **Step 5: Commit**

  ```bash
  git add src/ConversorLibros.Web/ClientApp/src
  git commit -m "feat(ui): implement Nueva conversion workspace with drag-and-drop and live progress"
  ```

---

### Task 7: Vista Ajustes (`SettingsPage`), explorador de carpetas y verificación integral

**Files:**
- Create: `src/ConversorLibros.Web/ClientApp/src/features/settings/HealthBadge.tsx`
- Create: `src/ConversorLibros.Web/ClientApp/src/features/settings/FolderBrowserModal.tsx`
- Create: `src/ConversorLibros.Web/ClientApp/src/features/settings/SettingsPage.tsx`
- Create: `src/ConversorLibros.Web/ClientApp/src/features/settings/__tests__/SettingsPage.test.tsx`
- Modify: `src/ConversorLibros.Web/ClientApp/src/app/App.tsx`
- Modify: `CLAUDE.md`

**Interfaces:**
- Consumes: `api.getSettings()`, `api.saveSettings(settings)`, `api.testOllama(url, model)`, `api.browseFileSystem(path?)`, `Modal`, `Button`, `Card`, `Badge`, `useToast`.
- Produces: `SettingsPage` montada en `/ajustes`, documentación actualizada en `CLAUDE.md`.

- [ ] **Step 1: Escribir tests fallidos en `SettingsPage.test.tsx`**

  ```tsx
  test("renders health badges for Piper and ffmpeg from GET /api/settings")
  test("opens FolderBrowserModal, navigates into a folder and applies selected path")
  test("Probar conexión populates detected Ollama models selector")
  test("Guardar persists settings and displays validation feedback")
  ```

- [ ] **Step 2: Ejecutar Vitest para verificar que falla**

  Run: `npm test -- --run src/features/settings/__tests__/SettingsPage.test.tsx`
  Expected: FAIL.

- [ ] **Step 3: Implementar `HealthBadge.tsx`, `FolderBrowserModal.tsx`, `SettingsPage.tsx` y conectar en `App.tsx`**

  - Tres tarjetas por dominio: **Motor de voz (Piper)**, **Carpeta de salida y ffmpeg** y **Traducción local (Ollama)**, cada una con `HealthBadge` (`Listo` / `Revisar` / `No encontrado`).
  - `FolderBrowserModal`: llama a `GET /api/fs/browse`, permite subir al directorio padre o volver a las unidades, navegar subcarpetas y confirmar la ruta actual.
  - En Ollama, **Probar conexión** muestra el estado y, si hay modelos devueltos por `/api/tags`, muestra un selector para elegir uno directamente.
  - Actualizar `CLAUDE.md` con los comandos de build/run de `ConversorLibros.Web` y `ClientApp`.

- [ ] **Step 4: Ejecutar suite completa de tests y builds**

  Run:
  ```bash
  cd src/ConversorLibros.Web/ClientApp && npm test -- --run && npm run build
  cd ../../../.. && dotnet test ConversorLibros.sln
  ```
  Expected: todos los tests de Vitest, `ConversorLibros.Core.Tests` y `ConversorLibros.Web.Tests` en verde, y build limpio en `wwwroot/`.

- [ ] **Step 5: Verificación de humo con el servidor real**

  Iniciar `dotnet run --project src/ConversorLibros.Web` en un comando junto con `curl` contra `http://localhost:5180/` (verifica HTML de React) y `http://localhost:5180/api/settings` (verifica JSON 200), luego detener el servidor.

- [ ] **Step 6: Commit**

  ```bash
  git add src/ConversorLibros.Web/ClientApp/src CLAUDE.md
  git commit -m "feat(ui): implement Ajustes page with folder browser and document web host"
  ```
