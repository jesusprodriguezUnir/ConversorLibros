using ConversorLibros.Web.Models;

namespace ConversorLibros.Web.Services;

/// <summary>Outcome of resolving the Piper model folder used for synthesis.</summary>
public sealed record PiperModelResolution(string EffectiveDir, bool IsAutoDetected, PiperValidationStatus Status);

/// <summary>
/// Picks the Piper model folder to synthesize with. The configured folder wins when valid; otherwise the first
/// valid <c>models/*</c> child is used, probed in this order: ancestors of the app base directory (nearest first),
/// ancestors of the content root (nearest first), then <c>%LOCALAPPDATA%\ConversorLibros\models\*</c>.
/// The detected folder is never written to settings.
/// </summary>
public sealed class PiperModelLocator
{
    private const string ModelsFolderName = "models";

    private readonly IReadOnlyList<string> _anchors;

    public PiperModelLocator(string? baseDirectory, string? contentRoot, string? localAppData)
    {
        var anchors = new List<string>();
        AddAncestors(anchors, baseDirectory);
        AddAncestors(anchors, contentRoot);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            anchors.Add(Path.Combine(localAppData, "ConversorLibros"));
        }

        _anchors = anchors.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public PiperModelResolution Resolve(string? configuredDir)
    {
        var configured = JsonSettingsStore.NormalizeModelDir((configuredDir ?? string.Empty).Trim());
        var configuredStatus = JsonSettingsStore.ValidatePiper(configured);
        if (configuredStatus.IsValid)
        {
            return new PiperModelResolution(configured, IsAutoDetected: false, configuredStatus);
        }

        foreach (var candidate in EnumerateCandidates())
        {
            var status = JsonSettingsStore.ValidatePiper(candidate);
            if (status.IsValid)
            {
                return new PiperModelResolution(candidate, IsAutoDetected: true, status);
            }
        }

        return new PiperModelResolution(configured, IsAutoDetected: false, configuredStatus);
    }

    private IEnumerable<string> EnumerateCandidates()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var anchor in _anchors)
        {
            var modelsRoot = Path.Combine(anchor, ModelsFolderName);
            if (!Directory.Exists(modelsRoot))
            {
                continue;
            }

            foreach (var child in Directory.EnumerateDirectories(modelsRoot).OrderBy(d => d, StringComparer.Ordinal))
            {
                if (seen.Add(child))
                {
                    yield return child;
                }
            }
        }
    }

    private static void AddAncestors(List<string> anchors, string? start)
    {
        if (string.IsNullOrWhiteSpace(start))
        {
            return;
        }

        for (var dir = Path.GetFullPath(start); dir is not null; dir = Path.GetDirectoryName(dir))
        {
            anchors.Add(dir);
        }
    }
}
