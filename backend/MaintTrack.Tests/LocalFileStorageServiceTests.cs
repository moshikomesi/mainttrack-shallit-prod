using MaintTrack.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;

namespace MaintTrack.Tests;

public sealed class LocalFileStorageServiceTests
{
    [Fact]
    public async Task UploadAsync_UsesConfiguredBasePath()
    {
        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            "mainttrack-storage-tests",
            Guid.NewGuid().ToString("N"),
            "uploads");

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Storage:BasePath"] = tempRoot
                })
                .Build();

            var service = new LocalFileStorageService(configuration);
            await using var stream = new MemoryStream(new byte[] { 1, 2, 3 });

            var url = await service.UploadAsync(
                stream,
                "test.png",
                "image/png",
                "tenant/../alpha",
                CancellationToken.None);

            var expectedPath = Path.Combine(
                Directory.GetParent(tempRoot)!.FullName,
                url.TrimStart('/'));

            Assert.StartsWith("/uploads/tenantalpha/", url);
            Assert.True(File.Exists(expectedPath));

            await service.DeleteAsync(url, CancellationToken.None);

            Assert.False(File.Exists(expectedPath));
        }
        finally
        {
            var cleanupRoot = Path.Combine(Path.GetTempPath(), "mainttrack-storage-tests");
            if (Directory.Exists(cleanupRoot))
            {
                Directory.Delete(cleanupRoot, recursive: true);
            }
        }
    }
}
