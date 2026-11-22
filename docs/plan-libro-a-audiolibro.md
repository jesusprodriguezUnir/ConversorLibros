# Plan: de libro a audiolibro (ConversorLibros)

_Uso personal, 100% gratis y offline. Arquitectura .NET reutilizable (`ConversorLibros.Core`) → CLI en PC → App Android (.NET MAUI)._

---

## Fase 0 — Validar la voz y G2P (sin código propio)
Objetivo: confirmar qué motor TTS suena mejor en español (`es-ES`) y si funciona sin dependencias nativas problemáticas.

1. Probar en el móvil **Kokoro** (`kokoro-multi-lang-v1_0` vía sherpa-onnx / VoxSherpa) vs **Piper es_ES** (SherpaTTS / sherpa-onnx) con un mismo capítulo.
2. Evaluar: inteligibilidad tras 30 min, números/abreviaturas, prosodia en diálogos, artefactos.
3. Verificar si `MisakiSharp` soporta G2P en `es-ES` sin `espeak-ng`; si no, priorizar **Piper (`sherpa-onnx`)** o G2P propio en C#.

---

## Fase 1 — `ConversorLibros.Core` + Tests
Objetivo: núcleo .NET reutilizable, testeado y preparado para streaming y cancelación.

1. **Higiene y estructura**:
   - `.gitignore` (dotnet, modelos `.onnx`, audios `.wav`/`.m4b`).
   - Solución con `ConversorLibros.Core` y `ConversorLibros.Core.Tests` (xUnit).
2. **Modelo de dominio**:
   - `Book` → `Chapter` → `TextChunk` (`Id`, `Hash`, `Order`).
3. **Pipeline** (todo `async` con `CancellationToken` e `IProgress<T>`):
   - `IBookSource`: lectura de EPUB sin DRM (`VersOne.Epub` + `AngleSharp`) respetando `spine` + `TOC` y filtrando notas, créditos e índices.
   - `ITextNormalizer`: reglas `es-ES` (números, ordinales, romanos, abreviaturas, rayas de diálogo `—`, siglas) + diccionario de pronunciación por libro. **100% cubierto con unit tests.**
   - `IChunker`: división por oración/párrafo bajo el límite de tokens del motor TTS, con pausas configurables.
   - `ITtsEngine`: interfaz única con implementación para el motor elegido en Fase 0.
   - `IChunkCache`: caché en disco por `hash(texto normalizado + voz + velocidad + modelo) → .wav`. Reanudable y paralelizable.
   - `IChapterAssembler`: concatena chunks **por capítulo** haciendo streaming a disco (`FileStream`, sin `MemoryStream`).

---

## Fase 2 — CLI en PC (`ConversorLibros.Cli`)
Objetivo: herramienta rápida en PC para validar libros reales y generar M4B.

1. `System.CommandLine` con opciones:
   - `--dry-run`: vuelca el texto normalizado y chunkeado para revisar errores de lectura sin sintetizar audio.
   - `--chapters 1-3` y `--resume`: iteración rápida y reanudación.
2. **`M4bFfmpegPackager`** (solo PC/CLI): empaqueta capítulos, metadatos y portada del EPUB en un `.m4b` (AAC mono 48–64 kbps) usando `ffmpeg`.

---

## Fase 3 — App Android (`.NET MAUI`)
Objetivo: generar y escuchar audiolibros 100% offline en el móvil reutilizando `ConversorLibros.Core`.

1. **Sin `ffmpeg` en Android**: reproducción directa por capítulo desde los archivos cacheados (o `MediaMuxer` nativo si se exporta).
2. **Modelos bajo demanda**: descarga del modelo TTS elegido al almacenamiento privado de la app (nunca dentro del APK).
3. **Generación en segundo plano**: `ForegroundService` (Android 14+) procesando capítulo a capítulo con `IChunkCache`.
4. **Reproducción inmediata**: reproducir el Capítulo 1 con `Media3` / `MediaSession` (pantalla de bloqueo, auriculares, Android Auto) mientras se siguen generando los siguientes.

---

## Fuera del MVP
- Traducción automática (si hace falta, paso previo aparte con LLM local).
- Soporte de PDF/TXT (solo EPUB sin DRM en el MVP).
