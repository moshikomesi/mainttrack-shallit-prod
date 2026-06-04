using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MaintTrack.Application.AnnualPlans;
using MaintTrack.Application.Abstractions;
using MaintTrack.Domain.AnnualPlans;
using MaintTrack.Domain.Audit;
using MaintTrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MaintTrack.Infrastructure.AnnualPlans;

public sealed class AnnualPlanService : IAnnualPlanService
{
    private readonly MaintTrackDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserContext _currentUserContext;

    public AnnualPlanService(
        MaintTrackDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUserContext currentUserContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _currentUserContext = currentUserContext ?? throw new ArgumentNullException(nameof(currentUserContext));
    }

    public async Task<Guid> CreateOrUpdateAsync(CreateAnnualPlanRequest request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is null)
            throw new InvalidOperationException("Tenant not resolved.");

        var tenantId = _tenantContext.TenantId.Value;
        var userId = _currentUserContext.UserId;
        var now = DateTime.UtcNow;

        // Validate items according to plan type
        foreach (var item in request.Items)
        {
            if (request.Type == PlanType.Preventive)
            {
                if (item.Execution is not null)
                    throw new InvalidOperationException("Execution data is not allowed for preventive plans.");
                if (item.Dates is null || !item.Dates.Any())
                    throw new InvalidOperationException("Preventive plans must contain at least one date per item.");
            }
            else if (request.Type == PlanType.Summer)
            {
                if (item.Execution is null)
                    throw new InvalidOperationException("Summer plans must contain execution data for each item.");
            }
        }

        // Validate tasks
        var taskIds = request.Items.Select(i => i.TaskId).Distinct().ToArray();
        if (taskIds.Length > 0)
        {
            var tasks = await _dbContext.MaintenanceTasks
                .AsNoTracking()
                .Where(t => t.TenantId == tenantId && t.Type == request.Type && taskIds.Contains(t.Id))
                .Select(t => t.Id)
                .ToListAsync(ct);
            if (tasks.Count != taskIds.Length)
                throw new InvalidOperationException("One or more maintenance tasks were not found for this tenant or plan type.");
        }

        // Validate technicians for summer plans
        var workerIds = request.Items
            .Where(i => i.Execution is not null)
            .SelectMany(i => i.Execution!.WorkerIds)
            .Distinct()
            .ToArray();
        if (workerIds.Length > 0)
        {
            var technicians = await _dbContext.Technicians
                .AsNoTracking()
                .Where(t => t.TenantId == tenantId && workerIds.Contains(t.Id))
                .Select(t => t.Id)
                .ToListAsync(ct);
            if (technicians.Count != workerIds.Length)
                throw new InvalidOperationException("One or more technicians were not found for this tenant.");
        }

        // Remove existing plan if present (per tenant/year/type)
        var existing = await _dbContext.AnnualPlans
            .Include(p => p.Items)
                .ThenInclude(i => i.Dates)
            .Include(p => p.Items)
                .ThenInclude(i => i.Execution!)
                    .ThenInclude(e => e.Workers)
            .FirstOrDefaultAsync(
                p => p.TenantId == tenantId && p.Year == request.Year && p.Type == request.Type,
                ct);

        if (existing is not null)
        {
            _dbContext.AnnualPlans.Remove(existing);
        }

        var planId = Guid.NewGuid();
        var plan = new AnnualPlan
        {
            Id = planId,
            TenantId = tenantId,
            Year = request.Year,
            Type = request.Type,
            CreatedAt = now
        };

        foreach (var itemRequest in request.Items)
        {
            var itemId = Guid.NewGuid();
            var item = new AnnualPlanItem
            {
                Id = itemId,
                PlanId = planId,
                TaskId = itemRequest.TaskId
            };

            if (request.Type == PlanType.Preventive && itemRequest.Dates is not null)
            {
                foreach (var date in itemRequest.Dates)
                {
                    item.Dates.Add(new PreventivePlanDate
                    {
                        Id = Guid.NewGuid(),
                        PlanItemId = itemId,
                        ScheduledDate = date
                    });
                }
            }

            if (request.Type == PlanType.Summer && itemRequest.Execution is not null)
            {
                var execId = Guid.NewGuid();
                var exec = new SummerPlanExecution
                {
                    Id = execId,
                    PlanItemId = itemId,
                    PlannedStart = itemRequest.Execution.PlannedStart,
                    RequiredDays = itemRequest.Execution.RequiredDays,
                    PlannedFinish = itemRequest.Execution.PlannedFinish,
                    Description = itemRequest.Execution.Description,
                    ActualStart = itemRequest.Execution.ActualStart,
                    ActualFinish = itemRequest.Execution.ActualFinish
                };

                foreach (var workerId in itemRequest.Execution.WorkerIds)
                {
                    exec.Workers.Add(new SummerPlanWorker
                    {
                        Id = Guid.NewGuid(),
                        ExecutionId = execId,
                        TechnicianId = workerId
                    });
                }

                item.Execution = exec;
            }

            plan.Items.Add(item);
        }

        await _dbContext.AnnualPlans.AddAsync(plan, ct);

        var action = request.Type == PlanType.Preventive
            ? "preventive_plan_created"
            : "summer_plan_created";

        await _dbContext.AuditLogs.AddAsync(new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            Action = action,
            EntityName = "AnnualPlan",
            EntityId = planId,
            CreatedAt = now
        }, ct);

        await _dbContext.SaveChangesAsync(ct);
        return planId;
    }

    public async Task<AnnualPlanDto?> GetByYearAsync(int year, PlanType type, CancellationToken ct)
    {
        if (_tenantContext.TenantId is null)
            throw new InvalidOperationException("Tenant not resolved.");

        var tenantId = _tenantContext.TenantId.Value;

        var dto = await _dbContext.AnnualPlans
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.Year == year && p.Type == type)
            .Select(p => new AnnualPlanDto(
                p.Id,
                p.Year,
                p.Type,
                p.Items
                    .OrderBy(i => i.Task.OrderIndex)
                    .Select(i => new AnnualPlanItemDto(
                        i.Task.TranslationKey,
                        i.Dates.Select(d => d.ScheduledDate).ToList(),
                        i.Execution == null
                            ? null
                            : new SummerExecutionDto(
                                i.Execution.PlannedStart,
                                i.Execution.RequiredDays,
                                i.Execution.PlannedFinish,
                                i.Execution.Description,
                                i.Execution.ActualStart,
                                i.Execution.ActualFinish,
                                i.Execution.Workers
                                    .OrderBy(w => w.Id)
                                    .Select(w => w.Technician.TranslationKey)
                                    .ToList()
                            )))
                    .ToList()))
            .FirstOrDefaultAsync(ct);

        return dto;
    }

    public async Task<IEnumerable<AnnualPlanDto>> GetListAsync(int? year, PlanType? type, CancellationToken ct)
    {
        if (_tenantContext.TenantId is null)
            throw new InvalidOperationException("Tenant not resolved.");

        var tenantId = _tenantContext.TenantId.Value;

        var query = _dbContext.AnnualPlans
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId);

        if (year.HasValue)
            query = query.Where(p => p.Year == year.Value);
        if (type.HasValue)
            query = query.Where(p => p.Type == type.Value);

        var list = await query
            .OrderByDescending(p => p.Year)
            .ThenBy(p => p.Type)
            .Select(p => new AnnualPlanDto(
                p.Id,
                p.Year,
                p.Type,
                p.Items
                    .OrderBy(i => i.Task.OrderIndex)
                    .Select(i => new AnnualPlanItemDto(
                        i.Task.TranslationKey,
                        i.Dates.Select(d => d.ScheduledDate).ToList(),
                        i.Execution == null
                            ? null
                            : new SummerExecutionDto(
                                i.Execution.PlannedStart,
                                i.Execution.RequiredDays,
                                i.Execution.PlannedFinish,
                                i.Execution.Description,
                                i.Execution.ActualStart,
                                i.Execution.ActualFinish,
                                i.Execution.Workers
                                    .OrderBy(w => w.Id)
                                    .Select(w => w.Technician.TranslationKey)
                                    .ToList()
                            )))
                    .ToList()))
            .ToListAsync(ct);

        return list;
    }
}

