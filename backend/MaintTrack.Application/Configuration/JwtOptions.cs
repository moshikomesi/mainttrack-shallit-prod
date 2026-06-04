namespace MaintTrack.Application.Configuration;

/// <summary>
/// Strongly-typed JWT configuration options.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; init; } = string.Empty;

    public string Audience { get; init; } = string.Empty;

    /// <summary>
    /// Symmetric signing key used for token generation and validation.
    /// </summary>
    public string Key { get; init; } = string.Empty;
}
