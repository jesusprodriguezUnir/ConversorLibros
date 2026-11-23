# Biblioteca estilo Calibre, Lector Unificado y Conversor con Muestras — Plan de Implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Evolucionar `ConversorLibros.Web` y `ConversorLibros.Core` hacia una biblioteca personal estilo Calibre respaldada por SQLite, con importación y lectura inmediata de EPUBs, muestras de voz antes de convertir, cola de conversión persistida y lector unificado (solo texto + *read-along* sincronizado en Opus y M4B).

**Architecture:** `ConversorLibros.Core` mantiene intacto el pipeline offline (normalización, chunking, traducción opcional con Ollama, Piper TTS, ensamblado WAV y empaquetado Opus/M4B) corrigiendo bugs base y añadiendo formato ligero y pausas reales. `ConversorLibros.Web` incorpora `Microsoft.Data.Sqlite` bajo `<OutputRoot>/library.db` mediante un contexto dinámico (`ILibraryContext`), importa EPUBs generando sidecars de texto al instante, expone descubrimiento de modelos Piper + muestras de voz (`POST /api/tts/sample`) y sustituye la conversión única por un `BackgroundService` con cola persistida. El frontend en React (`ClientApp`) evoluciona `/biblioteca` a un layout de tres paneles estilo Calibre, añade un modal de conversión con muestras y unifica `ReaderPage` para libros con y sin audio.

**Tech Stack:** .NET 10 (`net10.0`), ASP.NET Core Minimal APIs, `Microsoft.Data.Sqlite`, `VersOne.Epub` 3.3.4, `AngleSharp` 1.2.0, `org.k2fsa.sherpa.onnx` 1.13.8 (pinned), xUnit, React 19, TypeScript, Vite 6, Tailwind CSS 4, Vitest + Testing Library.

**Spec:** `docs/superpowers/specs/2026-11-23-calibre-library-and-reader-design.md`

---

## Global Constraints

- Plataforma objetivo: Windows x64, .NET 10 (`net10.0`), uso personal local 100 % offline.
- `org.k2fsa.sherpa.onnx` (Core) y `org.k2fsa.sherpa.onnx.runtime.win-x64` (CLI y Web) deben permanecer fijados en **1.13.8** (no actualizar a 2.x).
- Interfaz de usuario y documentación en español; código, identificadores y comentarios en inglés.
- `TextChunk.ContentHash` debe ser determinista con `CultureInfo.InvariantCulture`.
- `OllamaTranslator.PromptVersion` controla la invalidación de caché de traducción; no alterar sin subir versión.
- El ensamblado de capítulos usa streaming con `FileStream` sin cargar capítulos completos en memoria.
- Toda invocación a procesos externos (`ffmpeg`, `explorer.exe`) usa `ProcessStartInfo.ArgumentList`, nunca cadenas concatenadas.
- La síntesis de Piper es síncrona y debe ejecutarse siempre fuera del hilo HTTP (`Task.Run` / `BackgroundService`).
- Actualizar las 3 raíces de composición (`Cli/Program.cs`, `App/Services/ConversionService.cs`, `Web/Services/ConversionRunner.cs`) si cambia la firma de un constructor existente en `Core` (o añadir sobrecargas compatibles).

## Review Focus

1. **Cambio de `OutputRoot` con cola activa:** Si el usuario intenta cambiar `OutputRoot` en Ajustes mientras hay trabajos `queued` o `running`, `PUT /api/settings` debe responder `409 Conflict` sin alterar `settings.json` ni romper la conexión SQLite activa. *(Verificado en Task 3)*
2. **Importación por lotes con duplicados y archivos corruptos:** Arrastrar a la vez un EPUB válido, un EPUB duplicado (mismo `source_sha256`) y un archivo corrupto debe devolver resultados por archivo (`imported`, `skipped`, `error`) sin abortar el lote ni dejar carpetas huérfanas. *(Verificado en Task 4 y Task 5)*
3. **Conversión parcial y reconversión con cambio de formato o traducción:** Convertir solo un subconjunto de capítulos deja los demás legibles en solo texto; reconvertir reemplaza los archivos previos de `audio/` conservando `source.epub` y `work/cache/`. *(Verificado en Task 8 y Task 10)*
4. **Borrado seguro según tipo de libro y estado de cola:** `DELETE /api/library/{id}` rechaza con `409` si el libro tiene un trabajo activo/encolado; si `is_legacy = 1` elimina solo la fila SQLite sin tocar disco; si `is_legacy = 0` elimina fila y carpeta validando contención bajo `<LibraryRoot>/books/`. *(Verificado en Task 5)*
5. **Muestras de voz con capítulos vacíos o modelo inválido:** `POST /api/tts/sample` hace fallback a texto de demostración si el capítulo elegido no tiene texto sintetizable, recorta a 280 caracteres y responde `400 Bad Request` si el modelo no existe o es inválido. *(Verificado en Task 7)*

---

## File Structure

### Fase 0 — Saneamiento técnico
- Modify: `src/ConversorLibros.Core/Services/SentenceChunker.cs` (hash con `InvariantCulture`)
- Modify: `src/ConversorLibros.Core/Abstractions/PipelineInterfaces.cs` (sobrecarga/parámetro opcional de pausas en `IChapterAssembler`)
- Modify: `src/ConversorLibros.Core/Services/WavChapterAssembler.cs` (escritura de silencio PCM por chunk)
- Modify: `src/ConversorLibros.Core/Services/BookConversionPipeline.cs` (propagación de `PauseAfter` y sobrecarga con `Book` precargado)
- Modify: `src/ConversorLibros.Core/Services/SyncMapBuilder.cs` (contabilizar `PauseAfter` en timings)
- Modify: `src/ConversorLibros.Web/Services/ConversionRunner.cs` (`IHttpClientFactory`, `Book` precargado, limpieza de WAVs)
- Modify: `src/ConversorLibros.Web/Services/JsonSettingsStore.cs` & `src/ConversorLibros.Web/Models/SettingsModels.cs` (`DeleteWavAfterExport`, default `true`)
- Modify: `.gitignore`, `CLAUDE.md`, `README.md` (ignorar `.vite/` y marcar MAUI como archivado)

### Fase 1 — Catálogo SQLite, importación e interfaz estilo Calibre
- Modify: `src/ConversorLibros.Web/ConversorLibros.Web.csproj` (añadir `Microsoft.Data.Sqlite`)
- Create: `src/ConversorLibros.Web/Services/Library/LibrarySchema.cs` (DDL y migraciones con `PRAGMA user_version`)
- Create: `src/ConversorLibros.Web/Services/Library/ILibraryContext.cs` & `LibraryContext.cs` (ruta dinámica `<OutputRoot>/library.db` + init)
- Create: `src/ConversorLibros.Web/Services/Library/LibraryModels.cs` (`BookRecord`, `LibraryQuery`, `LibraryFacetsDto`, `LibraryBookDetailDto`, `UpdateBookMetadataRequest`, `ImportBookResultDto`, `ConversionJobRecord`)
- Create: `src/ConversorLibros.Web/Services/Library/SqliteLibraryRepository.cs` (CRUD libros, etiquetas, facetas, búsqueda, trabajos)
- Create: `src/ConversorLibros.Web/Services/Library/LegacyLibraryImporter.cs` (migración no destructiva de audiolibros existentes)
- Create: `src/ConversorLibros.Web/Services/Library/TextOnlyReaderSidecarWriter.cs` (generación inmediata de sidecars `reader/` sin audio)
- Create: `src/ConversorLibros.Web/Services/Library/BookImportService.cs` (importación batch EPUB + `source_sha256` + portada + sidecars)
- Modify: `src/ConversorLibros.Web/Endpoints/SettingsEndpoints.cs` (guard `409` al cambiar `OutputRoot` con cola activa + disparo de `LegacyLibraryImporter`)
- Modify: `src/ConversorLibros.Web/Endpoints/LibraryEndpoints.cs` (API REST completa de catálogo)
- Modify: `src/ConversorLibros.Web/Program.cs` (registro DI e inicialización en arranque)
- Create: `src/ConversorLibros.Web/ClientApp/src/features/library/LibraryFacetsSidebar.tsx`
- Create: `src/ConversorLibros.Web/ClientApp/src/features/library/LibraryTableView.tsx`
- Create: `src/ConversorLibros.Web/ClientApp/src/features/library/BookInspectorPanel.tsx`
- Create: `src/ConversorLibros.Web/ClientApp/src/features/library/EditMetadataModal.tsx`
- Modify: `src/ConversorLibros.Web/ClientApp/src/features/library/LibraryPage.tsx` (layout 3 paneles + drag & drop)
- Modify: `src/ConversorLibros.Web/ClientApp/src/shared/api/types.ts` & `client.ts`

### Fase 2 — Voces, muestras de audio y cola de conversión
- Modify: `src/ConversorLibros.Web/Services/PiperModelLocator.cs` (`ListAvailableModels()`)
- Create: `src/ConversorLibros.Web/Services/TtsSampleService.cs` (síntesis rápida de muestras con caché y límite de 280 chars)
- Create: `src/ConversorLibros.Web/Endpoints/TtsEndpoints.cs` (`GET /api/tts/voices`, `POST /api/tts/sample`)
- Create: `src/ConversorLibros.Web/Services/ConversionQueueService.cs` & `ConversionQueueWorker.cs` (`BackgroundService` + cola SQLite + SSE + reconversión)
- Modify: `src/ConversorLibros.Web/Endpoints/ConversionEndpoints.cs` (`/api/conversions/jobs` + SSE)
- Create: `src/ConversorLibros.Web/ClientApp/src/features/convert/ConvertBookModal.tsx` (selector de voz + reproductor de muestra + capítulos + diccionario)
- Create: `src/ConversorLibros.Web/ClientApp/src/features/convert/ConversionQueueDrawer.tsx`

### Fase 3 — Lector unificado (solo texto + read-along) y mejoras de lectura
- Modify: `src/ConversorLibros.Core/Models/ReaderModels.cs` (`ReaderParagraph.Kind`, `ReaderTextSpan`, `ReaderIndexChapter.HasAudio`, `AudioOffsetMs`)
- Modify: `src/ConversorLibros.Core/Services/EpubBookSource.cs` (extracción opcional de formato ligero sin alterar texto plano)
- Modify: `src/ConversorLibros.Web/Services/ReaderDataPublisher.cs` (soporte M4B + preservación de capítulos solo texto)
- Modify: `src/ConversorLibros.Web/Endpoints/ReaderEndpoints.cs` (audio Opus/M4B + sincronización de progreso con SQLite)
- Modify: `src/ConversorLibros.Web/ClientApp/src/features/reader/ReaderPage.tsx`, `useAudioSync.ts`, `PaginatedChapter.tsx`, `ReaderToolbar.tsx`

---

## Fase 0 — Saneamiento técnico

### Task 1: Hash invariante a la cultura y pausas reales en ensamblado WAV

**Files:**
- Modify: `src/ConversorLibros.Core/Services/SentenceChunker.cs:114-127`
- Modify: `src/ConversorLibros.Core/Abstractions/PipelineInterfaces.cs:69-76`
- Modify: `src/ConversorLibros.Core/Services/WavChapterAssembler.cs:14-70`
- Modify: `src/ConversorLibros.Core/Services/BookConversionPipeline.cs:116-125`
- Modify: `src/ConversorLibros.Core/Services/SyncMapBuilder.cs`
- Test: `tests/ConversorLibros.Core.Tests/SentenceChunkerTests.cs`
- Test: `tests/ConversorLibros.Core.Tests/WavChapterAssemblerTests.cs`
- Test: `tests/ConversorLibros.Core.Tests/SyncMapBuilderTests.cs`

**Interfaces:**
- Consumes: `TextChunk.PauseAfter` (`Book.cs:45-50`), `WavChapterAssembler.AssembleChapterAsync`
- Produces:
  - `IChapterAssembler.AssembleChapterAsync(int chapterIndex, IReadOnlyList<string> chunkAudioPaths, string outputDirectory, IReadOnlyList<TimeSpan>? pausesAfter = null, CancellationToken cancellationToken = default)` (sobrecarga por defecto para no romper implementaciones existentes)
  - `SyncMapBuilder.Build(Chapter chapter, IReadOnlyList<TextChunk> chunks, IReadOnlyList<string> chunkAudioPaths)` contabilizando `chunk.PauseAfter` en los offsets `StartMs` de chunks posteriores y en `DurationMs`.

- [ ] **Step 1: Write failing tests for culture-invariant hash, WAV silence insertion, and sync map pause offsets**

En `tests/ConversorLibros.Core.Tests/SentenceChunkerTests.cs`:
```csharp
[Fact]
public void Chunk_ProducesSameHashAcrossDifferentCultures()
{
    var original = CultureInfo.CurrentCulture;
    try
    {
        var chunker = new SentenceChunker();
        var chapter = new Chapter(1, "Cap 1", "Hola mundo.");
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("es-ES");
        var esHash = chunker.Chunk(chapter, "Hola mundo.", "0", 1.25f, "v1")[0].ContentHash;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var invHash = chunker.Chunk(chapter, "Hola mundo.", "0", 1.25f, "v1")[0].ContentHash;
        Assert.Equal(invHash, esHash);
    }
    finally
    {
        CultureInfo.CurrentCulture = original;
    }
}
```

En `tests/ConversorLibros.Core.Tests/WavChapterAssemblerTests.cs`, añadir `AssembleChapterAsync_InsertsSilenceBytesMatchingPausesAfter`: ensamblar 2 WAVs de 22050 Hz / 16-bit mono (44100 bytes/s) con `pausesAfter = [TimeSpan.FromMilliseconds(500), TimeSpan.Zero]` y verificar que el WAV resultante tiene exactamente `data1 + data2 + (44100 / 2)` bytes de datos PCM (alineado a `blockAlign`) y que los bytes intermedios son cero.

En `tests/ConversorLibros.Core.Tests/SyncMapBuilderTests.cs`, añadir `Build_AdvancesNextChunkStartMsByPauseAfter`: verificar que si chunk 0 dura `1000ms` y tiene `PauseAfter = 300ms`, chunk 1 arranca en `StartMs == 1300` y `DurationMs` incluye las pausas.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ConversorLibros.Core.Tests --filter "FullyQualifiedName~SentenceChunkerTests|FullyQualifiedName~WavChapterAssemblerTests|FullyQualifiedName~SyncMapBuilderTests"`
Expected: FAIL en los 3 tests nuevos.

- [ ] **Step 3: Implement invariant hash, PCM silence insertion, and sync-map pause accounting**

1. En `SentenceChunker.cs:123`: usar `string.Create(CultureInfo.InvariantCulture, $"{text}|{voiceId}|{speed:F2}|{modelVersion}")`.
2. En `PipelineInterfaces.cs`: añadir sobrecarga en `IChapterAssembler` con `IReadOnlyList<TimeSpan>? pausesAfter` (o método default que delega).
3. En `WavChapterAssembler.cs`: leer `byteRate` (offset 28, `int`) y `blockAlign` (offset 32, `short`) de la cabecera WAV de 44 bytes; tras copiar cada chunk `i` con `pausesAfter[i] > TimeSpan.Zero`, escribir `silenceBytes = ((long)(byteRate * pause.TotalSeconds) / blockAlign) * blockAlign` bytes en cero usando un buffer reutilizable de 8192 bytes, sumándolos a `totalDataBytes`.
4. En `BookConversionPipeline.cs:116`: pasar `chunks.Select(c => c.PauseAfter).ToList()` al ensamblador.
5. En `SyncMapBuilder.cs`: avanzar el cursor `elapsedMs` sumando `(long)Math.Round(chunks[i].PauseAfter.TotalMilliseconds)` tras cada chunk.

- [ ] **Step 4: Run Core tests to verify all pass**

Run: `dotnet test tests/ConversorLibros.Core.Tests`
Expected: PASS (0 failures).

- [ ] **Step 5: Commit**

```bash
git add src/ConversorLibros.Core tests/ConversorLibros.Core.Tests
git commit -m "fix(core): use invariant chunk hash and write real PauseAfter silence in WAV assembly"
```

---

### Task 2: Saneamiento de `ConversorLibros.Web`, limpieza de WAVs e higiene del repositorio

**Files:**
- Modify: `src/ConversorLibros.Core/Services/BookConversionPipeline.cs` (sobrecarga `ConvertAsync(Book book, ...)`)
- Modify: `src/ConversorLibros.Web/Models/SettingsModels.cs` & `src/ConversorLibros.Web/Services/JsonSettingsStore.cs` (`DeleteWavAfterExport = true`)
- Modify: `src/ConversorLibros.Web/Services/ConversionRunner.cs` (`IHttpClientFactory`, pasar `request.Book` sin re-parsear, borrar `work/chapter_*.wav` tras export exitoso si `DeleteWavAfterExport`)
- Modify: `src/ConversorLibros.Web/Program.cs` (`builder.Services.AddHttpClient()`)
- Modify: `.gitignore`, `CLAUDE.md`, `README.md`
- Test: `tests/ConversorLibros.Web.Tests/JsonSettingsStoreTests.cs`
- Test: `tests/ConversorLibros.Web.Tests/CoreConversionRunnerTests.cs` (nuevo)

**Interfaces:**
- Consumes: `BookConversionPipeline`, `JsonSettingsStore`, `CoreConversionRunner`
- Produces:
  - `BookConversionPipeline.ConvertAsync(Book book, string outputDirectory, ...)`
  - `WebSettings.DeleteWavAfterExport` (`bool`, default `true`)
  - `CoreConversionRunner(PiperModelLocator locator, IHttpClientFactory httpClientFactory)`

- [ ] **Step 1: Write failing tests for `DeleteWavAfterExport` default and WAV cleanup**

En `tests/ConversorLibros.Web.Tests/JsonSettingsStoreTests.cs`, añadir `Get_DefaultsDeleteWavAfterExportToTrue`: verificar `store.Get().DeleteWavAfterExport == true` y que persiste `false` al guardar.

En `tests/ConversorLibros.Web.Tests/CoreConversionRunnerTests.cs`, añadir `CleanupChapterWavs_DeletesWavFilesWhenEnabledAndKeepsWhenDisabled`: verificar el helper `CoreConversionRunner.CleanupChapterWavs(IReadOnlyList<string> wavPaths, bool deleteWav)`.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ConversorLibros.Web.Tests --filter "FullyQualifiedName~JsonSettingsStoreTests|FullyQualifiedName~CoreConversionRunnerTests"`
Expected: FAIL.

- [ ] **Step 3: Implement pipeline overload, `IHttpClientFactory`, WAV cleanup, and repo hygiene**

1. Extraer la lógica central de `BookConversionPipeline.ConvertAsync` a una sobrecarga pública `ConvertAsync(Book book, string outputDirectory, ...)` y hacer que la sobrecarga con `string epubPath` cargue el libro y delegue.
2. Añadir `bool DeleteWavAfterExport = true` a `WebSettings` (respetando compatibilidad con `settings.json` existentes sin la propiedad).
3. Inyectar `IHttpClientFactory` en `CoreConversionRunner` (`builder.Services.AddHttpClient()` en `Program.cs`), pasar `request.Book` al pipeline y llamar a `CleanupChapterWavs(wavPaths, settings.DeleteWavAfterExport)` tras `ExportThenPublishAsync` exitoso sin warning de fallo de export.
4. Ejecutar `git rm -r --cached src/ConversorLibros.Web/ClientApp/.vite` (si está trackeado), añadir `src/ConversorLibros.Web/ClientApp/.vite/` a `.gitignore`, y documentar en `CLAUDE.md` y `README.md` que `ConversorLibros.App` (MAUI) está archivada/sin mantenimiento activo.

- [ ] **Step 4: Run full .NET test suite**

Run: `dotnet test ConversorLibros.sln`
Expected: PASS (Core.Tests y Web.Tests en verde).

- [ ] **Step 5: Commit**

```bash
git add .gitignore CLAUDE.md README.md src/ConversorLibros.Core src/ConversorLibros.Web tests/ConversorLibros.Web.Tests
git commit -m "chore(web): clean up WAV intermediates, use IHttpClientFactory, and archive MAUI"
```

---

## Fase 1 — Catálogo SQLite, importación de EPUBs e interfaz estilo Calibre

### Task 3: Esquema SQLite, `ILibraryContext`, `SqliteLibraryRepository` y protección de cambio de `OutputRoot`

**Files:**
- Modify: `src/ConversorLibros.Web/ConversorLibros.Web.csproj` (añadir `PackageReference Include="Microsoft.Data.Sqlite"`)
- Create: `src/ConversorLibros.Web/Services/Library/LibrarySchema.cs`
- Create: `src/ConversorLibros.Web/Services/Library/ILibraryContext.cs`
- Create: `src/ConversorLibros.Web/Services/Library/LibraryContext.cs`
- Create: `src/ConversorLibros.Web/Services/Library/LibraryModels.cs`
- Create: `src/ConversorLibros.Web/Services/Library/SqliteLibraryRepository.cs`
- Modify: `src/ConversorLibros.Web/Endpoints/SettingsEndpoints.cs`
- Test: `tests/ConversorLibros.Web.Tests/SqliteLibraryRepositoryTests.cs`
- Test: `tests/ConversorLibros.Web.Tests/SettingsEndpointsTests.cs`

**Interfaces:**
- Consumes: `JsonSettingsStore`, `ConversionJobManager` (luego `ConversionQueueService`)
- Produces:
  - `ILibraryContext`: `string LibraryRoot { get; }`, `string DatabasePath { get; }`, `SqliteConnection OpenConnection()`, `void EnsureInitialized()`
  - `SqliteLibraryRepository`:
    - `Task<BookRecord> InsertBookAsync(BookRecord book, IReadOnlyList<string> tags, CancellationToken ct)`
    - `Task<BookRecord?> GetByIdAsync(string id, CancellationToken ct)`
    - `Task<BookRecord?> FindBySha256Async(string sha256, CancellationToken ct)`
    - `Task<LibrarySearchResult> QueryAsync(LibraryQuery query, CancellationToken ct)` (incluye `Items` + `Facets`: autores, series, etiquetas y contadores)
    - `Task<BookRecord?> UpdateMetadataAsync(string id, UpdateBookMetadataRequest patch, CancellationToken ct)`
    - `Task UpdateAudioStateAsync(string id, string audioStatus, string? audioFormat, long? durationMs, string? voiceUsed, CancellationToken ct)`
    - `Task UpdateReadingProgressAsync(string id, int chapterIndex, double percent, DateTimeOffset lastReadAt, CancellationToken ct)`
    - `Task<bool> DeleteBookAsync(string id, CancellationToken ct)`
    - `Task<bool> HasActiveOrQueuedJobsAsync(CancellationToken ct)` y `Task<bool> HasActiveOrQueuedJobForBookAsync(string bookId, CancellationToken ct)`

- [ ] **Step 1: Add `Microsoft.Data.Sqlite` package and write failing repository + settings guard tests**

En `tests/ConversorLibros.Web.Tests/SqliteLibraryRepositoryTests.cs`:
- `InsertAndQuery_FiltersByAuthorSeriesTagAudioStatusAndSearchText`: insertar 3 libros con distintas series/etiquetas/estados y validar filtros combinados, ordenamiento (`title`, `author`, `series`, `created_at`) y contadores de facetas.
- `FindBySha256_ReturnsExistingBook`: validar detección de duplicados por `source_sha256`.
- `UpdateMetadata_ReplacesTagsAndUpdatesTimestamp`: validar edición de título, serie, índice, sinopsis y reemplazo limpio de etiquetas.

En `tests/ConversorLibros.Web.Tests/SettingsEndpointsTests.cs` **(Review Focus #1)**:
- `PutSettings_ChangingOutputRootWhileJobActive_Returns409AndDoesNotSave`: con un trabajo `queued` o `running` en la DB (o conversión activa), enviar `PUT /api/settings` cambiando `outputRoot` y verificar `HttpStatusCode.Conflict` y que `settingsStore.Get().OutputRoot` no cambió.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ConversorLibros.Web.Tests --filter "FullyQualifiedName~SqliteLibraryRepositoryTests|FullyQualifiedName~SettingsEndpointsTests"`
Expected: FAIL.

- [ ] **Step 3: Implement `LibrarySchema`, `LibraryContext`, `SqliteLibraryRepository`, and `SettingsEndpoints` guard**

1. `LibrarySchema.EnsureCreated(SqliteConnection conn)` crea tablas `books`, `tags`, `book_tags`, `conversion_jobs` e índices (`idx_books_sha256`, `idx_books_author`, `idx_books_series`, `idx_jobs_status`) versionados con `PRAGMA user_version = 1` y `PRAGMA foreign_keys = ON; PRAGMA journal_mode = WAL;`.
2. `LibraryContext` lee `settingsStore.Get().OutputRoot`, construye `<OutputRoot>/library.db` y ejecuta `EnsureInitialized()` de forma idempotente (con cache de última ruta inicializada).
3. `SqliteLibraryRepository` implementa todas las operaciones con parámetros SQL (sin concatenación) y `async`/`CancellationToken`.
4. En `SettingsEndpoints.cs`: antes de `store.Save(input)`, si `outputRoot` cambia y `HasActiveOrQueuedJobsAsync` (o `jobManager.IsRunning`) es `true`, devolver `Results.Conflict(new { error = "No se puede cambiar la carpeta de biblioteca mientras hay conversiones en curso o en cola." })`. Si cambia con la cola vacía, guardar y disparar `libraryContext.EnsureInitialized()`.

- [ ] **Step 4: Run Web tests to verify they pass**

Run: `dotnet test tests/ConversorLibros.Web.Tests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ConversorLibros.Web tests/ConversorLibros.Web.Tests
git commit -m "feat(web): add SQLite library catalog schema, repository, and OutputRoot change guard"
```

---

### Task 4: Migración de biblioteca legada, sidecars de solo texto e importación por lotes de EPUB

**Files:**
- Create: `src/ConversorLibros.Web/Services/Library/LegacyLibraryImporter.cs`
- Create: `src/ConversorLibros.Web/Services/Library/TextOnlyReaderSidecarWriter.cs`
- Create: `src/ConversorLibros.Web/Services/Library/BookImportService.cs`
- Test: `tests/ConversorLibros.Web.Tests/LegacyLibraryImporterTests.cs`
- Test: `tests/ConversorLibros.Web.Tests/BookImportServiceTests.cs`

**Interfaces:**
- Consumes: `ILibraryContext`, `SqliteLibraryRepository`, `EpubBookSource`, `LibraryScanner`, `ReaderTextSegmenter`
- Produces:
  - `LegacyLibraryImporter.SyncAsync(CancellationToken ct)` → `Task<int>` (número de libros legados nuevos registrados; idempotente)
  - `TextOnlyReaderSidecarWriter.WriteAsync(Book book, string bookDir, CancellationToken ct)` → escribe `<bookDir>/reader/index.json` y `chapter-NNN.json` con `hasAudio = false`, `durationMs = 0`, párrafos y oraciones listos para `ReaderPage`
  - `BookImportService.ImportAsync(IReadOnlyList<(string FileName, Stream Content)> files, CancellationToken ct)` → `Task<IReadOnlyList<ImportBookResultDto>>` con `Status` en `"imported" | "skipped" | "error"`

- [ ] **Step 1: Write failing tests for legacy import, text-only sidecars, and batch EPUB import**

En `tests/ConversorLibros.Web.Tests/LegacyLibraryImporterTests.cs`:
- `SyncAsync_RegistersExistingOpusFolderAndTopLevelM4bWithoutMovingFiles`: crear una carpeta Opus y un `.m4b` en la raíz, ejecutar `SyncAsync` dos veces y verificar que se registran exactamente 2 filas con `IsLegacy == true`, `HasEpub == false`, `AudioStatus == "ready"`, y que los archivos originales no se movieron.

En `tests/ConversorLibros.Web.Tests/BookImportServiceTests.cs` **(Review Focus #2)**:
- `ImportAsync_HandlesValidDuplicateAndCorruptInSameBatch`: pasar en un solo lote `[validEpub, sameValidEpubAgain, corruptBytes]` y verificar:
  - 1º: `Status == "imported"`, existe `<LibraryRoot>/books/<id>/source.epub` y `<LibraryRoot>/books/<id>/reader/index.json`, `AudioStatus == "none"`.
  - 2º: `Status == "skipped"`, `Reason == "duplicate"`, `ExistingBookId == first.BookId`.
  - 3º: `Status == "error"`, sin carpetas huérfanas bajo `<LibraryRoot>/books/`.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ConversorLibros.Web.Tests --filter "FullyQualifiedName~LegacyLibraryImporterTests|FullyQualifiedName~BookImportServiceTests"`
Expected: FAIL.

- [ ] **Step 3: Implement `LegacyLibraryImporter`, `TextOnlyReaderSidecarWriter`, and `BookImportService`**

1. `LegacyLibraryImporter` usa `LibraryScanner.Scan(outputRoot)` excluyendo el subdirectorio `books/`, verifica si `folder_path` ya existe en SQLite e inserta los nuevos con `is_legacy = 1`, `has_epub = 0`, `audio_status = 'ready'`.
2. `TextOnlyReaderSidecarWriter` segmenta cada `Chapter.RawText` en párrafos y oraciones (`ReaderTextSegmenter`) y escribe `reader/index.json` (marcando `hasAudio: false` en cada capítulo) y `reader/chapter-NNN.json` con escrituras atómicas `.tmp` → `File.Move`.
3. `BookImportService` procesa cada archivo del lote en un `try/catch` aislado: copia a temporal mientras calcula `SHA256`, consulta `FindBySha256Async`, parsea con `EpubBookSource`, crea `<LibraryRoot>/books/<guid>/`, guarda `source.epub` y portada, llama a `TextOnlyReaderSidecarWriter`, inserta en SQLite (con `Metadata.Subjects` como etiquetas iniciales) y limpia el directorio si ocurre cualquier error.

- [ ] **Step 4: Run Web tests to verify they pass**

Run: `dotnet test tests/ConversorLibros.Web.Tests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ConversorLibros.Web/Services/Library tests/ConversorLibros.Web.Tests
git commit -m "feat(web): add legacy library migration, text-only reader sidecars, and batch EPUB import"
```

---

### Task 5: API REST completa de biblioteca (búsqueda, facetas, importación, edición y borrado seguro)

**Files:**
- Modify: `src/ConversorLibros.Web/Endpoints/LibraryEndpoints.cs`
- Modify: `src/ConversorLibros.Web/Services/LibraryBookResolver.cs` (resolver por `bookId` en `SqliteLibraryRepository`)
- Modify: `src/ConversorLibros.Web/Program.cs` (registrar `ILibraryContext`, `SqliteLibraryRepository`, `LegacyLibraryImporter`, `BookImportService` y ejecutar `SyncAsync` al arrancar)
- Modify: `src/ConversorLibros.Web/Endpoints/SettingsEndpoints.cs` (invocar `LegacyLibraryImporter.SyncAsync` tras cambio de `OutputRoot`)
- Test: `tests/ConversorLibros.Web.Tests/LibraryEndpointsTests.cs`

**Interfaces:**
- Consumes: `SqliteLibraryRepository`, `BookImportService`, `LegacyLibraryImporter`, `ILibraryContext`
- Produces:
  - `GET /api/library?q=&author=&series=&tag=&audioStatus=&sort=&dir=` → `LibraryCatalogResponse` (`items`, `facets`, `totalCount`)
  - `POST /api/library/import` (`multipart/form-data`, campo `files`) → `200 OK` con `{ results: ImportBookResultDto[] }`
  - `GET /api/library/{id}` → `LibraryBookDetailDto` (metadatos completos, etiquetas, capítulos, estado de audio, progreso)
  - `PATCH /api/library/{id}` → `200 OK` `LibraryBookDetailDto`
  - `PUT /api/library/{id}/cover` (`multipart/form-data`, campo `file`) → `204 No Content`
  - `DELETE /api/library/{id}` → `204 No Content` | `404` | `409`

- [ ] **Step 1: Write failing endpoint integration tests covering catalog, import, metadata edit, and safe delete**

En `tests/ConversorLibros.Web.Tests/LibraryEndpointsTests.cs`:
- `ImportThenQueryAndPatchMetadata_ReturnsUpdatedFacetsAndDetail`: subir EPUB a `POST /api/library/import`, consultar `GET /api/library?q=...`, editar serie/etiquetas con `PATCH /api/library/{id}` y validar facetas actualizadas.
- `DeleteBook_ManagedDeletesFolder_LegacyKeepsDiskFiles_ActiveJobReturns409` **(Review Focus #4)**:
  - Libro gestionado (`is_legacy = 0`): `DELETE` devuelve `204` y la carpeta `<LibraryRoot>/books/<id>` desaparece.
  - Libro legado (`is_legacy = 1`): `DELETE` devuelve `204`, desaparece de `GET /api/library`, pero los archivos legados en disco siguen intactos.
  - Libro con trabajo `queued`/`running`: `DELETE` devuelve `409 Conflict` y nada se borra.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ConversorLibros.Web.Tests --filter "FullyQualifiedName~LibraryEndpointsTests"`
Expected: FAIL.

- [ ] **Step 3: Implement updated `LibraryEndpoints`, `LibraryBookResolver`, and DI wiring in `Program.cs`**

1. Actualizar `LibraryBookResolver` para buscar primero por `id` en `SqliteLibraryRepository` y devolver la ruta absoluta validada bajo `LibraryRoot`.
2. Implementar los endpoints en `LibraryEndpoints.cs` conservando `GET /{id}/cover`, `GET /{id}/download` y `POST /{id}/open`.
3. En `DELETE /{id}`: comprobar `HasActiveOrQueuedJobForBookAsync` (`409`); si `!book.IsLegacy`, validar con `Path.GetFullPath` que `bookDir` está estrictamente dentro de `Path.Combine(LibraryRoot, "books")` antes de `Directory.Delete(bookDir, recursive: true)`.
4. Registrar los servicios en `Program.cs` y correr `LegacyLibraryImporter.SyncAsync` al iniciar el host.

- [ ] **Step 4: Run all backend tests**

Run: `dotnet test ConversorLibros.sln`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ConversorLibros.Web tests/ConversorLibros.Web.Tests
git commit -m "feat(web): expose SQLite-backed Calibre library REST endpoints"
```

---

### Task 6: Interfaz estilo Calibre en React (3 paneles, tabla/grilla, inspector y editor de metadatos)

**Files:**
- Modify: `src/ConversorLibros.Web/ClientApp/src/shared/api/types.ts`
- Modify: `src/ConversorLibros.Web/ClientApp/src/shared/api/client.ts`
- Create: `src/ConversorLibros.Web/ClientApp/src/features/library/LibraryFacetsSidebar.tsx`
- Create: `src/ConversorLibros.Web/ClientApp/src/features/library/LibraryTableView.tsx`
- Create: `src/ConversorLibros.Web/ClientApp/src/features/library/BookInspectorPanel.tsx`
- Create: `src/ConversorLibros.Web/ClientApp/src/features/library/EditMetadataModal.tsx`
- Modify: `src/ConversorLibros.Web/ClientApp/src/features/library/LibraryPage.tsx`
- Test: `src/ConversorLibros.Web/ClientApp/src/features/library/__tests__/LibraryPage.test.tsx`

**Interfaces:**
- Consumes: `GET /api/library`, `POST /api/library/import`, `GET/PATCH /api/library/{id}`, `PUT /api/library/{id}/cover`, `DELETE /api/library/{id}`
- Produces:
  - Tipos TS `LibraryCatalogResponse`, `LibraryBookDetailDto`, `LibraryFacetsDto`, `UpdateBookMetadataRequest`, `ImportBookResultDto` y métodos correspondientes en `api` (`client.ts`).
  - `LibraryPage` con prop/callback interno para abrir el modal **"Convertir a audiolibro"** (conectado en Task 9).

- [ ] **Step 1: Write failing Vitest tests for 3-pane Calibre UI**

En `src/ConversorLibros.Web/ClientApp/src/features/library/__tests__/LibraryPage.test.tsx`:
- `renders facets sidebar, toggles table/grid view, and updates right inspector on selection`: verificar cambio entre vista tabla y grilla de portadas, filtro por autor/etiqueta/estado y render de sinopsis + botones **Leer** y **Convertir a audiolibro** en el inspector.
- `edits metadata via modal and reflects updated title/series`: abrir *Editar metadatos*, modificar serie y etiquetas, guardar (`PATCH`) y verificar actualización en pantalla.
- `disables Leer button for legacy M4B without reader data`: verificar que el botón *Leer* aparece deshabilitado con título explicativo.

- [ ] **Step 2: Run Vitest to verify tests fail**

Run: `npx --prefix src/ConversorLibros.Web/ClientApp vitest run src/features/library/__tests__/LibraryPage.test.tsx`
Expected: FAIL.

- [ ] **Step 3: Implement API client extensions and 3-pane Calibre Library UI**

1. Añadir los DTOs y funciones `importBooks`, `getLibraryBook`, `updateBookMetadata`, `uploadBookCover`, `deleteBook` en `types.ts` y `client.ts`.
2. Construir `LibraryFacetsSidebar` (accesos rápidos + árboles colapsables Autores / Series / Etiquetas con contadores).
3. Construir `LibraryTableView` (columnas Título, Autor, Serie, Estado audio, Progreso, Fecha; doble clic navega a `/biblioteca/:id/leer`).
4. Construir `BookInspectorPanel` (portada, metadatos, barra de progreso, botones primarios **Leer** y **Convertir a audiolibro**, acciones secundarias *Editar metadatos*, *Descargar*, *Abrir carpeta*, *Eliminar* con confirmación que aclara el comportamiento en libros legados).
5. Construir `EditMetadataModal` (formulario de metadatos + subida de portada).
6. Integrar todo en `LibraryPage.tsx` con soporte *drag & drop* de EPUBs sobre la vista y resumen toast de resultados (`importados / duplicados omitidos / errores`).

- [ ] **Step 4: Run Vitest suite and TypeScript build**

Run: `npx --prefix src/ConversorLibros.Web/ClientApp vitest run && npm --prefix src/ConversorLibros.Web/ClientApp run build`
Expected: PASS y build limpio sin errores de TS.

- [ ] **Step 5: Commit**

```bash
git add src/ConversorLibros.Web/ClientApp
git commit -m "feat(ui): build 3-pane Calibre-style library catalog, inspector, and metadata editor"
```

---

## Fase 2 — Catálogo de voces, muestras de audio y cola de conversión

### Task 7: Descubrimiento de modelos Piper y endpoint de muestras de voz (`POST /api/tts/sample`)

**Files:**
- Modify: `src/ConversorLibros.Web/Services/PiperModelLocator.cs` (`ListAvailableModels()`)
- Create: `src/ConversorLibros.Web/Services/TtsSampleService.cs`
- Create: `src/ConversorLibros.Web/Endpoints/TtsEndpoints.cs`
- Modify: `src/ConversorLibros.Web/Program.cs` (registrar `TtsSampleService`, mapear `MapTtsEndpoints()`)
- Test: `tests/ConversorLibros.Web.Tests/PiperModelLocatorTests.cs`
- Test: `tests/ConversorLibros.Web.Tests/TtsEndpointsTests.cs`

**Interfaces:**
- Consumes: `PiperModelLocator`, `SqliteLibraryRepository`, `SpanishTextNormalizer`, `ChapterTextStatistics`, `ITtsEngine`
- Produces:
  - `PiperModelInfo(string Id, string DisplayName, string Directory, string OnnxFileName, IReadOnlyList<PiperVoiceInfo> Voices)`
  - `PiperModelLocator.ListAvailableModels(string? configuredDir)` → `IReadOnlyList<PiperModelInfo>`
  - `GET /api/tts/voices` → `IReadOnlyList<PiperModelInfo>`
  - `POST /api/tts/sample` body `TtsSampleRequest(string ModelId, string VoiceId, float Speed, string? Text, string? BookId, int? ChapterIndex)` → `200 OK` (`audio/wav`) | `400 Bad Request`

- [ ] **Step 1: Write failing tests for multi-model discovery and sample endpoint edge cases**

En `tests/ConversorLibros.Web.Tests/PiperModelLocatorTests.cs`:
- `ListAvailableModels_ReturnsAllValidModelFoldersAndReadsSpeakerCountFromJson`: crear 2 carpetas de modelo válidas (una con `*.onnx.json` declarando `num_speakers: 2`) y 1 incompleta; verificar que devuelve solo las 2 válidas con sus voces.

En `tests/ConversorLibros.Web.Tests/TtsEndpointsTests.cs` **(Review Focus #5)**:
- `PostSample_InvalidModelId_Returns400`: enviar `modelId: "inexistente"` y verificar `400 Bad Request`.
- `PostSample_EmptyChapterOrLongText_FallsBackAndCapsAt280Chars`: usando un `ITtsSampleSynthesizer` fake inyectado, verificar que (a) un texto de 500 caracteres se recorta a ≤ 280 antes de sintetizar, (b) un capítulo sin texto sintetizable cae al texto de demostración en español, y (c) dos llamadas idénticas hit-ean la caché y llaman al sintetizador solo una vez.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ConversorLibros.Web.Tests --filter "FullyQualifiedName~PiperModelLocatorTests|FullyQualifiedName~TtsEndpointsTests"`
Expected: FAIL.

- [ ] **Step 3: Implement `ListAvailableModels`, `TtsSampleService`, and `TtsEndpoints`**

1. Extender `PiperModelLocator` para enumerar todas las carpetas válidas sin duplicados (deduplicadas por ruta canónica) y leer `num_speakers` / `speaker_id_map` si existe `<modelo>.onnx.json` (fallback a speaker `"0"`).
2. Crear `ITtsSampleSynthesizer` + implementación real con `PiperSherpaTtsEngine` (ejecutada en `Task.Run` bajo un `SemaphoreSlim(1, 1)` para evitar picos de RAM con Piper) y caché LRU acotada (p. ej. 24 muestras en memoria por hash de `(normalizedText, modelId, voiceId, speed)`).
3. En `TtsSampleService`: si `BookId` está presente, leer las primeras 2 oraciones normalizadas del capítulo elegido (o del primer capítulo de contenido según `ChapterTextStatistics`); si queda vacío, usar frase de demostración en español; recortar a 280 caracteres en límite de palabra.
4. Mapear `GET /api/tts/voices` y `POST /api/tts/sample` en `TtsEndpoints.cs`.

- [ ] **Step 4: Run Web tests to verify they pass**

Run: `dotnet test tests/ConversorLibros.Web.Tests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ConversorLibros.Web tests/ConversorLibros.Web.Tests
git commit -m "feat(web): add Piper voice discovery and TTS sample preview endpoint"
```

---

### Task 8: Cola de conversión persistida, diccionario por libro y reconversión limpia

**Files:**
- Modify: `src/ConversorLibros.Web/Services/ConversionRunner.cs` (aceptar `BookId`, `ModelDir`, `VoiceId`, `PronunciationDictionary`, escribir salida en `<bookDir>/audio/` y `work/` en `<bookDir>/work/`)
- Create: `src/ConversorLibros.Web/Services/ConversionQueueService.cs`
- Create: `src/ConversorLibros.Web/Services/ConversionQueueWorker.cs`
- Modify: `src/ConversorLibros.Web/Endpoints/ConversionEndpoints.cs` (`POST /api/conversions/jobs`, `GET /api/conversions/jobs`, `DELETE /api/conversions/jobs/{jobId}`, SSE `/events`)
- Modify: `src/ConversorLibros.Web/Program.cs` (registrar `ConversionQueueService` y `AddHostedService<ConversionQueueWorker>()`)
- Test: `tests/ConversorLibros.Web.Tests/ConversionQueueTests.cs`

**Interfaces:**
- Consumes: `SqliteLibraryRepository`, `ILibraryContext`, `IConversionRunner`, `PiperModelLocator`, `JsonSettingsStore`
- Produces:
  - `EnqueueConversionRequest(string BookId, IReadOnlyList<int> ChapterIndexes, string? ModelId, string VoiceId, float Speed, bool Translate, string? SourceLanguage, AudiobookFormat Format, IReadOnlyDictionary<string, string>? PronunciationDictionary)`
  - `ConversionJobDto(string Id, string BookId, string BookTitle, string Status, string? Stage, double ProgressFraction, int? CurrentChapter, string? Error, string? Warning, DateTimeOffset CreatedAt, DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt)`
  - Endpoints `/api/conversions/jobs` (`GET`, `POST`), `/api/conversions/jobs/{jobId}` (`DELETE`), `/api/conversions/events` (SSE emitiendo eventos `queue-updated` y `job-progress`).

- [ ] **Step 1: Write failing tests for multi-book queue, cancellation, restart recovery, and reconversion cleanup**

En `tests/ConversorLibros.Web.Tests/ConversionQueueTests.cs`:
- `EnqueueTwoBooks_ProcessesSequentiallyAndUpdatesBookAudioStatus`: encolar libro A y libro B con un `IConversionRunner` fake; verificar que ambos se ejecutan en orden FIFO, sus filas pasan `queued -> running -> completed`, y `books.audio_status` pasa a `"ready"`.
- `CancelQueuedJob_MarksCancelledAndRevertsBookAudioStatus`: cancelar el 2º libro mientras el 1º corre y verificar que el 2º queda `cancelled`.
- `ReconvertBook_ReplacesPriorAudioFormatAndPreservesSourceAndCache` **(Review Focus #3)**: simular un libro ya convertido en Opus (`audio/001.opus` + `work/cache/abc.wav` + `source.epub`), reconvertir a `M4B` y verificar que los `.opus` anteriores se eliminan de `audio/`, el nuevo `.m4b` queda en `audio/`, y tanto `source.epub` como `work/cache/abc.wav` permanecen intactos.
- `WorkerStartup_ResetsInterruptedRunningJobsToFailed`: insertar un trabajo `running` en SQLite, iniciar el worker y verificar que se marca como `failed` (interrumpido) y no bloquea nuevos trabajos.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ConversorLibros.Web.Tests --filter "FullyQualifiedName~ConversionQueueTests"`
Expected: FAIL.

- [ ] **Step 3: Implement `ConversionQueueService`, `ConversionQueueWorker`, and updated `ConversionEndpoints`**

1. Persistir `pronunciation.json` en `<bookDir>/pronunciation.json` cuando el usuario provea un diccionario y pasarlo al pipeline.
2. En `CoreConversionRunner`: aceptar `BookDir`, `ModelDir`, `VoiceId`, `PronunciationDictionary`; al finalizar un export exitoso en `<bookDir>/audio.staging/`, reemplazar atómicamente `<bookDir>/audio/` (limpiando archivos del formato anterior) sin tocar `source.epub` ni `work/cache/`.
3. `ConversionQueueService` gestiona inserción en `conversion_jobs`, señalización por `Channel<string>`, cancelación por `CancellationTokenSource` del trabajo activo y difusión SSE.
4. `ConversionQueueWorker` (`BackgroundService`) marca trabajos huérfanos `running` como `failed` al iniciar y consume la cola secuencialmente actualizando `conversion_jobs` y `books`.
5. Exponer las nuevas rutas en `ConversionEndpoints.cs` (manteniendo compatibilidad de eventos SSE).

- [ ] **Step 4: Run all backend tests**

Run: `dotnet test ConversorLibros.sln`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ConversorLibros.Web tests/ConversorLibros.Web.Tests
git commit -m "feat(web): add persistent SQLite conversion queue, per-book pronunciation, and clean reconversion"
```

---

### Task 9: Modal "Convertir a audiolibro" con muestras de voz y panel de cola en React

**Files:**
- Modify: `src/ConversorLibros.Web/ClientApp/src/shared/api/types.ts` & `client.ts`
- Create: `src/ConversorLibros.Web/ClientApp/src/features/convert/ConvertBookModal.tsx`
- Create: `src/ConversorLibros.Web/ClientApp/src/features/convert/ConversionQueueDrawer.tsx`
- Modify: `src/ConversorLibros.Web/ClientApp/src/features/library/LibraryPage.tsx` (conectar botón **"Convertir a audiolibro"** al modal)
- Modify: `src/ConversorLibros.Web/ClientApp/src/app/Layout.tsx` (indicador de cola en barra lateral + botón para abrir `ConversionQueueDrawer`)
- Test: `src/ConversorLibros.Web/ClientApp/src/features/convert/__tests__/ConvertBookModal.test.tsx`

**Interfaces:**
- Consumes: `GET /api/tts/voices`, `POST /api/tts/sample`, `POST /api/conversions/jobs`, `GET /api/conversions/jobs`, `DELETE /api/conversions/jobs/{jobId}`, `GET /api/conversions/events`
- Produces:
  - `ConvertBookModal({ bookId, onClose, onEnqueued })`
  - `ConversionQueueDrawer({ open, onClose })`

- [ ] **Step 1: Write failing Vitest tests for `ConvertBookModal` sample playback and enqueueing**

En `src/ConversorLibros.Web/ClientApp/src/features/convert/__tests__/ConvertBookModal.test.tsx`:
- `loadsVoices_playsSample_andEnqueuesConversionJob`: mockear `getVoices`, `generateVoiceSample` (devuelve `Blob`) y `enqueueConversion`; elegir voz, pulsar **"Escuchar muestra"** y verificar llamada a `POST /api/tts/sample` con `{ modelId, voiceId, speed, bookId }`; luego pulsar **"Convertir a audiolibro"** y verificar llamada a `POST /api/conversions/jobs` y cierre del modal.

- [ ] **Step 2: Run Vitest to verify it fails**

Run: `npx --prefix src/ConversorLibros.Web/ClientApp vitest run src/features/convert/__tests__/ConvertBookModal.test.tsx`
Expected: FAIL.

- [ ] **Step 3: Implement `ConvertBookModal`, `ConversionQueueDrawer`, and integrate into `LibraryPage` & `Layout`**

1. Añadir `getVoices()`, `generateVoiceSample(req)` (devuelve `Blob` → `URL.createObjectURL`), `getConversionJobs()`, `enqueueConversion(req)` y `cancelConversionJob(jobId)` en `client.ts`.
2. Implementar `ConvertBookModal`: selector de modelo/voz, slider de velocidad, botón **"Escuchar muestra"** con reproductor `<audio>` inline (revocando object URLs al desmontar), lista de capítulos con selección rápida y estimación de tiempo, formato `Opus`/`M4B`, opciones Ollama y sección colapsable de diccionario de pronunciación.
3. Implementar `ConversionQueueDrawer` suscrito a `/api/conversions/events` (SSE) mostrando progreso en tiempo real, trabajos en cola, historial y botón cancelar.
4. Conectar `BookInspectorPanel` en `LibraryPage.tsx` para abrir `ConvertBookModal` y recargar el estado del catálogo al completarse un trabajo.

- [ ] **Step 4: Run Vitest and production build**

Run: `npx --prefix src/ConversorLibros.Web/ClientApp vitest run && npm --prefix src/ConversorLibros.Web/ClientApp run build`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ConversorLibros.Web/ClientApp
git commit -m "feat(ui): add ConvertBookModal with voice sample preview and conversion queue drawer"
```

---

## Fase 3 — Lector unificado (solo texto + read-along) y mejoras de lectura

### Task 10: Backend del lector unificado (capítulos mixtos, M4B, formato ligero y progreso en SQLite)

**Files:**
- Modify: `src/ConversorLibros.Core/Models/ReaderModels.cs` (`ReaderParagraph.Kind`, `ReaderTextSpan`, `ReaderIndexChapter.HasAudio`, `AudioOffsetMs`)
- Modify: `src/ConversorLibros.Core/Services/EpubBookSource.cs` (preservar marcadores de encabezado/separador/énfasis ligero sin alterar `Chapter.RawText`)
- Modify: `src/ConversorLibros.Web/Services/ReaderDataPublisher.cs` (habilitar M4B con `AudioOffsetMs` acumulados por capítulo + fusionar con capítulos solo-texto existentes)
- Modify: `src/ConversorLibros.Web/Services/Library/TextOnlyReaderSidecarWriter.cs` (emitir `Kind` y `HasAudio = false`)
- Modify: `src/ConversorLibros.Web/Endpoints/ReaderEndpoints.cs` (servir audio Opus por capítulo o M4B con Range + reflejar `PUT /reader/progress` en SQLite)
- Test: `tests/ConversorLibros.Web.Tests/ReaderDataPublisherTests.cs`
- Test: `tests/ConversorLibros.Web.Tests/ReaderEndpointsTests.cs`

**Interfaces:**
- Consumes: `SqliteLibraryRepository`, `ReaderStore`, `EpubBookSource`
- Produces:
  - `ReaderIndexResponse` con `bool HasAudio`, `string? AudioFormat`, y `chapters[i]` con `bool HasAudio`, `long? AudioOffsetMs`
  - `ReaderParagraph` con `string Kind = "paragraph"` (`"heading" | "paragraph" | "separator"`) y `ReaderSentence` con `IReadOnlyList<ReaderTextSpan>? Spans = null` (`Text`, `Italic`, `Bold`)
  - `GET /api/library/{id}/chapters/{n}/audio` soporta `audio/ogg` (Opus) y `audio/mp4` (M4B) con `enableRangeProcessing: true`.

- [ ] **Step 1: Write failing tests for mixed chapters, M4B sidecars, and SQLite progress sync**

En `tests/ConversorLibros.Web.Tests/ReaderDataPublisherTests.cs` **(Review Focus #3)**:
- `PublishAsync_M4bFormat_WritesIndexWithOffsetsAndPreservesUnconvertedTextChapters`: partir de un libro con 3 capítulos solo-texto en `reader/`, publicar `ChapterSyncMap` solo para el capítulo 2 con `AudiobookFormat.M4b` y verificar:
  - `index.json` mantiene los 3 capítulos; capítulos 1 y 3 tienen `HasAudio == false`; capítulo 2 tiene `HasAudio == true` y `AudioOffsetMs == 0`.
  - `AudioFormat == "m4b"`.

En `tests/ConversorLibros.Web.Tests/ReaderEndpointsTests.cs`:
- `PutProgress_UpdatesReaderJsonAndSqliteBookReadingPercent`: llamar a `PUT /api/library/{id}/reader/progress` y verificar que tanto `reader/progress.json` como `books.reading_chapter` / `books.reading_percent` en SQLite se actualizan.
- `GetChapterAudio_ServesM4bWithRangeSupport`: verificar `200`/`206` con `Content-Type: audio/mp4` para libro M4B.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ConversorLibros.Web.Tests --filter "FullyQualifiedName~ReaderDataPublisherTests|FullyQualifiedName~ReaderEndpointsTests"`
Expected: FAIL.

- [ ] **Step 3: Implement mixed-chapter publishing, M4B audio serving, lightweight formatting, and progress sync**

1. Extender `ReaderModels.cs` con propiedades opcionales con valores por defecto (compatibles hacia atrás con JSON existentes).
2. En `ReaderDataPublisher.PublishAsync`: quitar el early-return para `M4b`, calcular `AudioOffsetMs` acumulados para M4B y fusionar los capítulos recién sincronizados con los capítulos solo-texto ya presentes en `reader/index.json` para que ningún capítulo no convertido desaparezca.
3. En `EpubBookSource`: al extraer texto de bloques HTML (`h1..h6`, `hr`, `p`), conservar metadatos ligeros opcionales de formato (`heading`, `separator`, `em`, `strong`) sin cambiar el texto plano que consume `SpanishTextNormalizer` ni `ReaderTextAligner`.
4. En `ReaderEndpoints.cs`: resolver audio Opus o M4B en `GET /chapters/{n}/audio` y llamar a `repository.UpdateReadingProgressAsync(...)` en `PUT /reader/progress`.

- [ ] **Step 4: Run full .NET test suite**

Run: `dotnet test ConversorLibros.sln`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ConversorLibros.Core src/ConversorLibros.Web tests/ConversorLibros.Web.Tests
git commit -m "feat(reader): support text-only chapters, M4B playback offsets, light formatting, and SQLite progress sync"
```

---

### Task 11: Frontend del lector unificado (`ReaderPage` solo texto + read-along Opus/M4B + atajos de teclado)

**Files:**
- Modify: `src/ConversorLibros.Web/ClientApp/src/shared/api/types.ts` (`hasAudio`, `audioFormat`, `audioOffsetMs`, `kind`, `spans`)
- Modify: `src/ConversorLibros.Web/ClientApp/src/features/reader/useAudioSync.ts` (soporte `audioOffsetMs` para M4B y desactivación limpia cuando `!chapterHasAudio`)
- Modify: `src/ConversorLibros.Web/ClientApp/src/features/reader/PaginatedChapter.tsx` (renderizado de `heading`, `separator`, `em`/`strong`)
- Modify: `src/ConversorLibros.Web/ClientApp/src/features/reader/ReaderPage.tsx` (modo solo lectura + botón **"Convertir a audiolibro"** + atajos `ArrowLeft`/`ArrowRight`/`Space`/`Escape`)
- Test: `src/ConversorLibros.Web/ClientApp/src/features/reader/__tests__/ReaderPage.test.tsx`

**Interfaces:**
- Consumes: `ReaderIndexDto` (con `hasAudio` por capítulo y `audioFormat`), `ConvertBookModal` (de Task 9)
- Produces: Experiencia completa de lectura en `/biblioteca/:id/leer` para libros solo texto, mixtos, Opus y M4B.

- [ ] **Step 1: Write failing Vitest tests for text-only mode, M4B offset sync, formatting, and keyboard shortcuts**

En `src/ConversorLibros.Web/ClientApp/src/features/reader/__tests__/ReaderPage.test.tsx`:
- `rendersTextOnlyBookWithoutAudioToolbarAndShowsConvertButton`: con `hasAudio: false`, verificar que el capítulo se pagina y lee normalmente, no aparece el botón Play y sí aparece **"Convertir a audiolibro"**.
- `handlesKeyboardShortcutsForPagingPlayAndEscape`: verificar `ArrowRight` avanza página, `ArrowLeft` retrocede, `Space` alterna Play/Pausa cuando hay audio (y se ignora si el foco está en un `<input>` o `<textarea>`).
- `appliesAudioOffsetForM4bChapters`: con `audioFormat: "m4b"` y `audioOffsetMs: 120000`, verificar que `seekTo(5000)` posiciona `audio.currentTime` en `125` segundos y que `entryAtMs` resta `120000ms`.

- [ ] **Step 2: Run Vitest to verify tests fail**

Run: `npx --prefix src/ConversorLibros.Web/ClientApp vitest run src/features/reader/__tests__/ReaderPage.test.tsx`
Expected: FAIL.

- [ ] **Step 3: Implement unified `ReaderPage`, M4B offset support in `useAudioSync`, light formatting, and keyboard shortcuts**

1. En `useAudioSync.ts`: aceptar `enabled: boolean` y `audioOffsetMs?: number` (default `0`), ajustando `seekTo`, lectura de `currentTime` y condición de fin de capítulo cuando `audioFormat === "m4b"` (`currentTime * 1000 >= audioOffsetMs + durationMs`).
2. En `PaginatedChapter.tsx`: renderizar `kind === "heading"` como `<h2>`, `kind === "separator"` como `<hr />` y `spans` con `<em>`/`<strong>` preservando los `data-entry-index` de resaltado.
3. En `ReaderPage.tsx`:
   - Evaluar `chapterHasAudio = currentMeta?.hasAudio ?? false`.
   - Ocultar `ReaderToolbar` de audio si `!chapterHasAudio` y mostrar botón **"Convertir a audiolibro"** en la barra superior que abre `ConvertBookModal`.
   - Añadir listener `keydown` global respetando inputs enfocados para `ArrowLeft`, `ArrowRight`, `Space` y `Escape`.
   - Guardar progreso por capítulo/página incluso en modo solo texto.

- [ ] **Step 4: Run full frontend + backend verification**

Run: `npx --prefix src/ConversorLibros.Web/ClientApp vitest run && npm --prefix src/ConversorLibros.Web/ClientApp run build && dotnet test ConversorLibros.sln`
Expected: PASS en todos los tests de frontend, build de Vite limpio y 100 % verde en xUnit.

- [ ] **Step 5: Commit**

```bash
git add src/ConversorLibros.Web/ClientApp
git commit -m "feat(reader): unify text-only and read-along reader with M4B sync, formatting, and shortcuts"
```
