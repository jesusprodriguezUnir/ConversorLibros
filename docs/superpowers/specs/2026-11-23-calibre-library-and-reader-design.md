# Diseño: Evolución de ConversorLibros hacia Biblioteca estilo Calibre, Lector Unificado y Conversor con Muestras

## Intención y contexto

`ConversorLibros` nació como un conversor offline de EPUB (sin DRM) a audiolibro en español (`es-ES`) y hoy ya cuenta con una base sólida en `ConversorLibros.Core` y `ConversorLibros.Web` (ASP.NET Core + React/Vite), incluyendo caché resumible, traducción local opcional con Ollama, exportación a Opus/M4B y un lector *read-along* con paso automático de páginas sincronizado con audio (`ReaderPage`).

Sin embargo, hoy la aplicación sigue estructurada como un **conversor con una lista plana de audiolibros**:
- Los EPUBs subidos viven en un directorio temporal (máximo 5) y no forman parte de una biblioteca persistente.
- Los metadatos ricos que `EpubBookSource` ya extrae (series, índice de serie, ISBN, editorial, etiquetas, sinopsis) se descartan tras el `inspect`.
- No es posible leer un EPUB sin haberlo convertido primero a Opus.
- No existe selector de voces/modelos en la UI, ni forma de escuchar una muestra antes de lanzar una conversión larga, ni cola de conversión para múltiples libros.

El objetivo de esta evolución es transformar `ConversorLibros.Web` en una **aplicación personal de escritorio (web local en Windows) centrada en la biblioteca**: una interfaz estilo Calibre donde el usuario gestiona sus libros, los lee al instante y, desde cualquier libro, puede escuchar muestras de voz, convertirlo a audiolibro en segundo plano y disfrutarlo en el lector escuchando mientras las páginas pasan solas.

---

## Objetivos, no-objetivos y anti-objetivos

### Objetivos
1. **Catálogo persistente estilo Calibre:** importar EPUBs a una biblioteca propia respaldada por SQLite, conservando archivo original, portada y metadatos completos (título, autor, serie e índice, etiquetas, editorial, fecha, ISBN, sinopsis).
2. **Interfaz de biblioteca de escritorio:** layout de tres paneles (facetas/filtros laterales, catálogo con vista de tabla y grilla de portadas, panel lateral de detalle con edición de metadatos y acciones rápidas: *Leer*, *Convertir a audiolibro*, *Descargar*, *Eliminar*).
3. **Lectura inmediata + lectura sincronizada:** abrir y leer cualquier EPUB importado sin esperar conversión; una vez convertido a audiolibro, el mismo lector habilita reproducción sincronizada con resaltado y paso automático de hojas y capítulos (soportando tanto Opus como M4B).
4. **Botón "Convertir a audiolibro" con muestras de voz y cola:** desde cualquier libro, elegir modelo/voz Piper y velocidad, **escuchar una muestra rápida** antes de convertir y encolar conversiones en segundo plano con estado persistido.
5. **Saneamiento técnico previo:** corregir bugs detectados en el pipeline actual (`PauseAfter` sin silencio real, hash de chunk dependiente de la cultura del SO, WAVs temporales sin limpiar en Web) y simplificar mantenimiento dejando de dar soporte activo a la app MAUI (`ConversorLibros.App`).

### No-objetivos
- Integración o sincronización bidireccional con una base de datos externa de Calibre (`metadata.db`).
- Soporte para formatos distintos de EPUB sin DRM (PDF, MOBI, AZW3, CBZ) en esta evolución.
- Acceso multiusuario, autenticación, despliegue en la nube o diseño móvil/tablet prioritario (uso personal local en PC con Windows por ahora).
- Renderizado EPUB nativo completo mediante `epub.js` / `foliate-js` (ver sección de Enfoques).
- Síntesis TTS en tiempo real/streaming bajo demanda al abrir un libro sin convertir (la conversión sigue siendo un trabajo explícito en segundo plano).

### Anti-objetivos
- Duplicar lógica de normalización, chunking, traducción, TTS o empaquetado fuera de `ConversorLibros.Core`.
- Romper compatibilidad con los audiolibros ya convertidos en disco en la carpeta de salida actual.
- Introducir dependencias en la nube o telemetría (la aplicación debe permanecer 100 % offline).
- Romper el alineamiento de texto/audio cuando el libro se traduce localmente con Ollama.

---

## Restricciones

- **Plataforma y runtime:** Windows x64, .NET 10 (`net10.0`), uso local en PC.
- **Dependencias fijadas:** `org.k2fsa.sherpa.onnx` y `org.k2fsa.sherpa.onnx.runtime.win-x64` deben permanecer en **1.13.8** (versiones 2.x producen audio silencioso con los modelos Piper actuales).
- **Convenciones del repositorio:** interfaz de usuario y documentación en español; código, identificadores y comentarios en inglés.
- **Invariantes del pipeline:**
  - `TextChunk.Hash` debe ser determinista e independiente de la cultura del SO (`CultureInfo.InvariantCulture`).
  - `OllamaTranslator.PromptVersion` controla la invalidación de caché de traducción.
  - El ensamblado de capítulos transmite a disco con `FileStream` sin cargar capítulos enteros en memoria.
  - Invocaciones externas (`ffmpeg`, `explorer.exe`) siempre mediante `ProcessStartInfo.ArgumentList`.
  - Síntesis de Piper es síncrona; siempre debe ejecutarse fuera del hilo de peticiones HTTP (`Task.Run` / `BackgroundService`).

---

## Enfoques evaluados y decisión

### Enfoque 1: Evolucionar el lector actual + catálogo SQLite *(Seleccionado por el usuario)*
- **Descripción:** introducir SQLite embebido para el catálogo estilo Calibre, generar sidecars de lectura de texto al importar cada EPUB y reutilizar `ReaderPage` (`useAudioSync`, `useAutoFollow`, `PaginatedChapter`, `SyncMapBuilder`, `ReaderTextAligner`) añadiendo modo solo-lectura, soporte M4B y formato ligero (títulos, cursivas, negritas, saltos de escena).
- **Tradeoffs:** reutiliza la mayor parte del código y tests ya existentes, mantiene compatibilidad total con libros traducidos por Ollama y permite entregas incrementales seguras. Sacrifica fidelidad visual en EPUBs con maquetación compleja (tablas elaboradas, CSS arbitrario o ilustraciones incrustadas).

### Enfoque 2: Reemplazar el lector por un motor EPUB nativo (`foliate-js` / `epub.js`) *(Descartado)*
- **Descripción:** renderizar el XHTML/CSS/imágenes originales del EPUB en un iframe/motor dedicado y reimplementar la sincronización de audio y paso de páginas sobre ese DOM.
- **Tradeoffs:** ofrece fidelidad visual completa con el EPUB original, pero acoplar resaltado frase a frase y auto-paso de página a un motor EPUB es frágil, obliga a reescribir el lector actual y se rompe cuando el libro se traduce con Ollama (el texto hablado deja de corresponderse con el XHTML original).

---

## Arquitectura y diseño por fases

> **Convención de decisiones:** los requisitos funcionales de biblioteca estilo Calibre, botón de conversión con muestras de voz, enfoque 1 y foco exclusivo en PC Windows provienen del usuario. Las elecciones de implementación (SQLite con `Microsoft.Data.Sqlite`, estructura de carpetas, contratos de API y desglose en 4 fases) son propuestas del arquitecto.

### Fase 0 — Saneamiento técnico y corrección de bugs
Antes de sumar capas nuevas, se estabiliza la base actual:
1. **Hash invariante a la cultura:** en `Core/Services/SentenceChunker.cs`, formatear `speed` con `CultureInfo.InvariantCulture` al construir el hash SHA-256. *(Nota: en máquinas `es-ES` esto invalida cachés previas que usaban `,` decimal; se acepta explícitamente por ser un bug de determinismo.)*
2. **Pausas reales en el audio:** propagar `TextChunk.PauseAfter` desde `BookConversionPipeline` hasta `IChapterAssembler` / `WavChapterAssembler` para escribir muestras PCM de silencio correspondientes entre chunks/párrafos (verificando que `SyncMapBuilder` contabilice la duración real del WAV ensamblado con pausas).
3. **Limpieza y recursos en Web:**
   - Eliminar `work/chapter_NNN.wav` tras exportación exitosa cuando el ajuste `deleteWavAfterExport` esté activo (por defecto `true`).
   - Evitar re-parsear el EPUB dentro de `BookConversionPipeline` cuando el invocador ya dispone de la instancia `Book` (o añadir sobrecarga que acepte `Book`).
   - Reemplazar `new HttpClient()` en `CoreConversionRunner` por `IHttpClientFactory`.
4. **Higiene del repositorio:**
   - Eliminar `src/ConversorLibros.Web/ClientApp/.vite/` del control de versiones y añadirlo a `.gitignore`.
   - Documentar `ConversorLibros.App` (MAUI) como archivado/sin mantenimiento activo en `CLAUDE.md` y `README.md` (sin borrarlo aún por seguridad).

---

### Fase 1 — Catálogo SQLite, importación de EPUBs e interfaz estilo Calibre

#### Almacenamiento y estructura en disco
Se introduce una base de datos SQLite embebida (`Microsoft.Data.Sqlite`) ubicada en `<LibraryRoot>/library.db` (donde `LibraryRoot` reutiliza el `OutputRoot` configurado por el usuario para mantener la biblioteca autocontenida y portable).

Estructura por libro en `<LibraryRoot>/books/<bookId>/`:
```text
<LibraryRoot>/
├── library.db
└── books/
    └── <bookId>/              # GUID corto o slug único inmutable
        ├── source.epub        # Copia del EPUB importado (si procede de importación)
        ├── cover.jpg|png      # Portada extraída o personalizada
        ├── reader/            # Sidecars de lectura (index.json, chapter-NNN.json, progress.json, bookmarks.json)
        ├── audio/             # Audiolibro exportado (*.opus o <titulo>.m4b)
        └── work/              # Temporales de conversión (cache/, translation-cache/, chapter_NNN.wav)
```

#### Migración no destructiva de libros existentes
Al arrancar la aplicación (o al cambiar `LibraryRoot` en Ajustes), un servicio de sincronización (`LegacyLibraryImporter`) detecta audiolibros ya existentes en el layout antiguo de `<OutputRoot>` (carpetas `<OutputRoot>/<Title>/` y archivos sueltos `<OutputRoot>/<Title>.m4b`) y los registra en `library.db` con `is_legacy = 1`, `has_epub = 0` y `audio_status = 'ready'`, sin mover ni borrar archivos del usuario.

#### Modelo de datos (SQLite)
- **`books`**:
  - `id` (TEXT PK), `title`, `sort_title`, `author`, `series`, `series_index` (REAL NULL), `publisher`, `published_date`, `isbn`, `description`, `language`
  - `folder_path` (TEXT, relativa a `LibraryRoot`; para M4B legados apunta al archivo o carpeta correspondiente), `is_legacy` (INTEGER DEFAULT 0), `source_sha256` (TEXT NULL, indexado para evitar duplicados), `has_epub` (INTEGER), `has_cover` (INTEGER)
  - `audio_status` (TEXT: `none` | `queued` | `converting` | `ready` | `error`), `audio_format` (`opus` | `m4b` | NULL), `audio_duration_ms` (INTEGER NULL), `chapter_count` (INTEGER), `voice_used` (TEXT NULL)
  - `reading_chapter` (INTEGER NULL), `reading_percent` (REAL DEFAULT 0), `last_read_at` (TEXT NULL)
  - `created_at`, `updated_at`
- **`tags`** (`id`, `name` UNIQUE) y **`book_tags`** (`book_id`, `tag_id`).
- **`conversion_jobs`** (usada en Fase 2; tabla creada desde el esquema inicial o en migración incremental): `id`, `book_id`, `status` (`queued` | `running` | `completed` | `failed` | `cancelled`), `options_json`, `progress_percent`, `current_stage`, `error_message`, `created_at`, `started_at`, `finished_at`.

#### Cambio de `LibraryRoot` (`OutputRoot`) en caliente *(decisión del arquitecto)*
- Los repositorios SQLite y `ConversionQueueWorker` resuelven la ruta de `library.db` a través de una abstracción de contexto (`ILibraryContext`) ligada al `OutputRoot` vigente, en lugar de fijar la conexión al arrancar.
- Si existen trabajos `queued` o `running`, `PUT /api/settings` rechaza cambiar `OutputRoot` con `409 Conflict` indicando que hay conversiones activas. Si la cola está vacía, el cambio aplica en caliente: inicializa/abre `library.db` en la nueva carpeta y ejecuta `LegacyLibraryImporter`.

#### Flujo de importación inmediata y duplicados *(decisión del arquitecto)*
1. El usuario arrastra uno o varios `.epub` sobre la biblioteca o pulsa **"Añadir libros"**.
2. `POST /api/library/import` (`multipart/form-data`) calcula el SHA-256 (`source_sha256`) de cada EPUB recibido. Si ya existe un libro con el mismo `source_sha256` en `books`, lo omite sin duplicarlo y devuelve en la respuesta por archivo `{ status: "skipped", reason: "duplicate", existingBookId }`.
3. Para cada EPUB nuevo, guarda `source.epub`, extrae `BookMetadata` + portada con `EpubBookSource` y genera los sidecars de texto en `reader/` (`index.json` + `chapter-NNN.json` con `hasAudio = false` y `durationMs = 0`) mediante un nuevo `TextOnlyReaderSidecarWriter` (compartiendo el mismo formato JSON de `ReaderChapter`).
4. El libro aparece inmediatamente en la biblioteca con `audio_status = 'none'` y queda listo para **Leer** al instante.

#### API de Biblioteca (evolución de `LibraryEndpoints`)
- `GET /api/library?q=&author=&series=&tag=&audioStatus=&sort=&dir=` — consulta indexada sobre SQLite (elimina el escaneo de disco en cada request) + resumen de facetas (`authors`, `series`, `tags`, `counts`) para el panel izquierdo.
- `POST /api/library/import` — importa uno o varios EPUBs y devuelve el resultado por archivo (`imported`, `skipped`, `error`).
- `GET /api/library/{id}` — ficha completa del libro (metadatos, etiquetas, estado de audio, progreso de lectura, lista de capítulos).
- `PATCH /api/library/{id}` — actualiza metadatos editables (título, autor, serie, índice, etiquetas, editorial, fecha, ISBN, sinopsis).
- `PUT /api/library/{id}/cover` — reemplaza la portada.
- `DELETE /api/library/{id}` — rechaza con `409` si hay una conversión activa/encolada para ese libro. Para libros gestionados (`is_legacy = 0`), elimina el registro de SQLite y su carpeta bajo `<LibraryRoot>/books/<bookId>` con validación estricta de contención. Para libros legados (`is_legacy = 1`), elimina únicamente el registro del catálogo sin borrar archivos legados en disco (y la UI lo aclara en la confirmación).
- Se conservan `GET /api/library/{id}/cover`, `GET /api/library/{id}/download` y `POST /api/library/{id}/open` (adaptados a resolver por `bookId` en SQLite).

#### Interfaz estilo Calibre (React)
Layout de escritorio de **3 zonas** en `/biblioteca`:
1. **Panel izquierdo (Explorador de facetas):** accesos rápidos (*Todos los libros*, *Con audiolibro*, *Solo lectura*, *En conversión*) + árboles colapsables de **Autores**, **Series** y **Etiquetas** con contadores.
2. **Panel central (Catálogo):**
   - Barra superior con botón **"Añadir libros"** (también soporta *drag & drop* global), buscador instantáneo, selector de ordenamiento y conmutador de vista **Tabla** (estilo Calibre clásico: Título, Autor, Serie, Estado audio, Progreso, Fecha) / **Grilla de portadas**.
   - Seleccionar una fila o tarjeta actualiza el panel derecho; doble clic abre el lector.
3. **Panel derecho (Inspector de libro):**
   - Portada grande, título, autor, serie, etiquetas, sinopsis, progreso de lectura y estado del audiolibro.
   - Acciones primarias visibles: **"Leer"** y **"Convertir a audiolibro"** (o **"Reconvertir"** si ya tiene audio), más acciones secundarias (*Editar metadatos*, *Descargar audiolibro*, *Abrir carpeta*, *Eliminar*).
   - Modal/panel de **Editar metadatos** para modificar todos los campos y cambiar portada.

---

### Fase 2 — Catálogo de voces, muestras de audio y cola de conversión

#### Descubrimiento de modelos y voces
- Extender `PiperModelLocator` con `ListAvailableModels()` para devolver todos los directorios válidos (con `*.onnx`, `tokens.txt` y `espeak-ng-data/`) encontrados en las ubicaciones configuradas y detectadas, junto con metadatos básicos (nombre legible del modelo, archivo `.onnx`, número de speakers si existe `*.onnx.json`).
- Nuevo endpoint `GET /api/tts/voices` que expone los modelos y voces disponibles para poblar el selector en la UI.

#### Muestras de voz ("Escuchar muestra")
- Nuevo endpoint `POST /api/tts/sample`:
  - Entrada: `{ modelDir | modelId, voiceId, speed, text?, bookId?, chapterIndex? }`.
  - Si se pasa `bookId` (y opcionalmente `chapterIndex`), extrae las primeras 2 frases normalizadas del capítulo elegido (o del primer capítulo de contenido real usando la heurística existente de `ChapterTextStatistics`); si no, usa un texto de demostración en español (o el `text` provisto, limitado a 280 caracteres).
  - Sintetiza en `Task.Run` usando `PiperSherpaTtsEngine` (reutilizando una caché ligera en memoria/disco por hash de `(text, model, voice, speed)` para que repetir una muestra sea instantáneo) y devuelve `audio/wav`.
  - Serializa el acceso al sintetizador si hace falta o libera instancias limpiamente para no saturar memoria con múltiples modelos Piper cargados simultáneamente.

#### Modal / Panel "Convertir a audiolibro" desde la biblioteca
- Se abre desde el botón **"Convertir a audiolibro"** del inspector de cualquier libro (la ruta `/convertir` puede quedar como acceso directo o redirigir a importar + abrir este flujo).
- Permite configurar:
  - **Voz y velocidad:** selector de modelo/voz Piper + slider de velocidad + botón **"Escuchar muestra"** con mini reproductor inline.
  - **Capítulos:** lista con estadísticas (palabras, tiempo estimado, detección de preliminares) y vista previa del texto normalizado.
  - **Formato de salida:** `Opus` o `M4B`.
  - **Traducción local (Ollama):** activar/desactivar, idioma origen y modelo Ollama.
  - **Diccionario de pronunciación (opcional):** pares `término → pronunciación` persistidos por libro (`<book>/pronunciation.json`) y pasados a `SpanishTextNormalizer`.
- Al confirmar, encola el trabajo en `POST /api/conversions/jobs` y cierra el modal dejando feedback visible en la barra de estado/cola.

#### Cola de conversión persistida
- Reemplazar la restricción de "1 trabajo o `409 Conflict`" en `ConversionJobManager` por una cola secuencial respaldada por la tabla `conversion_jobs` en SQLite y ejecutada por un `BackgroundService` (`ConversionQueueWorker`).
- Endpoints:
  - `POST /api/conversions/jobs` — encola un libro y actualiza `books.audio_status = 'queued'`.
  - `GET /api/conversions/jobs` — lista trabajos activos, en cola e historial reciente.
  - `DELETE /api/conversions/jobs/{jobId}` — cancela un trabajo en cola o en curso.
  - `GET /api/conversions/events` (SSE) — emite eventos de progreso y cambios de estado de la cola (incluyendo `bookId` y `jobId`).
- Al reiniciar la aplicación, trabajos que quedaron en estado `running` se marcan como `failed` (interrumpidos) o `queued` reanudables gracias a `FileSystemChunkCache`.
- Al finalizar una conversión exitosa, se reemplazan los archivos anteriores en `audio/` (un único audiolibro activo por libro; se conserva `work/cache/` para reutilizar chunks sin cambios), se actualizan `books.audio_status = 'ready'`, `audio_format`, `audio_duration_ms`, `voice_used` y se actualizan los sidecars `reader/` correspondientes a los capítulos convertidos (si se activó traducción con Ollama, los capítulos convertidos muestran el texto traducido sincronizado con el audio; `source.epub` permanece intacto para poder reconvertir sin traducción si se desea).

---

### Fase 3 — Lector unificado (solo texto + read-along sincronizado) y mejoras de lectura

1. **Lector funcional con o sin audio (y capítulos mixtos) *(decisión del arquitecto)*:**
   - `ReaderIndexDto` incluye `hasAudio` a nivel de libro **y** `hasAudio` por capítulo (`chapters[i].hasAudio`), además de `audioFormat` (`opus` | `m4b` | `null`).
   - Si el usuario convierte solo un subconjunto de capítulos (por ejemplo omitiendo preliminares), los capítulos no convertidos **permanecen legibles en modo solo texto** junto a los capítulos con audio.
   - En un capítulo sin audio, `ReaderPage` oculta los controles de reproducción y muestra un botón discreto **"Convertir a audiolibro"** en la barra superior; en un capítulo con audio, habilita la barra de audio sincronizado con resaltado frase a frase y paso automático de páginas y capítulos (`useAudioSync` + `useAutoFollow`).
   - Para libros legados `.m4b` sin `reader/` sidecars ni `source.epub`, el botón **Leer** se muestra deshabilitado con explicación clara (se mantienen *Descargar* y *Abrir carpeta*).
   - Sincronizar el progreso de lectura (`reading_chapter`, `reading_percent`, `last_read_at`) también en SQLite para mostrar la barra de progreso en el catálogo estilo Calibre.
2. **Soporte M4B en el lector:**
   - Permitir que `ReaderDataPublisher` genere los sidecars de sincronización también cuando el formato exportado es `M4B` (almacenando el offset de inicio de cada capítulo dentro del archivo `.m4b` o manteniendo los tiempos por capítulo).
   - `GET /api/library/{id}/chapters/{n}/audio` sirve el `.opus` del capítulo o, para `M4B`, sirve el stream del `.m4b` con soporte de `Range` (y el frontend posiciona `audio.currentTime` en el offset del capítulo) o extrae/transmuxa el rango del capítulo. *(La estrategia exacta queda a criterio del implementador, priorizando seek instantáneo y simplicidad.)*
3. **Formato tipográfico ligero:**
   - Extender el modelo de `ReaderParagraph` / `ReaderSentence` con semántica básica opcional (por ejemplo `kind: "heading" | "paragraph" | "separator"` y marcas inline simples de énfasis `em` / `strong`), extraída en `EpubBookSource` sin alterar el texto plano normalizado que consume el TTS ni romper `ReaderTextAligner`.
4. **Atajos de teclado:**
   - `ArrowLeft` / `ArrowRight` para pasar página, `Space` para Play/Pausa (cuando hay audio y el foco no está en un input), `Escape` para cerrar paneles laterales (TOC, Apariencia, Marcadores).

---

## Decisiones que quedan a criterio del implementador

- Estrategia de migraciones de esquema en SQLite (script versionado en `PRAGMA user_version` o archivos SQL embebidos).
- Diseño visual detallado de componentes React/Tailwind (colores, espaciado, iconos) respetando el estilo limpio de escritorio ya presente en `ClientApp`.
- Implementación interna de caché para `POST /api/tts/sample` (LRU en memoria vs carpeta temporal acotada) y reutilización de la instancia de `PiperSherpaTtsEngine` cuando el modelo coincide.
- Mecanismo exacto de reproducción de capítulos M4B en `ReaderEndpoints` / `useAudioSync` (single-file M4B con offsets de capítulo vs segmentos servidos por capítulo).
- Política de limpieza de `UploadedBookStore` (retirarlo una vez que el flujo principal pase por `POST /api/library/import` o mantenerlo solo si algún endpoint legado lo necesita).
