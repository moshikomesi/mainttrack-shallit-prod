using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MaintTrack.Application.Translations;
using Microsoft.Extensions.Options;

namespace MaintTrack.Infrastructure.Translations;

public sealed class AzureTranslatorProvider : ITranslationProvider
{
    private const string ApiVersion = "3.0";
    private const double MinimumDetectionConfidence = 0.5d;
    private const string SubscriptionKeyHeader = "Ocp-Apim-Subscription-Key";
    private const string SubscriptionRegionHeader = "Ocp-Apim-Subscription-Region";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly string _endpoint;
    private readonly string _apiKey;
    private readonly string? _region;
    private readonly TimeSpan _detectionTimeout;

    public AzureTranslatorProvider(
        HttpClient httpClient,
        IOptions<AzureTranslatorOptions> options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        var value = options.Value ?? throw new InvalidOperationException("Azure Translator options are missing.");
        if (!value.HasApiKey)
            throw new InvalidOperationException("Azure Translator API key is not configured.");

        if (!value.IsEndpointValid)
            throw new InvalidOperationException("Azure Translator endpoint must be a valid HTTPS URI.");

        _httpClient = httpClient;
        _endpoint = value.Endpoint.Trim().TrimEnd('/');
        _apiKey = value.ApiKey.Trim();
        _region = string.IsNullOrWhiteSpace(value.Region) ? null : value.Region.Trim();
        _detectionTimeout = value.DetectionTimeout;
    }

    public async Task<string> TranslateAsync(
        string text,
        string? sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        var result = await TranslateWithMetadataAsync(text, sourceLanguage, targetLanguage, cancellationToken);
        return result.Text;
    }

    public async Task<string?> DetectLanguageAsync(
        string text,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var uri = $"{_endpoint}/detect?api-version={ApiVersion}";
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_detectionTimeout);

        using var request = CreateRequest(uri, text);
        using var response = await SendAsync(
            request,
            "language detection",
            callerToken: cancellationToken,
            effectiveToken: timeoutCts.Token);

        await EnsureSuccessStatusCodeAsync(
            response,
            "language detection",
            callerToken: cancellationToken,
            effectiveToken: timeoutCts.Token);

        using var document = await ParseJsonAsync(
            response,
            "language detection",
            callerToken: cancellationToken,
            effectiveToken: timeoutCts.Token);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
            throw new InvalidOperationException("Azure Translator language detection returned an unexpected response payload.");

        var first = root[0];
        if (first.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Azure Translator language detection returned an unexpected response item.");

        if (first.TryGetProperty("isTranslationSupported", out var supportedElement) &&
            supportedElement.ValueKind == JsonValueKind.False)
        {
            return null;
        }

        if (first.TryGetProperty("score", out var scoreElement) &&
            scoreElement.ValueKind == JsonValueKind.Number &&
            scoreElement.TryGetDouble(out var score) &&
            score < MinimumDetectionConfidence)
        {
            return null;
        }

        if (!first.TryGetProperty("language", out var languageElement) ||
            languageElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return NormalizeLanguageTag(languageElement.GetString());
    }

    public async Task<TranslationResult> TranslateWithMetadataAsync(
        string text,
        string? sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Source text cannot be empty.", nameof(text));

        if (string.IsNullOrWhiteSpace(targetLanguage))
            throw new ArgumentException("Target language is required.", nameof(targetLanguage));

        var trimmedSource = string.IsNullOrWhiteSpace(sourceLanguage) ? null : sourceLanguage.Trim();
        var trimmedTarget = targetLanguage.Trim();

        var uriBuilder = new StringBuilder();
        uriBuilder.Append(_endpoint)
            .Append("/translate?api-version=")
            .Append(ApiVersion);
        if (trimmedSource is not null)
        {
            uriBuilder.Append("&from=")
                .Append(Uri.EscapeDataString(trimmedSource));
        }

        uriBuilder.Append("&to=")
            .Append(Uri.EscapeDataString(trimmedTarget));

        using var request = CreateRequest(uriBuilder.ToString(), text);
        using var response = await SendAsync(
            request,
            "translation",
            callerToken: cancellationToken,
            effectiveToken: cancellationToken);

        await EnsureSuccessStatusCodeAsync(
            response,
            "translation",
            callerToken: cancellationToken,
            effectiveToken: cancellationToken);

        using var document = await ParseJsonAsync(
            response,
            "translation",
            callerToken: cancellationToken,
            effectiveToken: cancellationToken);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
            throw new InvalidOperationException("Azure Translator returned an unexpected response payload.");

        var first = root[0];
        if (first.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Azure Translator returned an unexpected response item.");

        if (!first.TryGetProperty("translations", out var translations) ||
            translations.ValueKind != JsonValueKind.Array ||
            translations.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("Azure Translator response contained no translations.");
        }

        var firstTranslation = translations[0];
        if (firstTranslation.ValueKind != JsonValueKind.Object ||
            !firstTranslation.TryGetProperty("text", out var textElement) ||
            textElement.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException("Azure Translator response did not include translated text.");
        }

        var translatedText = textElement.GetString();
        if (string.IsNullOrWhiteSpace(translatedText))
            throw new InvalidOperationException("Azure Translator returned empty translation text.");

        var detectedSource = trimmedSource is not null
            ? NormalizeLanguageTag(trimmedSource)
            : ExtractDetectedLanguage(first);

        return new TranslationResult(translatedText, detectedSource);
    }

    private HttpRequestMessage CreateRequest(string uri, string text)
    {
        var payload = JsonSerializer.Serialize(new[] { new TranslateRequestItem(text) }, SerializerOptions);
        var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation(SubscriptionKeyHeader, _apiKey);
        if (_region is not null)
            request.Headers.TryAddWithoutValidation(SubscriptionRegionHeader, _region);

        return request;
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        string operation,
        CancellationToken callerToken,
        CancellationToken effectiveToken)
    {
        try
        {
            return await _httpClient.SendAsync(request, effectiveToken);
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            throw new TimeoutException($"Azure Translator {operation} request timed out.", exception);
        }
    }

    private static async Task EnsureSuccessStatusCodeAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken callerToken,
        CancellationToken effectiveToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        int? errorCode = null;
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(effectiveToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: effectiveToken);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("error", out var errorElement) &&
                errorElement.ValueKind == JsonValueKind.Object &&
                errorElement.TryGetProperty("code", out var codeElement) &&
                codeElement.ValueKind == JsonValueKind.Number &&
                codeElement.TryGetInt32(out var parsedCode))
            {
                errorCode = parsedCode;
            }
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            throw new TimeoutException($"Azure Translator {operation} request timed out.", exception);
        }
        catch
        {
            // Ignore empty/malformed error bodies; never leak raw response/headers in exception messages.
        }

        var message = errorCode is int code
            ? $"Azure Translator {operation} failed with HTTP {(int)response.StatusCode} (error code {code})."
            : $"Azure Translator {operation} failed with HTTP {(int)response.StatusCode}.";

        throw new HttpRequestException(message, null, response.StatusCode);
    }

    private static async Task<JsonDocument> ParseJsonAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken callerToken,
        CancellationToken effectiveToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(effectiveToken);
            return await JsonDocument.ParseAsync(stream, cancellationToken: effectiveToken);
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            throw new TimeoutException($"Azure Translator {operation} request timed out.", exception);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"Azure Translator {operation} returned malformed JSON.",
                exception);
        }
    }

    private static string? ExtractDetectedLanguage(JsonElement item)
    {
        if (!item.TryGetProperty("detectedLanguage", out var detected) ||
            detected.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (detected.TryGetProperty("score", out var scoreElement) &&
            scoreElement.ValueKind == JsonValueKind.Number &&
            scoreElement.TryGetDouble(out var score) &&
            score < MinimumDetectionConfidence)
        {
            return null;
        }

        if (!detected.TryGetProperty("language", out var languageElement) ||
            languageElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return NormalizeLanguageTag(languageElement.GetString());
    }

    private static string? NormalizeLanguageTag(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return null;

        var trimmed = language.Trim();
        var separatorIndex = trimmed.IndexOfAny(new[] { '-', '_' });
        var primary = (separatorIndex >= 0 ? trimmed[..separatorIndex] : trimmed).ToLowerInvariant();
        if (string.IsNullOrEmpty(primary) || primary == "und")
            return null;

        return primary == "iw" ? "he" : primary;
    }

    private sealed record TranslateRequestItem([property: JsonPropertyName("Text")] string Text);
}
