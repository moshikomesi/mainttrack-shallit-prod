using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using MaintTrack.Application.Abstractions;
using Microsoft.Extensions.Configuration;

namespace MaintTrack.Infrastructure.Storage;

public sealed class S3FileStorageService : IFileStorageService
{
    private readonly IAmazonS3 _s3;
    private readonly IConfiguration _configuration;
    private readonly string _bucketName;

    private const string BucketConfigKey = "S3:BucketName";

    /// <summary>
    /// Characters that are unsafe in S3 keys (path separators and common dangerous chars).
    /// </summary>
    private static readonly Regex DangerousFileNameChars = new(@"[\/\\:*?""<>|\x00-\x1f]", RegexOptions.Compiled);

    public S3FileStorageService(IAmazonS3 s3, IConfiguration configuration)
    {
        _s3 = s3 ?? throw new ArgumentNullException(nameof(s3));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _bucketName = _configuration[BucketConfigKey]
            ?? throw new InvalidOperationException($"S3 bucket name is not configured. Set '{BucketConfigKey}' in configuration.");
    }

    public async Task<string> UploadAsync(
        Stream stream,
        string fileName,
        string contentType,
        string tenantId,
        CancellationToken ct)
    {
        if (stream is null)
            throw new ArgumentNullException(nameof(stream));
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new InvalidOperationException("Tenant identifier is required.");

        var safeFileName = SanitizeFileName(fileName);
        var key = $"tenants/{tenantId}/maintenance/{Guid.NewGuid():N}-{safeFileName}";

        var effectiveContentType = !string.IsNullOrWhiteSpace(contentType)
            ? contentType.Trim()
            : "application/octet-stream";

        var request = new PutObjectRequest
        {
            BucketName = _bucketName,
            Key = key,
            InputStream = stream,
            ContentType = effectiveContentType,
            AutoCloseStream = false
        };


        await _s3.PutObjectAsync(request, ct);

        var region = _s3.Config.RegionEndpoint?.SystemName
            ?? throw new InvalidOperationException("S3 region is not configured on the AWS client.");

        var urlSafeKey = string.Join("/", key.Split('/').Select(Uri.EscapeDataString));

        return $"https://{_bucketName}.s3.{region}.amazonaws.com/{urlSafeKey}";
    }

    public async Task DeleteAsync(string fileUrl, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(fileUrl))
            return;

        var key = GetKeyFromUrl(fileUrl);
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("The file URL is malformed or does not contain a valid S3 object key.", nameof(fileUrl));

        await _s3.DeleteObjectAsync(new DeleteObjectRequest
        {
            BucketName = _bucketName,
            Key = key
        }, ct);
    }

    /// <summary>
    /// Sanitizes a file name for use in an S3 key: removes path separators and dangerous characters, preserves extension, trims whitespace.
    /// </summary>
    private static string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "file";

        // Remove path separators by taking only the file name part
        var name = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(name))
            return "file";

        name = name.Trim();

        // Remove dangerous characters (path separators, control chars, etc.)
        name = DangerousFileNameChars.Replace(name, string.Empty);

        // Collapse multiple dots to avoid ".." and trim again
        while (name.Contains("..", StringComparison.Ordinal))
            name = name.Replace("..", ".", StringComparison.Ordinal);

        if (string.IsNullOrWhiteSpace(name))
            return "file";

        return name;
    }

    /// <summary>
    /// Extracts the S3 object key from a URL like https://bucket.s3.{region}.amazonaws.com/maintenance/guid-filename.jpg.
    /// The key is the path after the host (after ".com/").
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when the URL is malformed or the key cannot be parsed.</exception>
    private static string? GetKeyFromUrl(string fileUrl)
    {
        if (string.IsNullOrWhiteSpace(fileUrl))
            return null;

        if (!Uri.TryCreate(fileUrl, UriKind.Absolute, out var uri) || !uri.IsAbsoluteUri)
            return null;

        // Extract key as the path after the authority (e.g. after ".com/")
        var path = uri.AbsolutePath;
        if (string.IsNullOrEmpty(path) || path.Length <= 1)
            return null;

        var key = path.TrimStart('/');
        if (string.IsNullOrEmpty(key))
            return null;

        return key;
    }
}
