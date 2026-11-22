# Diseño: Nueva interfaz web en React para ConversorLibros (Fase 1)

## Intención y contexto

Reemplazar la experiencia visual básica de la aplicación actual en .NET MAUI (`ConversorLibros.App`) por una interfaz web local moderna y profesional construida con **React + TypeScript**, respaldada por un host local ligero en **ASP.NET Core** que reutiliza `ConversorLibros.Core` sin duplicar lógica del pipeline.

El proyecto se desarrollará por fases. Esta **Fase 1** establece una arquitectura limpia, modular y extensible que cubre el 100 % de las capacidades actuales (Biblioteca, Nueva conversión y Ajustes) con un salto cualitativo en diseño visual, aprovechamiento del espacio de escritorio y retroalimentación en tiempo real.

## Objetivos, no-objetivos y anti-objetivos

### Objetivos (Fase 1)
- Paridad funcional completa con `ConversorLibros.App` en las tres vistas principales: **Biblioteca**, **Nueva conversión** y **Ajustes**.
- Interfaz de escritorio profesional en React + TypeScript + Tailwind CSS:
  - Navegación lateral persistente con indicador de estado si hay una conversión en curso.
  - **Biblioteca:** grilla responsiva de portadas, búsqueda por título/autor, filtro por formato (`Opus` / `M4B`), metadatos legibles (capítulos, duración) y apertura directa en el Explorador de Windows.
  - **Nueva conversión:** layout de dos columnas en escritorio con zona de arrastrar y soltar (drag & drop) de `.epub`, vista previa de portada y metadatos, lista de capítulos con filtro y selección rápida (todos/ninguno), controles claros de velocidad, traducción y formato, y panel de progreso en tiempo real (etapa, porcentaje, capítulo, tiempo transcurrido/estimado y registro colapsable).
  - **Ajustes:** diagnóstico visual del estado de Piper, `ffmpeg`, carpeta de salida y Ollama; explorador de carpetas integrado; prueba de conexión a Ollama con selección de modelos detectados.
- Arquitectura modular por *features* en el frontend y API REST + SSE en el backend para que las fases siguientes (reproductor integrado, cola de conversiones, diccionario de pronunciación, etc.) se agreguen como módulos independientes.

### No-objetivos (Fase 1)
- Reproductor de audio integrado en el navegador (queda para una fase posterior).
- Cola de múltiples libros o conversiones simultáneas (en Fase 1 se permite una conversión activa a la vez).
- Editor de diccionario de pronunciación por libro.
- Autenticación, multiusuario o despliegue en la nube (uso personal y local en Windows).
- Eliminación del proyecto `ConversorLibros.App` (MAUI se conserva intacto como respaldo hasta que el usuario decida retirarlo).

### Anti-objetivos
- Duplicar lógica de normalización, chunking, traducción, TTS o empaquetado fuera de `ConversorLibros.Core`.
- Romper invariantes de caché/hashes (`TextChunk.Hash`, `OllamaTranslator.PromptVersion`) o los tests existentes en `ConversorLibros.Core.Tests`.
- Introducir dependencias de servicios externos en la nube (la aplicación debe operar 100 % offline).
- Construir una UI de formulario plano de una sola columna que desaproveche pantallas de escritorio.

## Restricciones

- **Plataforma:** Windows (x64), uso personal local, .NET 10 (`net10.0`).
- **Dependencia fijada:** `org.k2fsa.sherpa.onnx.runtime.win-x64` debe mantenerse en la versión **1.13.8** (en sincronía con `ConversorLibros.Core`), ya que `sherpa-onnx 2.x` produce audio silencioso con los modelos Piper actuales.
- **Convenciones del repositorio:** interfaz de usuario y documentación en español; código, identificadores y comentarios en inglés.
- **Ejecución síncrona de Piper:** la síntesis TTS es síncrona, por lo que el backend debe ejecutar el pipeline dentro de `Task.Run` para no bloquear el servidor HTTP.
- **Llamadas a procesos externos:** cualquier invocación a `explorer.exe` o `ffmpeg` debe usar `ProcessStartInfo.ArgumentList`, nunca cadenas concatenadas.
- **Manejo de archivos EPUB desde navegador:** el navegador no expone la ruta física de un archivo seleccionado/arrastrado. El backend recibirá el archivo por `multipart/form-data`, lo almacenará en un directorio temporal gestionado (`%TEMP%\ConversorLibros\uploads\`), lo analizará con `EpubBookSource` y devolverá un `uploadId` para referenciarlo al iniciar la conversión.
- **Selección de carpetas desde navegador:** el navegador tampoco accede a `FolderPicker` de WinRT. Para evitar acoplar WinForms/WinRT al host web, los campos de ruta admitirán edición directa con validación instantánea en servidor y un botón **Examinar** que abre un modal explorador de carpetas respaldado por un endpoint local de solo lectura (`GET /api/fs/browse`).

## Enfoques considerados

### Enfoque 1: ASP.NET Core Minimal API (`ConversorLibros.Web`) + React/Vite/TypeScript SPA *(Elegido)*
- Un proyecto `Microsoft.NET.Sdk.Web` en `src/ConversorLibros.Web` que referencia `ConversorLibros.Core`, expone REST + Server-Sent Events (SSE) y sirve el build estático de React desde `wwwroot/`. En desarrollo, Vite corre con hot-reload haciendo proxy de `/api` hacia el backend.
- **Ventajas:** iteración rápida por fases, un solo proceso/puerto en producción, reutilización total del Core, cero dependencias de empaquetado de escritorio.
- **Tradeoffs:** corre en una pestaña del navegador local (`localhost`) en lugar de una ventana nativa de Windows (aunque el usuario puede instalarla como app desde Edge/Chrome si lo desea).

### Enfoque 2: Mismo backend .NET + ventana nativa WebView2 / Photino *(Descartado)*
- Envuelve la misma app React dentro de una ventana de escritorio propia en Windows.
- **Ventajas:** ventana propia en la barra de tareas y acceso a diálogos nativos de sistema.
- **Tradeoffs:** agrega una capa de host nativo y empaquetado más pesado, ralentizando el ciclo de desarrollo en fases incrementales. Si en el futuro se quisiera envolver en ventana nativa, el código React y la API del Enfoque 1 se reutilizan sin cambios.

**Decisión:** el usuario eligió el **Enfoque 1**.

## Arquitectura técnica

### 1. Backend (`src/ConversorLibros.Web`)

Nuevo proyecto `ConversorLibros.Web.csproj` (`net10.0`, `Microsoft.NET.Sdk.Web`), incluido en `ConversorLibros.sln`.

#### Servicios internos
- **`JsonSettingsStore`**: lee y escribe `%LocalAppData%\ConversorLibros\settings.json`. Mantiene los mismos valores por defecto que `AppSettings`:
  - `PiperModelDir`: `""`
  - `OutputRoot`: `<Mis Documentos>\ConversorLibros`
  - `OllamaUrl`: `http://localhost:11434`
  - `OllamaModel`: `qwen3:8b`
  - `FfmpegOverride`: `""`
  - Incluye la misma normalización de `PiperModelDir` (si el usuario selecciona una subcarpeta como `espeak-ng-data`, resuelve la carpeta padre si contiene un `.onnx`) y la validación detallada (`.onnx`, `tokens.txt`, `espeak-ng-data`).
- **`UploadedBookStore`**: almacena temporalmente los EPUB subidos junto con el `Book` parseado en memoria (indexado por `uploadId` GUID), sirve su portada embebida y limpia archivos antiguos al recibir uno nuevo o al finalizar el servidor.
- **`ConversionJobManager`**: singleton que coordina una conversión activa a la vez:
  - Ejecuta `BookConversionPipeline` + `AudiobookChapterResolver` + `OpusFolderExporter`/`M4bExporter` dentro de `Task.Run` con `CancellationTokenSource`.
  - Traduce `ConversionProgress` a porcentaje global, etapa localizada, capítulo actual, tiempo transcurrido y tiempo restante estimado.
  - Mantiene el estado actual y el historial de entradas de registro en memoria (para que recargar la página o cambiar de vista no pierda el progreso).
  - Emite eventos en vivo a todos los suscriptores SSE activos mediante canales acotados (`Channel<ConversionEvent>`).

#### Contrato HTTP (`/api`)

| Método y ruta | Propósito |
|---|---|
| `GET /api/library` | Escanea `OutputRoot` con `AudiobookLibrary.Scan` y devuelve la lista de audiolibros con identificador estable, título, autor, formato, capítulos, duración y `coverUrl` opcional. |
| `GET /api/library/{id}/cover` | Devuelve la imagen `cover.jpg` o `cover.png` del audiolibro. |
| `POST /api/library/{id}/open` | Abre la carpeta o archivo `.m4b` en `explorer.exe` usando `ProcessStartInfo.ArgumentList`. |
| `POST /api/books/inspect` | Recibe `multipart/form-data` con un `.epub`, lo parsea con `EpubBookSource` y devuelve `uploadId`, `title`, `author`, `language`, `hasCover`, `coverUrl` y `chapters` (`index`, `title`, `charCount`). |
| `GET /api/books/{uploadId}/cover` | Devuelve la portada embebida del EPUB inspeccionado. |
| `POST /api/conversions` | Inicia la conversión (`uploadId`, `chapterIndexes`, `speed`, `translate`, `sourceLanguage`, `format`). Devuelve `202 Accepted` o `409 Conflict` si ya hay una conversión en curso. |
| `GET /api/conversions/current` | Devuelve el estado completo del trabajo actual (inactivo, ejecutando, completado, cancelado o error), progreso y registro acumulado. |
| `GET /api/conversions/events` | Stream `text/event-stream` (SSE) con eventos en vivo: `snapshot`, `progress`, `log`, `completed`, `cancelled`, `error`. |
| `POST /api/conversions/cancel` | Solicita la cancelación de la conversión activa. |
| `GET /api/settings` | Devuelve los ajustes actuales más el estado de salud de Piper (`isValid`, `message`) y `ffmpeg` (`found`, `resolvedPath`, `message`). |
| `PUT /api/settings` | Normaliza, guarda y devuelve los ajustes actualizados con su estado de validación. |
| `POST /api/settings/test-ollama` | Recibe `url` y `model`, consulta `{url}/api/tags` con timeout de 10 s y devuelve `connected`, `modelInstalled`, lista de `models` disponibles y `message`. |
| `GET /api/fs/browse?path=...` | Sin `path`, devuelve las unidades lógicas de Windows (`C:\`, `D:\`, etc.). Con `path`, devuelve la ruta actual, la ruta padre y sus subcarpetas accesibles. |

#### Ejecución y servido estático
- Escucha en `http://localhost:5180` por defecto (configurable por `ASPNETCORE_URLS`).
- Sirve el frontend compilado desde `wwwroot/` con fallback SPA a `index.html` para rutas que no empiecen por `/api`.
- En modo desarrollo, Vite corre en `http://localhost:5173` con proxy de `/api` hacia `http://localhost:5180`.

### 2. Frontend (`src/ConversorLibros.Web/ClientApp`)

#### Stack
- **React 19** + **TypeScript** + **Vite**
- **Tailwind CSS v4**
- **lucide-react** para iconografía consistente
- **react-router-dom** para navegación (`/biblioteca`, `/convertir`, `/ajustes`)

#### Estructura de carpetas
```
ClientApp/
  src/
    app/
      App.tsx
      AppShell.tsx          # Barra lateral fija + indicador de trabajo activo + outlet
      ConversionContext.tsx # Estado global de conversión + suscripción SSE única
    features/
      library/              # LibraryPage, BookCard, filtros y estado vacío
      conversion/           # NewConversionPage, EpubDropzone, ChapterList, ConversionOptions, ProgressPanel
      settings/             # SettingsPage, HealthBadge, FolderBrowserModal
    shared/
      api/                  # Cliente HTTP tipado y tipos compartidos
      components/           # Primitivas UI reutilizables (Button, Card, Badge, Modal, Toast)
      utils/                # Formateo de duración, tamaños y textos
```

#### Dirección visual y experiencia de usuario
- **Estética "Dark Studio" editorial:** fondo profundo (`#0B0D12`), superficies elevadas en capas (`#131720` y `#1B2130`), bordes sutiles de 1 px (`#252C3D`) y acento cálido ámbar (`#F59E0B` / `#FBBF24`) para acciones primarias y barras de progreso, combinado con semántica clara para estados (verde esmeralda para listo/completado, ámbar para advertencias, rojo coral para errores).
- **Tipografía:** fuente sans limpia para interfaz (`Inter` / sistema) y monoespaciada (`JetBrains Mono` / Consolas) para rutas, tiempos, porcentajes y consola de registro.
- **Navegación lateral persistente:** logo/nombre de la app, enlaces a **Biblioteca**, **Nueva conversión** y **Ajustes**, y un mini-widget en la parte inferior de la barra lateral que muestra el progreso en vivo si hay una conversión corriendo mientras el usuario navega por otras secciones.
- **Biblioteca:**
  - Encabezado con contador de audiolibros, buscador instantáneo por título/autor, selector de filtro (`Todos`, `Opus`, `M4B`) y botón **Actualizar**.
  - Grilla responsiva de tarjetas con portada (o portada generada elegante con iniciales/título cuando no hay imagen), badge de formato, conteo de capítulos, duración total y botón **Abrir carpeta**.
  - Estado vacío claro con botón directo a **Nueva conversión**.
- **Nueva conversión:**
  - Layout de dos columnas en pantallas anchas (`lg:grid-cols-12`):
    - **Columna principal (7 cols):** zona drag & drop de `.epub` (que al cargar se transforma en tarjeta compacta con portada, título, autor, idioma detectado y botón "Cambiar libro") + lista de capítulos con buscador, botones **Todos / Ninguno**, contador de seleccionados y longitud aproximada de cada capítulo.
    - **Columna lateral (5 cols):** tarjeta de opciones (velocidad con presets rápidos `0.85x`, `1.0x`, `1.15x`, `1.25x` y slider fino `0.5x–1.5x`; toggle de traducción con código de idioma origen → `es`; selector visual de formato Opus vs M4B con descripción) + tarjeta de ejecución y progreso (barra animada, etapa actual, capítulo `X/Y`, tiempo transcurrido y estimado, botones **Empezar** / **Cancelar**, y consola de registro desplegable).
  - Si faltan rutas críticas (como el modelo Piper), muestra un aviso directo con enlace a **Ajustes** antes de iniciar.
- **Ajustes:**
  - Tarjetas separadas por dominio: **Motor de voz (Piper)**, **Carpeta de salida y ffmpeg** y **Traducción local (Ollama)**.
  - Cada tarjeta incluye un badge de estado (`Listo` / `Revisar` / `No encontrado`) y botón **Examinar** que abre `FolderBrowserModal`.
  - En Ollama, **Probar conexión** muestra el resultado y, si conecta, permite elegir directamente cualquiera de los modelos instalados devueltos por `/api/tags`.

## Decisiones que quedan a criterio del implementador

- Detalles finos de espaciado, transiciones CSS y animaciones de carga.
- Estrategia exacta de limpieza de archivos temporales en `UploadedBookStore` (por ejemplo, limpieza al iniciar/detener la app y límite de archivos recientes).
- Script de conveniencia para desarrollo local (por ejemplo, script npm o PowerShell para levantar backend + Vite).
- Pruebas de humo e integración ligera para los endpoints de `ConversorLibros.Web` (`WebApplicationFactory` o pruebas unitarias de `JsonSettingsStore` y `ConversionJobManager`).
