using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MaintTrack.Application.Abstractions;

public interface IFileStorageService
{
    Task<string> UploadAsync(
        Stream stream,
        string fileName,
        string contentType,
        string tenantId,
        CancellationToken ct);

    Task DeleteAsync(string fileUrl, CancellationToken ct);
}
