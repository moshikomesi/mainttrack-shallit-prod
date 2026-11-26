using System.Net;
using System.Text;
using System.Text.Json;
using MaintTrack.Application.Translations;
using MaintTrack.Infrastructure.Persistence;
using MaintTrack.Infrastructure.Translations;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace MaintTrack.Tests.Translations;

public sealed class AzureTranslatorProviderTests
{
    private const string TestApiKey = "secret-azure-test-key-do-not-leak";

    [Fact]
    public async Task TranslateWithMetadataAsync_GlobalResource_OmitsFromAndRegionHeaders_AndReturnsTranslation()
    {
        var handler = new RecordingHandler(_ => JsonResponse(
            HttpStatusCode.OK,
            """
            [
              {
                "detectedLanguage": { "language": "th", "score": 1.0 },
                "translations": [ { "text": "האטם דולף.", "to": "he" } ]
              }
            ]
            """));

        var provider = CreateProvider(handler, region: null);
        var result = await provider.TranslateWithMetadataAsync(
            "ซีลรั่ว",
            sourceLanguage: null,
            targetLanguage: "he",
            CancellationToken.None);

        Assert.Equal("האטם דולף.", result.Text);
        Assert.Equal("th", result.DetectedSourceLanguage);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(
            "https://api.cognitive.microsofttranslator.com/translate?api-version=3.0&to=he",
            request.Uri);
        Assert.DoesNotContain("from=", request.Uri, StringComparison.Ordinal);
        Assert.DoesNotContain(TestApiKey, request.Uri, StringComparison.Ordinal);
        Assert.Equal(TestApiKey, Assert.Single(request.Headers["Ocp-Apim-Subscription-Key"]));
        Assert.False(request.Headers.ContainsKey("Ocp-Apim-Subscription-Region"));

        using var payload = JsonDocument.Parse(request.Body);
        Assert.Equal("ซีลรั่ว", payload.RootElement[0].GetProperty("Text").GetString());
    }

    [Fact]
    public async Task TranslateAsync_WithExplicitSourceAndRegion_IncludesFromQueryAndRegionHeader()
    {
        var handler = new RecordingHandler(_ => JsonResponse(
            HttpStatusCode.OK,
            """
            [
              {
                "translations": [ { "text": "Seal leaking", "to": "en" } ]
              }
            ]
            """));

        var provider = CreateProvider(handler, region: "westeurope");
        var translated = await provider.TranslateAsync(
            "אטם דולף",
            sourceLanguage: "he",
            targetLanguage: "en",
            CancellationToken.None);

        Assert.Equal("Seal leaking", translated);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(
            "https://api.cognitive.microsofttranslator.com/translate?api-version=3.0&from=he&to=en",
            request.Uri);
        Assert.Equal("westeurope", Assert.Single(request.Headers["Ocp-Apim-Subscription-Region"]));
    }

    [Theory]
    [InlineData("""[{"language":"he","score":1.0,"isTranslationSupported":true}]""", "he")]
    [InlineData("""[{"language":"en","score":0.95,"isTranslationSupported":true}]""", "en")]
    [InlineData("""[{"language":"th","score":1.0,"isTranslationSupported":true}]""", "th")]
    [InlineData("""[{"language":"en-US","score":0.9,"isTranslationSupported":true}]""", "en")]
    [InlineData("""[{"language":"iw","score":0.9,"isTranslationSupported":true}]""", "he")]
    [InlineData("""[{"language":"fr","score":0.99,"isTranslationSupported":true}]""", "fr")]
    [InlineData("""[{"language":"en","score":0.25,"isTranslationSupported":true}]""", null)]
    [InlineData("""[{"language":"und","score":1.0,"isTranslationSupported":true}]""", null)]
    [InlineData("""[{"language":"th","score":1.0,"isTranslationSupported":false}]""", null)]
    public async Task DetectLanguageAsync_InterpretsLanguageConfidenceAndSupport(
        string responseJson,
        string? expectedLanguage)
    {
        var handler = new RecordingHandler(_ => JsonResponse(HttpStatusCode.OK, responseJson));
        var provider = CreateProvider(handler, region: null);

        var detected = await provider.DetectLanguageAsync("Sample narrative", CancellationToken.None);

        Assert.Equal(expectedLanguage, detected);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(
            "https://api.cognitive.microsofttranslator.com/detect?api-version=3.0",
            request.Uri);
        Assert.Equal(TestApiKey, Assert.Single(request.Headers["Ocp-Apim-Subscription-Key"]));
        Assert.False(request.Headers.ContainsKey("Ocp-Apim-Subscription-Region"));
    }

    [Theory]
    [InlineData("""[{"translations":[{"text":""}]}]""")]
    [InlineData("""[{"translations":[{"text":"   \n\t  "}]}]""")]
    [InlineData("""[{"translations":[]}]""")]
    [InlineData("""[{"detectedLanguage":{"language":"en","score":1.0}}]""")]
    [InlineData("""[]""")]
    [InlineData("""{"not":"an-array"}""")]
    [InlineData("""{malformed-json""")]
    public async Task TranslateAsync_RejectsEmptyWhitespaceAndMalformedPayloads(string responseBody)
    {
        var handler = new RecordingHandler(_ => JsonResponse(HttpStatusCode.OK, responseBody));
        var provider = CreateProvider(handler, region: null);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.TranslateAsync("Seal leaking", null, "he", CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, """{"error":{"code":401000,"message":"Unauthorized"}}""")]
    [InlineData(HttpStatusCode.TooManyRequests, """{"error":{"code":429001,"message":"Rate limited"}}""")]
    [InlineData(HttpStatusCode.InternalServerError, "raw server failure")]
    public async Task TranslateAsync_HttpErrors_ThrowSafelyWithoutLeakingApiKey(
        HttpStatusCode statusCode,
        string responseBody)
    {
        var handler = new RecordingHandler(_ => JsonResponse(statusCode, responseBody));
        var provider = CreateProvider(handler, region: null);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            provider.TranslateAsync("Seal leaking", null, "he", CancellationToken.None));

        Assert.Equal(statusCode, exception.StatusCode);
        Assert.DoesNotContain(TestApiKey, exception.ToString(), StringComparison.Ordinal);
        Assert.Contains(((int)statusCode).ToString(), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TranslateAsync_NonSeekableChunkedErrorStream_ParsesAzureErrorCode()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StreamContent(new NonSeekableStream(
                Encoding.UTF8.GetBytes("""{"error":{"code":401000,"message":"Access denied"}}""")))
        });
        var provider = CreateProvider(handler, region: null);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            provider.TranslateAsync("Seal leaking", null, "he", CancellationToken.None));

        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
        Assert.Contains("401000", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(TestApiKey, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DetectLanguageAsync_BlankInput_ReturnsNullWithoutHttpRequest()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("HTTP call unexpected"));
        var provider = CreateProvider(handler, region: null);

        Assert.Null(await provider.DetectLanguageAsync("", CancellationToken.None));
        Assert.Null(await provider.DetectLanguageAsync("   \n ", CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("he", new[] { "en", "th" })]
    [InlineData("iw", new[] { "en", "th" })]
    [InlineData("he-IL", new[] { "en", "th" })]
    [InlineData("en", new[] { "he", "th" })]
    [InlineData("en-US", new[] { "he", "th" })]
    [InlineData("th", new[] { "he", "en" })]
    [InlineData("th-TH", new[] { "he", "en" })]
    [InlineData("fr", new[] { "he", "en", "th" })]
    [InlineData("und", new[] { "he", "en", "th" })]
    [InlineData(null, new[] { "he", "en", "th" })]
    public void TranslationLanguages_TargetsExcluding_HandlesSupportedRegionalAndUnknownTags(
        string? detected,
        string[] expected)
    {
        Assert.Equal(expected, MaintTrack.Domain.Translations.TranslationLanguages.TargetsExcluding(detected));
    }

    [Fact]
    public async Task TranslateAsync_TimeoutThrowsTimeoutException_WhileCallerCancellationPropagates()
    {
        var timeoutHandler = new RecordingHandler((_, ct) =>
            Task.FromException<HttpResponseMessage>(new TaskCanceledException("Simulated HttpClient timeout")));
        var provider = CreateProvider(timeoutHandler, region: null);

        await Assert.ThrowsAsync<TimeoutException>(() =>
            provider.TranslateAsync("Seal leaking", null, "he", CancellationToken.None));

        using var callerCts = new CancellationTokenSource();
        callerCts.Cancel();
        var cancelledHandler = new RecordingHandler((_, ct) =>
            Task.FromCanceled<HttpResponseMessage>(ct));
        var cancelledProvider = CreateProvider(cancelledHandler, region: null);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            cancelledProvider.TranslateAsync("Seal leaking", null, "he", callerCts.Token));
    }

    [Fact]
    public async Task DetectLanguageAsync_RespectsConfiguredDetectionTimeout()
    {
        var handler = new RecordingHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return JsonResponse(HttpStatusCode.OK, """[{"language":"en","score":1.0}]""");
        });

        var options = Options.Create(new AzureTranslatorOptions
        {
            ApiKey = TestApiKey,
            Endpoint = AzureTranslatorOptions.DefaultEndpoint,
            DetectionTimeoutSeconds = 1
        });
        var provider = new AzureTranslatorProvider(new HttpClient(handler), options);

        await Assert.ThrowsAsync<TimeoutException>(() =>
            provider.DetectLanguageAsync("Seal leaking", CancellationToken.None));
    }

    [Theory]
    [InlineData("https://api.cognitive.microsofttranslator.com")]
    [InlineData("https://api.cognitive.microsofttranslator.com/")]
    [InlineData("  https://api.cognitive.microsofttranslator.com  ")]
    [InlineData("HTTPS://westeurope.api.cognitive.microsoft.com")]
    [InlineData("https://my-resource.cognitiveservices.azure.com")]
    public void Options_AcceptHttpsEndpoints(string endpoint)
    {
        var options = new AzureTranslatorOptions { Endpoint = endpoint, ApiKey = TestApiKey };

        Assert.True(options.IsEndpointValid);
        Assert.True(options.IsConfigured);
    }

    [Theory]
    [InlineData("http://api.cognitive.microsofttranslator.com")]
    [InlineData("HTTP://api.cognitive.microsofttranslator.com")]
    [InlineData("http://localhost:5000")]
    [InlineData("ftp://api.cognitive.microsofttranslator.com")]
    [InlineData("api.cognitive.microsofttranslator.com")]
    [InlineData("/translate")]
    [InlineData("not-a-valid-uri")]
    [InlineData("https://")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Options_RejectHttpAndMalformedEndpoints(string? endpoint)
    {
        var options = new AzureTranslatorOptions { Endpoint = endpoint!, ApiKey = TestApiKey };

        Assert.False(options.IsEndpointValid);
        Assert.False(options.IsConfigured);
    }

    [Fact]
    public void Options_DefaultGlobalEndpoint_IsHttpsAndValid()
    {
        var options = new AzureTranslatorOptions { ApiKey = TestApiKey };

        Assert.Equal("https://api.cognitive.microsofttranslator.com", AzureTranslatorOptions.DefaultEndpoint);
        Assert.Equal(AzureTranslatorOptions.DefaultEndpoint, options.Endpoint);
        Assert.True(options.IsEndpointValid);
        Assert.True(options.IsConfigured);
        Assert.False(new AzureTranslatorOptions().IsConfigured);
    }

    [Theory]
    [InlineData("http://api.cognitive.microsofttranslator.com")]
    [InlineData("not-a-valid-uri")]
    [InlineData("")]
    public void Provider_RejectsHttpAndMalformedEndpoints_WithoutLeakingTheKey(string endpoint)
    {
        var options = Options.Create(new AzureTranslatorOptions { Endpoint = endpoint, ApiKey = TestApiKey });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new AzureTranslatorProvider(new HttpClient(new RecordingHandler(_ => JsonResponse(HttpStatusCode.OK, "[]"))), options));

        Assert.Contains("HTTPS", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(TestApiKey, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Provider_SendsRequestsOverHttpsToTheConfiguredEndpoint()
    {
        var handler = new RecordingHandler(_ => JsonResponse(
            HttpStatusCode.OK,
            """[{"translations":[{"text":"Seal leaking","to":"en"}]}]"""));
        var options = Options.Create(new AzureTranslatorOptions
        {
            Endpoint = "https://westeurope.api.cognitive.microsoft.com/",
            ApiKey = TestApiKey
        });
        var provider = new AzureTranslatorProvider(new HttpClient(handler), options);

        await provider.TranslateAsync("אטם דולף", null, "en", CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.StartsWith("https://westeurope.api.cognitive.microsoft.com/translate?", request.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProgramDi_RegistersProviderOnlyWhenApiKeyIsConfigured()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        using (var unconfiguredFactory = CreateHostFactory(connection, apiKey: null, endpoint: null))
        using (var scope = unconfiguredFactory.Services.CreateScope())
        {
            Assert.Null(scope.ServiceProvider.GetService<ITranslationProvider>());
        }

        using (var configuredFactory = CreateHostFactory(connection, apiKey: TestApiKey, endpoint: AzureTranslatorOptions.DefaultEndpoint))
        using (var scope = configuredFactory.Services.CreateScope())
        {
            var provider = scope.ServiceProvider.GetService<ITranslationProvider>();
            Assert.IsType<AzureTranslatorProvider>(provider);
        }

        // The shipped appsettings.json default (Global endpoint) must keep working with only an API key.
        using (var globalDefaultFactory = CreateHostFactory(connection, apiKey: TestApiKey, endpoint: null))
        using (var scope = globalDefaultFactory.Services.CreateScope())
        {
            Assert.IsType<AzureTranslatorProvider>(scope.ServiceProvider.GetService<ITranslationProvider>());
        }

        foreach (var invalidEndpoint in new[] { "not-a-valid-uri", "http://api.cognitive.microsofttranslator.com" })
        {
            var exception = Assert.Throws<InvalidOperationException>(() =>
            {
                using var invalidFactory = CreateHostFactory(connection, apiKey: TestApiKey, endpoint: invalidEndpoint);
                _ = invalidFactory.Services;
            });
            Assert.Contains("HTTPS", exception.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(TestApiKey, exception.ToString(), StringComparison.Ordinal);
        }
    }

    private static AzureTranslatorProvider CreateProvider(RecordingHandler handler, string? region)
    {
        var options = Options.Create(new AzureTranslatorOptions
        {
            Endpoint = AzureTranslatorOptions.DefaultEndpoint,
            ApiKey = TestApiKey,
            Region = region,
            TimeoutSeconds = 15,
            DetectionTimeoutSeconds = 5
        });

        return new AzureTranslatorProvider(new HttpClient(handler), options);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string content) =>
        new(statusCode)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };

    private static WebApplicationFactory<Program> CreateHostFactory(
        SqliteConnection connection,
        string? apiKey,
        string? endpoint)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Jwt:Issuer", "MaintTrack.Test");
            builder.UseSetting("Jwt:Audience", "MaintTrack.Api.Test");
            builder.UseSetting("Jwt:Key", "TestSecretKeyForJwtTokenGeneration12345678901234567890");
            builder.UseSetting("Translation:EnqueueEnabled", "false");
            builder.UseSetting("Translation:Azure:ApiKey", apiKey ?? string.Empty);
            if (endpoint is not null)
                builder.UseSetting("Translation:Azure:Endpoint", endpoint);

            builder.ConfigureServices(services =>
            {
                services.RemoveAll(typeof(DbContextOptions<MaintTrackDbContext>));
                services.RemoveAll(typeof(MaintTrackDbContext));
                services.AddDbContext<MaintTrackDbContext>(options => options.UseSqlite(connection));
            });
        });
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            : this((request, _) => Task.FromResult(responder(request)))
        {
        }

        public RecordingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        {
            _responder = responder;
        }

        public List<RecordedRequest> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            var headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in request.Headers)
                headers[header.Key] = header.Value.ToArray();

            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri?.ToString() ?? string.Empty,
                headers,
                body));

            return await _responder(request, cancellationToken);
        }
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        string Uri,
        Dictionary<string, string[]> Headers,
        string Body);

    private sealed class NonSeekableStream : MemoryStream
    {
        public NonSeekableStream(byte[] buffer) : base(buffer) { }
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
    }
}
