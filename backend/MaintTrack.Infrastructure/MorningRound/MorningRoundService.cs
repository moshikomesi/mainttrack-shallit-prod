using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MaintTrack.Application.Abstractions;
using MaintTrack.Application.MorningRound;
using MaintTrack.Domain.Audit;
using MaintTrack.Domain.MorningRound;
using MaintTrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MaintTrack.Infrastructure.MorningRound;

/// <summary>
/// Infrastructure implementation of <see cref="IMorningRoundService" /> backed by EF Core.
/// Uses ICurrentUserContext for performed-by; stores notes as JSON keyed by template item id (GUID).
/// </summary>
public sealed class MorningRoundService : IMorningRoundService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly MaintTrackDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserContext _currentUserContext;

    public MorningRoundService(
        MaintTrackDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUserContext currentUserContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _currentUserContext = currentUserContext ?? throw new ArgumentNullException(nameof(currentUserContext));
    }

    public async Task<MorningRoundDto?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var row = await (from r in _dbContext.MorningRoundReports.AsNoTracking()
                         join u in _dbContext.Users on r.PerformedByUserId equals u.Id into userJoin
                         from u in userJoin.DefaultIfEmpty()
                         where r.Id == id
                         select new
                         {
                             r.Id,
                             r.ReportDate,
                             r.PerformedByUserId,
                             r.PerformedAt,
                             r.NotesJson,
                             PerformedByName = u != null ? u.DisplayName : "Unknown"
                         }).FirstOrDefaultAsync(ct);

        if (row is null)
            return null;

        return new MorningRoundDto(
            row.Id,
            row.ReportDate,
            row.PerformedByUserId,
            row.PerformedByName,
            row.PerformedAt,
            DeserializeNotes(row.NotesJson));
    }

    public async Task<IReadOnlyList<MorningRoundDto>> GetAsync(GetMorningRoundsRequest request, CancellationToken ct)
    {
        var query = from r in _dbContext.MorningRoundReports.AsNoTracking()
                    join u in _dbContext.Users on r.PerformedByUserId equals u.Id into userJoin
                    from u in userJoin.DefaultIfEmpty()
                    select new
                    {
                        r.Id,
                        r.ReportDate,
                        r.PerformedByUserId,
                        r.PerformedAt,
                        r.NotesJson,
                        PerformedByName = u != null ? u.DisplayName : "Unknown"
                    };

        if (request.FromDate.HasValue)
            query = query.Where(x => x.ReportDate >= request.FromDate.Value);

        if (request.ToDate.HasValue)
            query = query.Where(x => x.ReportDate <= request.ToDate.Value);

        if (request.PerformedByUserId.HasValue)
            query = query.Where(x => x.PerformedByUserId == request.PerformedByUserId.Value);

        var pageNumber = request.PageNumber < 1 ? 1 : request.PageNumber;
        var pageSize = request.PageSize < 1 ? 20 : request.PageSize;

        var rows = await query
            .OrderByDescending(x => x.ReportDate)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return rows.Select(row => new MorningRoundDto(
            row.Id,
            row.ReportDate,
            row.PerformedByUserId,
            row.PerformedByName,
            row.PerformedAt,
            DeserializeNotes(row.NotesJson))).ToList();
    }

    public async Task<Guid> CreateAsync(CreateMorningRoundRequest request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is null)
            throw new InvalidOperationException("Tenant not resolved.");

        var performedByUserId = _currentUserContext.UserId;

        if (request.Notes != null && request.Notes.Count > 0)
        {
            var parsedIds = new List<Guid>(request.Notes.Count);

            foreach (var keyStr in request.Notes.Keys)
            {
                if (!Guid.TryParse(keyStr, out var guid))
                    throw new ArgumentException($"Invalid template item id '{keyStr}'");

                parsedIds.Add(guid);
            }

            var validIds = await _dbContext.MorningRoundTemplateItems
                .AsNoTracking()
                .Where(x => parsedIds.Contains(x.Id) && x.IsActive)
                .Select(x => x.Id)
                .ToListAsync(ct);

            if (validIds.Count != parsedIds.Count)
                throw new ArgumentException("One or more template item IDs are invalid or inactive.");
        }

        var notesJson = SerializeNotes(request.Notes);
        var reportId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var report = new MorningRoundReport
        {
            Id = reportId,
            TenantId = _tenantContext.TenantId!.Value,
            ReportDate = request.ReportDate,
            PerformedByUserId = performedByUserId,
            PerformedAt = now,
            NotesJson = notesJson,
            CreatedAt = now
        };

        await _dbContext.MorningRoundReports.AddAsync(report, ct);

        var auditLog = new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.TenantId!.Value,
            UserId = performedByUserId,
            Action = "morning_round_created",
            EntityName = "MorningRoundReport",
            EntityId = report.Id,
            CreatedAt = now
        };
        await _dbContext.AuditLogs.AddAsync(auditLog, ct);

        await _dbContext.SaveChangesAsync(ct);

        return report.Id;
    }

    private static string SerializeNotes(Dictionary<string, string>? notes)
    {
        if (notes == null || notes.Count == 0)
            return "{}";

        var filtered = notes.Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        return filtered.Count == 0 ? "{}" : JsonSerializer.Serialize(filtered, JsonOptions);
    }

    private Dictionary<Guid, string>? DeserializeNotes(string notesJson)
    {
        if (string.IsNullOrWhiteSpace(notesJson) || notesJson == "{}")
            return null;

        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(notesJson, JsonOptions);
            if (dict == null || dict.Count == 0)
                return null;

            var result = new Dictionary<Guid, string>();
            foreach (var kv in dict)
            {
                if (Guid.TryParse(kv.Key, out var id) && !string.IsNullOrEmpty(kv.Value))
                    result[id] = kv.Value;
            }
            return result.Count > 0 ? result : null;
        }
        catch
        {
            return null;
        }
    }
}
