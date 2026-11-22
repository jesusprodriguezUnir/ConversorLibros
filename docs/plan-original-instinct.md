# Plan original (referencia) — De libro a audiolibro

_Plan recibido el 21-nov-2026, guardado como referencia. El plan vigente es `plan-libro-a-audiolibro.md`._

Pipeline en el PC con Windows y escucha en Android. Todo gratis y en local.

## Fase 0 — Preparar el PC
- Python 3 (python.org, marcar «Add python.exe to PATH»).
- Entorno virtual: `python -m venv voz` → `voz\Scripts\activate.bat` (PowerShell: `voz\Scripts\Activate.ps1`).
- ffmpeg: `winget install "FFmpeg (Essentials Build)"` o build de gyan.dev.
- Solo libros propios sin DRM.

## Fase 1 — Sacar el texto
- Calibre: `ebook-convert libro.epub libro.txt`.
- Script Python para dividir en un archivo por capítulo.
- Alternativa: ebooklib para leer el EPUB directamente.

## Fase 2 — Traducir (opcional)
- `pip install argostranslate`; `argospm update`; `argospm install translate-en_es`.
- Prueba: `argos-translate --from-lang en --to-lang es "Hello World"`.
- Calidad inferior a Google Translate/DeepL.

## Fase 3 — La voz
- Elección: Kokoro-82M (Apache 2.0). Alternativa: Piper (`pip install piper-tts`, voces es_ES, GPL).
- `pip install "kokoro>=0.9.4" soundfile`.
- espeak-ng (.msi). Si da «espeak not installed»: `PHONEMIZER_ESPEAK_LIBRARY=C:\Program Files\eSpeak NG\libespeak-ng.dll`.
- `lang_code='e'`; voces `ef_dora`, `em_alex`, `em_santa`.
- Script por capítulo; Kokoro devuelve fragmentos WAV a 24 kHz, que se concatenan.
- CPU sin GPU; ~80.000 palabras ≈ 9–10 h de audio; tiempo de generación sin verificar.

## Fase 4 — Un MP3 por capítulo
- `ffmpeg -i entrada.wav -codec:a libmp3lame capitulo.mp3`; nombres `01.mp3`, `02.mp3`…

## Fase 5 — Escuchar en Android
- Copiar por USB o nube; Spotify (archivos locales) o una app de audiolibros que recuerde la posición.
- Spotify no permite publicarlos como podcast; solo uso local.

Coste: 0 €. Fuentes consultadas el 21-nov-2026; nada probado.
