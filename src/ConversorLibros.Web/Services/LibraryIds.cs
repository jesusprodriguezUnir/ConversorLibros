using System.Security.Cryptography;
using System.Text;

namespace ConversorLibros.Web.Services;

/// <summary>Stable library ids: URL-safe prefix of the SHA-256 of the lowercased full path of a book.</summary>
public static class LibraryIds
{
    public static string Compute(string fullPath)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(fullPath.ToLowerInvariant()));
        return Convert.ToBase64String(hash)[..16]
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
