# Guía de uso

Cómo instalar lo necesario, abrir la aplicación y convertir un EPUB en un audiolibro para escucharlo en el móvil.

## 1. Requisitos (una sola vez)

| Qué | Para qué | Cómo comprobarlo |
|---|---|---|
| Windows 10/11 x64 | La app y la CLI solo funcionan en Windows | — |
| .NET 10 SDK | Compilar y ejecutar | `dotnet --version` → `10.x` |
| Modelo de voz Piper | Generar la voz en español | Carpeta `models\vits-piper-es_ES-davefx-medium\` con `.onnx`, `tokens.txt` y `espeak-ng-data\` |
| ffmpeg | Comprimir el audio a Opus/M4B | `ffmpeg -version` |
| Ollama + `qwen3:8b` | Solo si quieres **traducir** libros que no estén en español | `ollama list` |

### Modelo de voz

Si la carpeta `models\vits-piper-es_ES-davefx-medium\` no existe, descarga y descomprime el modelo dentro de `models\`:

```
https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models/vits-piper-es_ES-davefx-medium.tar.bz2
```

### ffmpeg

```powershell
winget install --id Gyan.FFmpeg -e
```

Si la consola dice que `winget` no se reconoce, llámalo con su ruta completa:

```powershell
& "$env:LOCALAPPDATA\Microsoft\WindowsApps\winget.exe" install --id Gyan.FFmpeg -e
```

No hace falta que ffmpeg esté en el `PATH`: la app y la CLI lo buscan también en la carpeta de paquetes de WinGet.

### Ollama (opcional, solo para traducir)

Instala Ollama desde su web oficial, ábrelo y descarga el modelo:

```powershell
ollama pull qwen3:8b
```

## 2. Abrir la aplicación

Desde la raíz del repositorio (`D:\Personal\ConversorLibros`):

```powershell
dotnet build src/ConversorLibros.App -f net10.0-windows10.0.19041.0
.\src\ConversorLibros.App\bin\Debug\net10.0-windows10.0.19041.0\win-x64\ConversorLibros.App.exe
```

Una vez compilada, puedes abrir directamente ese `.exe` (o crearle un acceso directo). Solo hay que volver a compilar si cambia el código.

## 3. Primera configuración: pantalla **Ajustes**

1. **Carpeta del modelo Piper** → `Examinar…` → elige `D:\Personal\ConversorLibros\models\vits-piper-es_ES-davefx-medium`.
2. **Carpeta de salida** → elige dónde guardar los audiolibros, por ejemplo `D:\Audiolibros`. Ahí también se guarda la caché, así que no la borres mientras un libro esté a medias.
3. **ffmpeg** → déjalo vacío (detección automática). Solo escribe una ruta si no lo encuentra.
4. **Ollama** (solo si vas a traducir):
   - Endpoint: `http://localhost:11434`
   - Modelo: `qwen3:8b`
   - Pulsa **Probar conexión** para comprobarlo.
5. Pulsa **Guardar**.

## 4. Convertir un libro: pantalla **Nueva conversión**

1. **Elegir EPUB…** y selecciona el libro (sin DRM). Verás el título, el autor y la lista de capítulos.
2. **Capítulos**: marca los que quieras. Para una primera prueba, elige solo uno.
3. **Traducir de**: márcalo solo si el libro no está en español y escribe el idioma de origen (`en` para inglés). El destino siempre es español.
4. **Velocidad**: `1.00x` es la velocidad normal. Ten en cuenta que se queda grabada en el audio; los reproductores del móvil también permiten acelerar al escuchar.
5. **Formato**:
   - **Opus** (recomendado): una carpeta con un fichero por capítulo y la portada. Ocupa unos 7–8 MB por hora.
   - **M4B**: un único fichero con marcas de capítulo. Ocupa unas 4 veces más.
6. Pulsa **Empezar**. La barra muestra la etapa (traducción, síntesis, ensamblado, exportación) y el tiempo estimado. **Cancelar** detiene la conversión sin perder lo ya hecho.

### Cuánto tarda

- Síntesis de voz: unos pocos minutos por capítulo.
- Traducción con `qwen3:8b`: es lo lento, unos 15 minutos por capítulo largo. Un libro entero puede llevar varias horas.
- **Puedes cortar y retomar cuando quieras**: todo lo traducido y sintetizado se guarda en caché. Si vuelves a lanzar el mismo libro con los mismos ajustes, continúa donde lo dejó.

## 5. Ver lo convertido: pantalla **Biblioteca**

Muestra los audiolibros de la carpeta de salida, con su portada. **Abrir carpeta** abre el libro en el Explorador. Si no aparece uno recién terminado, pulsa **Actualizar**.

## 6. Escucharlo en el móvil (Android)

1. Instala un reproductor de audiolibros, por ejemplo **Smart AudioBook Player** o **Voice**.
2. Conecta el móvil por USB y copia **la carpeta entera del libro** (la de los `.opus` y `cover.jpg`) a una carpeta del móvil, por ejemplo `Audiobooks/`.
3. En el reproductor, indica `Audiobooks/` como carpeta raíz. Cada subcarpeta aparecerá como un libro y cada fichero como un capítulo. El reproductor recuerda por dónde vas y permite cambiar la velocidad.

## 7. Alternativa: la línea de comandos (CLI)

Hace lo mismo que la app y viene bien para lanzar libros largos o automatizar.

```powershell
# Ver el texto que se leerá, sin generar audio (rápido; útil para revisar la traducción)
dotnet run --project src/ConversorLibros.Cli -- convert "C:\Libros\libro.epub" --dry-run --chapters 1-3 --out .\out

# Convertir un libro en español
dotnet run --project src/ConversorLibros.Cli -- convert "C:\Libros\libro.epub" --model-dir models\vits-piper-es_ES-davefx-medium --out .\out

# Traducir del inglés y convertir los capítulos 1 a 5
dotnet run --project src/ConversorLibros.Cli -- convert "C:\Libros\book.epub" --translate-from en --model-dir models\vits-piper-es_ES-davefx-medium --chapters 1-5 --out .\out
```

| Opción | Significado |
|---|---|
| `--model-dir <carpeta>` | Carpeta del modelo Piper (obligatoria salvo con `--dry-run`) |
| `--out <carpeta>` | Carpeta de salida y caché (por defecto `.\out`) |
| `--chapters <rango>` | Capítulos a convertir: `5` o `1-3` |
| `--speed <n>` | Velocidad (por defecto `1.0`) |
| `--format <opus\|m4b>` | Formato de salida (por defecto `opus`) |
| `--delete-wav` | Borra los WAV intermedios al terminar (la caché se conserva) |
| `--translate-from <idioma>` | Activa la traducción desde ese idioma, p. ej. `en` |
| `--translate-to <idioma>` | Idioma de destino (por defecto `es`) |
| `--ollama-url <url>` | Dirección de Ollama (por defecto `http://localhost:11434`) |
| `--ollama-model <modelo>` | Modelo de Ollama (por defecto `qwen3:8b`) |
| `--dry-run` | Genera `dry-run.txt` con el texto que se leería, sin audio |

## 8. Problemas frecuentes

| Síntoma | Causa y solución |
|---|---|
| "no se encontró ffmpeg" | No está instalado → apartado 1. Si está instalado en otra ruta, indícala en Ajustes. Mientras tanto, los WAV de cada capítulo sí se generan. |
| `winget` no se reconoce | Usa la ruta completa del apartado 1. |
| "Probar conexión" falla | Ollama no está abierto. Ábrelo (icono en la bandeja) y vuelve a probar. |
| La traducción deja frases en inglés | Pasa a veces con el modelo local. Revisa el texto con `--dry-run` antes de generar el audio. |
| El EPUB no abre | Comprueba que no tenga DRM. |
| La carpeta de salida ocupa mucho | Los WAV intermedios (`chapter_NNN.wav`) y `cache\` se pueden borrar cuando el libro esté terminado y copiado. Si los borras, un nuevo intento empieza de cero. |
