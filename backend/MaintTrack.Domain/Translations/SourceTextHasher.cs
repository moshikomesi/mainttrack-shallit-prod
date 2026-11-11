using System.Security.Cryptography;
using System.Text;

namespace MaintTrack.Domain.Translations;

/// <summary>
/// Deterministic source-version identifier. The same Unicode text always produces the same hash.
/// </summary>
public static class SourceTextHasher
{
    public static string Compute(string sourceText)
    {
        ArgumentNullException.ThrowIfNull(sourceText);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sourceText));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
