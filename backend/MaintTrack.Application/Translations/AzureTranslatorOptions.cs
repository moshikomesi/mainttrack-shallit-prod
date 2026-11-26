namespace MaintTrack.Application.Translations;

public sealed class AzureTranslatorOptions
{
    public const string SectionName = "Translation:Azure";
    public const string DefaultEndpoint = "https://api.cognitive.microsofttranslator.com";

    public string Endpoint { get; set; } = DefaultEndpoint;

    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Optional Azure resource region. Leave empty for a Global Translator resource.
    /// </summary>
    public string? Region { get; set; }

    public int TimeoutSeconds { get; set; } = 15;

    public int DetectionTimeoutSeconds { get; set; } = 5;

    public TimeSpan Timeout =>
        TimeSpan.FromSeconds(Math.Clamp(TimeoutSeconds, 1, 120));

    public TimeSpan DetectionTimeout =>
        TimeSpan.FromSeconds(Math.Clamp(DetectionTimeoutSeconds, 1, 30));

    public bool HasApiKey => !string.IsNullOrWhiteSpace(ApiKey);

    public bool IsEndpointValid =>
        Uri.TryCreate(Endpoint?.Trim(), UriKind.Absolute, out var uri) &&
        (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
         string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase));

    public bool IsConfigured => HasApiKey && IsEndpointValid;
}
