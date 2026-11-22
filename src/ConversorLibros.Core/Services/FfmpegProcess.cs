using System.ComponentModel;
using System.Diagnostics;

namespace ConversorLibros.Core.Services;

/// <summary>Thrown when the ffmpeg executable cannot be started.</summary>
public sealed class FfmpegNotFoundException : InvalidOperationException
{
    public FfmpegNotFoundException(string ffmpegPath, Exception inner)
        : base($"No se pudo ejecutar ffmpeg en '{ffmpegPath}'.", inner)
    {
    }
}

/// <summary>Runs ffmpeg with an argument list (no shell quoting) and reports failures with stderr.</summary>
public static class FfmpegProcess
{
    public static async Task RunAsync(string ffmpegPath, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo(ffmpegPath)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        Process process;
        try
        {
            process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start ffmpeg.");
        }
        catch (Win32Exception ex)
        {
            throw new FfmpegNotFoundException(ffmpegPath, ex);
        }

        using (process)
        {
            // Drain both pipes while ffmpeg runs; reading only after exit deadlocks once the stderr buffer fills.
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            await stdoutTask.ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"ffmpeg failed with exit code {process.ExitCode}: {stderr}");
            }
        }
    }
}
