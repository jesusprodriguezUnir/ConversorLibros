using System.Buffers.Binary;
using ConversorLibros.Core.Abstractions;
using SherpaOnnx;

namespace ConversorLibros.Core.Services;

/// <summary>
/// Offline TTS engine backed by sherpa-onnx VITS/Piper models.
/// </summary>
public sealed class PiperSherpaTtsEngine : ITtsEngine, IDisposable
{
    private readonly OfflineTts _tts;

    public string ModelVersion { get; }

    public PiperSherpaTtsEngine(string modelOnnxPath, string tokensPath, string dataDir, string modelVersion = "piper-v1")
    {
        if (!File.Exists(modelOnnxPath))
        {
            throw new FileNotFoundException("Piper ONNX model not found.", modelOnnxPath);
        }

        if (!File.Exists(tokensPath))
        {
            throw new FileNotFoundException("Piper tokens.txt not found.", tokensPath);
        }

        if (!Directory.Exists(dataDir))
        {
            throw new DirectoryNotFoundException($"espeak-ng data directory not found: '{dataDir}'.");
        }

        ModelVersion = modelVersion;

        var config = new OfflineTtsConfig();
        config.Model.Vits.Model = modelOnnxPath;
        config.Model.Vits.Tokens = tokensPath;
        config.Model.Vits.DataDir = dataDir;
        config.Model.NumThreads = Math.Max(1, Environment.ProcessorCount / 2);
        config.Model.Provider = "cpu";

        _tts = new OfflineTts(config);
    }

    public Task<byte[]> SynthesizeWavAsync(string text, string voiceId, float speed, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var speakerId = int.TryParse(voiceId, out var sid) ? sid : 0;
        var audio = _tts.Generate(text, speed, speakerId);
        if (audio.Samples.Length == 0)
        {
            throw new InvalidOperationException("sherpa-onnx generated no audio samples for the given text.");
        }

        var wavBytes = EncodePcm16Wav(audio.Samples, audio.SampleRate);

        return Task.FromResult(wavBytes);
    }

    private static byte[] EncodePcm16Wav(float[] samples, int sampleRate)
    {
        var dataBytes = samples.Length * 2;
        var wav = new byte[44 + dataBytes];

        "RIFF"u8.CopyTo(wav.AsSpan(0, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(4, 4), (uint)(36 + dataBytes));
        "WAVEfmt "u8.CopyTo(wav.AsSpan(8, 8));
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(16, 4), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(wav.AsSpan(20, 2), 1); // PCM
        BinaryPrimitives.WriteUInt16LittleEndian(wav.AsSpan(22, 2), 1); // Mono
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(24, 4), (uint)sampleRate);
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(28, 4), (uint)(sampleRate * 2));
        BinaryPrimitives.WriteUInt16LittleEndian(wav.AsSpan(32, 2), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(wav.AsSpan(34, 2), 16);
        "data"u8.CopyTo(wav.AsSpan(36, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(40, 4), (uint)dataBytes);

        for (var i = 0; i < samples.Length; i++)
        {
            var clamped = Math.Clamp(samples[i], -1.0f, 1.0f);
            var pcm = (short)(clamped * short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(44 + i * 2, 2), pcm);
        }

        return wav;
    }

    public void Dispose() => _tts.Dispose();
}
