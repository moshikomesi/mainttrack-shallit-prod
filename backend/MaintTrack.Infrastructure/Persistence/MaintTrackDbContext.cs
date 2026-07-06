using MaintTrack.Application.Abstractions;
using MaintTrack.Domain.Arrays;
using MaintTrack.Domain.Audit;
using MaintTrack.Domain.Forklifts;
using MaintTrack.Domain.Machines;
using MaintTrack.Domain.MachineComponents;
using MaintTrack.Domain.Maintenance;
using MaintTrack.Domain.MaintenanceTasks;
using MaintTrack.Domain.MorningRound;
using MaintTrack.Domain.MorningRoundV2;
using MaintTrack.Domain.Treatments;
using MaintTrack.Domain.Tenants;
using MaintTrack.Domain.Users;
using MaintTrack.Domain.AnnualPlans;
using Microsoft.EntityFrameworkCore;

namespace MaintTrack.Infrastructure.Persistence;

/// <summary>
/// Entity Framework Core DbContext for MaintTrack.
/// </summary>
public class MaintTrackDbContext : DbContext
{
    private readonly ITenantContext _tenantContext;

    public MaintTrackDbContext(DbContextOptions<MaintTrackDbContext> options, ITenantContext tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<User> Users => Set<User>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<Machine> Machines => Set<Machine>();

    public DbSet<MachineComponent> MachineComponents => Set<MachineComponent>();

    public DbSet<MachineComponentMapping> MachineComponentMappings => Set<MachineComponentMapping>();

    public DbSet<WorkGroup> Arrays => Set<WorkGroup>();

    public DbSet<MorningRoundReport> MorningRoundReports => Set<MorningRoundReport>();

    public DbSet<MorningRoundTemplateItem> MorningRoundTemplateItems => Set<MorningRoundTemplateItem>();

    public DbSet<MorningRoundV2Submission> MorningRoundV2Submissions => Set<MorningRoundV2Submission>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<MaintenanceEntry> MaintenanceEntries => Set<MaintenanceEntry>();

    public DbSet<MaintenanceType> MaintenanceTypes => Set<MaintenanceType>();

    public DbSet<Treatment> Treatments => Set<Treatment>();

    public DbSet<Forklift> Forklifts => Set<Forklift>();

    public DbSet<ForkliftReport> ForkliftReports => Set<ForkliftReport>();

    public DbSet<ForkliftTreatment> ForkliftTreatments => Set<ForkliftTreatment>();

    public DbSet<ForkliftFault> ForkliftFaults => Set<ForkliftFault>();

    public DbSet<ForkliftInspection> ForkliftInspections => Set<ForkliftInspection>();

    public DbSet<AnnualPlan> AnnualPlans => Set<AnnualPlan>();

    public DbSet<AnnualPlanItem> AnnualPlanItems => Set<AnnualPlanItem>();

    public DbSet<MaintenanceTask> MaintenanceTasks => Set<MaintenanceTask>();

    public DbSet<MaintenanceTaskLog> MaintenanceTaskLogs => Set<MaintenanceTaskLog>();

    public DbSet<Technician> Technicians => Set<Technician>();

    public DbSet<PreventivePlanDate> AnnualPlanDates => Set<PreventivePlanDate>();

    public DbSet<SummerPlanExecution> AnnualPlanExecution => Set<SummerPlanExecution>();

    public DbSet<SummerPlanWorker> AnnualPlanWorkers => Set<SummerPlanWorker>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureTenants(modelBuilder);
        ConfigureUsers(modelBuilder);
        ConfigureRoles(modelBuilder);
        ConfigureArrays(modelBuilder);
        ConfigureMachines(modelBuilder);
        ConfigureMachineComponents(modelBuilder);
        ConfigureMorningRoundReport(modelBuilder);
        ConfigureMorningRoundTemplateItem(modelBuilder);
        ConfigureMorningRoundV2Submission(modelBuilder);
        ConfigureAuditLog(modelBuilder);
        ConfigureMaintenance(modelBuilder);
        ConfigureMaintenanceTaskLogs(modelBuilder);
        ConfigureTreatment(modelBuilder);
        ConfigureAnnualPlans(modelBuilder);
        ConfigureForklift(modelBuilder);
        ConfigureForkliftReport(modelBuilder);
        ConfigureForkliftTreatment(modelBuilder);
        ConfigureForkliftFault(modelBuilder);
        ConfigureForkliftInspection(modelBuilder);
    }

    private static void ConfigureTenants(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Tenant>();

        entity.ToTable("tenants");

        entity.HasKey(x => x.Id);

        entity.Property(x => x.Id)
            .HasColumnName("id");

        entity.Property(x => x.Name)
            .HasColumnName("name")
            .HasMaxLength(200)
            .IsRequired();

        entity.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .IsRequired();

        entity.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();
    }

    private static void ConfigureRoles(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Role>();

        entity.ToTable("roles");

        entity.HasKey(x => x.Id);

        entity.Property(x => x.Id)
            .HasColumnName("id");

        entity.Property(x => x.Name)
            .HasColumnName("name")
            .HasMaxLength(50)
            .IsRequired();

        entity.HasIndex(x => x.Name)
            .IsUnique();
    }

    private void ConfigureUsers(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<User>();

        entity.ToTable("users");

        entity.HasKey(x => x.Id);

        entity.Property(x => x.Id)
            .HasColumnName("id");

        entity.Property(x => x.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();

        entity.Property(x => x.RoleId)
            .HasColumnName("role_id")
            .IsRequired();

        entity.Property(x => x.Email)
            .HasColumnName("email")
            .HasMaxLength(320)
            .IsRequired();

        entity.Property(x => x.Username)
            .HasColumnName("username")
            .HasMaxLength(200)
            .IsRequired();

        entity.Property(x => x.PasswordHash)
            .HasColumnName("password_hash")
            .IsRequired();

        entity.Property(x => x.DisplayName)
            .HasColumnName("display_name")
            .HasMaxLength(200)
            .IsRequired();

        entity.Property(x => x.IsActive)
            .HasColumnName("is_active")
            .IsRequired();

        entity.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        entity.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at");

        entity.HasOne(x => x.Role)
            .WithMany()
            .HasForeignKey(x => x.RoleId)
            .OnDelete(DeleteBehavior.Restrict);

        // Multi-tenancy: enforce tenant_id for all User queries.
        entity.HasQueryFilter(x => _tenantContext.TenantId == null || x.TenantId == _tenantContext.TenantId);
    }

    private void ConfigureArrays(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<WorkGroup>();

        entity.ToTable("arrays");

        entity.HasKey(x => x.Id);

        entity.Property(x => x.Id)
            .HasColumnName("id");

        entity.Property(x => x.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();

        entity.Property(x => x.NameKey)
            .HasColumnName("name_key")
            .IsRequired();

        entity.Property(x => x.SortOrder)
            .HasColumnName("sort_order")
            .HasDefaultValue(0)
            .IsRequired();

        entity.Property(x => x.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true)
            .IsRequired();

        entity.Property(x => x.IsMorningRoundEnabled)
            .HasColumnName("is_morning_round_enabled")
            .HasDefaultValue(false)
            .IsRequired();

        entity.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        entity.Ignore(x => x.UpdatedAt);

        entity.HasQueryFilter(x => _tenantContext.TenantId == null || x.TenantId == _tenantContext.TenantId);
    }

    private void ConfigureMachines(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Machine>();

        entity.ToTable("machines");

        entity.HasKey(x => x.Id);

        entity.Property(x => x.Id)
            .HasColumnName("id");

        entity.Property(x => x.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();

        entity.Property(x => x.Name)
            .HasColumnName("name")
            .HasMaxLength(200)
            .IsRequired();

        entity.Property(x => x.Code)
            .HasColumnName("code")
            .HasMaxLength(100)
            .IsRequired();

        entity.Property(x => x.Description)
            .HasColumnName("description");

        entity.Property(x => x.IsActive)
            .HasColumnName("is_active")
            .IsRequired();

        entity.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        entity.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at");

        entity.Property(x => x.ArrayId)
            .HasColumnName("array_id");

        entity.Ignore(x => x.Array);

        // Multi-tenancy: enforce tenant_id for all Machine queries.
        entity.HasQueryFilter(x => _tenantContext.TenantId == null || x.TenantId == _tenantContext.TenantId);
    }

    private void ConfigureMachineComponents(ModelBuilder modelBuilder)
    {
        var component = modelBuilder.Entity<MachineComponent>();

        component.ToTable("machine_components");

        component.HasKey(x => x.Id);

        component.Property(x => x.Id)
            .HasColumnName("id");

        component.Property(x => x.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();

        component.Property(x => x.Code)
            .HasColumnName("code")
            .HasMaxLength(100)
            .IsRequired();

        component.Property(x => x.NameKey)
            .HasColumnName("name_key")
            .HasMaxLength(200)
            .IsRequired();

        component.Property(x => x.SortOrder)
            .HasColumnName("sort_order")
            .HasDefaultValue(0)
            .IsRequired();

        component.Property(x => x.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true)
            .IsRequired();

        component.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        component.Ignore(x => x.UpdatedAt);

        component.HasIndex(x => new { x.TenantId, x.Code })
            .IsUnique();

        component.HasQueryFilter(x => _tenantContext.TenantId == null || x.TenantId == _tenantContext.TenantId);

        var mapping = modelBuilder.Entity<MachineComponentMapping>();

        mapping.ToTable("machine_component_mappings");

        mapping.HasKey(x => x.Id);

        mapping.Property(x => x.Id)
            .HasColumnName("id");

        mapping.Property(x => x.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();

        mapping.Property(x => x.MachineId)
            .HasColumnName("machine_id")
            .IsRequired();

        mapping.Property(x => x.ComponentId)
            .HasColumnName("component_id")
            .IsRequired();

        mapping.Property(x => x.SortOrder)
            .HasColumnName("sort_order")
            .HasDefaultValue(0)
            .IsRequired();

        mapping.Property(x => x.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true)
            .IsRequired();

        mapping.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        mapping.Ignore(x => x.UpdatedAt);

        mapping.HasIndex(x => new { x.MachineId, x.ComponentId })
            .IsUnique();

        mapping.HasIndex(x => new { x.TenantId, x.MachineId });

        mapping.HasOne(x => x.Machine)
            .WithMany()
            .HasForeignKey(x => x.MachineId)
            .OnDelete(DeleteBehavior.Cascade);

        mapping.HasOne(x => x.Component)
            .WithMany()
            .HasForeignKey(x => x.ComponentId)
            .OnDelete(DeleteBehavior.Restrict);

        mapping.HasQueryFilter(x => _tenantContext.TenantId == null || x.TenantId == _tenantContext.TenantId);
    }

    private void ConfigureMorningRoundReport(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<MorningRoundReport>();

        entity.ToTable("morning_round_reports");

        entity.HasKey(x => x.Id);

        entity.Property(x => x.Id)
            .HasColumnName("id");

        entity.Property(x => x.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();

        entity.Property(x => x.ReportDate)
            .HasColumnName("report_date")
            .IsRequired();

        entity.Property(x => x.PerformedByUserId)
            .HasColumnName("performed_by_user_id")
            .IsRequired();

        entity.Property(x => x.PerformedAt)
            .HasColumnName("performed_at")
            .IsRequired();

        entity.Property(x => x.NotesJson)
            .HasColumnName("notes_json")
            .IsRequired();

        entity.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        entity.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at");

        entity.HasIndex(x => new { x.TenantId, x.ReportDate })
            .IsUnique();

        entity.HasQueryFilter(x => _tenantContext.TenantId == null || x.TenantId == _tenantContext.TenantId);
    }

    private void ConfigureMorningRoundTemplateItem(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<MorningRoundTemplateItem>();

        entity.ToTable("morning_round_template_items");

        entity.HasKey(x => x.Id);

        entity.Property(x => x.Id)
            .HasColumnName("id");

        entity.Property(x => x.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();

        entity.Property(x => x.TranslationKey)
            .HasColumnName("translation_key")
            .IsRequired();

        entity.Property(x => x.SortOrder)
            .HasColumnName("sort_order")
            .IsRequired();

        entity.Property(x => x.IsActive)
            .HasColumnName("is_active")
            .IsRequired();

        entity.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        entity.HasIndex(x => new { x.TenantId, x.SortOrder });

        entity.HasQueryFilter(x => _tenantContext.TenantId == null || x.TenantId == _tenantContext.TenantId);
    }

    private void ConfigureMorningRoundV2Submission(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<MorningRoundV2Submission>();

        entity.ToTable("morning_round_v2_submissions");

        entity.HasKey(x => x.Id);

        entity.Property(x => x.Id)
            .HasColumnName("id");

        entity.Property(x => x.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();

        entity.Property(x => x.ReportDate)
            .HasColumnName("report_date")
            .IsRequired();

        entity.Property(x => x.SubmittedAt)
            .HasColumnName("submitted_at")
            .IsRequired();

        entity.Property(x => x.SubmittedByUserId)
            .HasColumnName("submitted_by_user_id")
            .IsRequired();

        entity.Property(x => x.ItemsJson)
            .HasColumnName("items_json")
            .HasColumnType("jsonb")
            .IsRequired();

        entity.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        entity.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at");

        entity.HasIndex(x => new { x.TenantId, x.ReportDate })
            .IsUnique();

        entity.HasQueryFilter(x => _tenantContext.TenantId == null || x.TenantId == _tenantContext.TenantId);
    }

    private void ConfigureAuditLog(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<AuditLog>();

        entity.ToTable("audit_logs");

        entity.HasKey(x => x.Id);

        entity.Property(x => x.Id)
            .HasColumnName("id");

        entity.Property(x => x.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();

        entity.Property(x => x.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        entity.Property(x => x.Action)
            .HasColumnName("action")
            .HasMaxLength(200)
            .IsRequired();

        entity.Property(x => x.EntityName)
            .HasColumnName("entity_name")
            .HasMaxLength(200)
            .IsRequired();

        entity.Property(x => x.EntityId)
            .HasColumnName("entity_id")
            .IsRequired();

        entity.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        entity.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at");

        entity.HasIndex(x => new { x.TenantId, x.CreatedAt });
        entity.HasIndex(x => new { x.TenantId, x.EntityName, x.EntityId });

        entity.HasQueryFilter(x => _tenantContext.TenantId == null || x.TenantId == _tenantContext.TenantId);
    }

    private void ConfigureMaintenance(ModelBuilder modelBuilder)
    {
        var typeEntity = modelBuilder.Entity<MaintenanceType>();

        typeEntity.ToTable("maintenance_types");

        typeEntity.HasKey(x => x.Id);

        typeEntity.Property(x => x.Id)
            .HasColumnName("id");

        typeEntity.Property(x => x.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();

        typeEntity.Property(x => x.Code)
            .HasColumnName("code")
            .HasMaxLength(100)
            .IsRequired();

        typeEntity.Property(x => x.IsActive)
            .HasColumnName("is_active")
            .IsRequired();

        typeEntity.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        typeEntity.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at");

        typeEntity.HasIndex(x => new { x.TenantId, x.Code })
            .IsUnique();

        typeEntity.HasQueryFilter(x => _tenantContext.TenantId == null || x.TenantId == _tenantContext.TenantId);

        var entryEntity = modelBuilder.Entity<MaintenanceEntry>();

        entryEntity.ToTable("MaintenanceEntries");

        entryEntity.HasOne(x => x.MaintenanceType)
            .WithMany()
            .HasForeignKey(x => x.MaintenanceTypeId)
            .OnDelete(DeleteBehavior.SetNull);

        entryEntity.HasQueryFilter(x => _tenantContext.TenantId == null || x.TenantId == _tenantContext.TenantId);
    }

    private void ConfigureTreatment(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Treatment>();

        entity.ToTable("treatments");

        entity.HasKey(x => x.Id);

        entity.Property(x => x.Id)
            .HasColumnName("id");

        entity.Property(x => x.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();

        entity.Property(x => x.MachineId)
            .HasColumnName("machine_id");

        entity.HasOne(x => x.Machine)
            .WithMany()
            .HasForeignKey(x => x.MachineId)
            .OnDelete(DeleteBehavior.SetNull);

        entity.Property(x => x.EquipmentType)
            .HasColumnName("equipment_type")
            .HasConversion<string>()
            .IsRequired();

        entity.Property(x => x.TreatmentDate)
            .HasColumnName("treatment_date")
            .IsRequired();

        entity.Property(x => x.MaintenanceTypeId)
            .HasColumnName("maintenance_type_id");

        entity.HasOne(x => x.MaintenanceType)
            .WithMany()
            .HasForeignKey(x => x.MaintenanceTypeId)
            .OnDelete(DeleteBehavior.SetNull);

        entity.Property(x => x.TreatmentType)
            .HasColumnName("treatment_type")
            .HasConversion<string>()
            .IsRequired();

        entity.Property(x => x.Description)
            .HasColumnName("description")
            .HasMaxLength(2000)
            .IsRequired();

        entity.Property(x => x.Technician)
            .HasColumnName("technician")
            .HasMaxLength(200)
            .IsRequired();

        entity.Property(x => x.Cost)
            .HasColumnName("cost")
            .IsRequired();

        entity.Property(x => x.NextDueDate)
            .HasColumnName("next_due_date");

        entity.Property(x => x.CreatedByUserId)
            .HasColumnName("created_by_user_id");

        entity.HasOne(x => x.CreatedByUser)
            .WithMany()
            .HasForeignKey(x => x.CreatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        entity.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        entity.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at");

        entity.HasIndex(x => new { x.TenantId, x.EquipmentType });
        entity.HasIndex(x => new { x.TenantId, x.TreatmentDate });
        entity.HasIndex(x => new { x.TenantId, x.MachineId });
        entity.HasIndex(x => new { x.TenantId, x.MaintenanceTypeId });

        entity.HasQueryFilter(x => _tenantContext.TenantId == null || x.TenantId == _tenantContext.TenantId);
    }

    private void ConfigureAnnualPlans(ModelBuilder modelBuilder)
    {
        // AnnualPlan
        var plan = modelBuilder.Entity<AnnualPlan>();
        plan.ToTable("annual_plans");
        plan.HasKey(x => x.Id);
        plan.Property(x => x.Id).HasColumnName("id");
        plan.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        plan.Property(x => x.Year).HasColumnName("year").IsRequired();
        plan.Property(x => x.Type)
            .HasColumnName("type")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();
        plan.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        plan.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        plan.HasIndex(x => new { x.TenantId, x.Year, x.Type }).IsUnique();
        plan.HasQueryFilter(x => _tenantContext.TenantId == null || x.TenantId == _tenantContext.TenantId);

        // AnnualPlanItem
        var item = modelBuilder.Entity<AnnualPlanItem>();
        item.ToTable("annual_plan_items");
        item.HasKey(x => x.Id);
        item.Property(x => x.Id).HasColumnName("id");
        item.Property(x => x.PlanId).HasColumnName("plan_id").IsRequired();
        item.Property(x => x.TaskId).HasColumnName("task_id").IsRequired();
        item.HasOne(x => x.Plan)
            .WithMany(p => p.Items)
            .HasForeignKey(x => x.PlanId)
            .OnDelete(DeleteBehavior.Cascade);
        item.HasOne(x => x.Task)
            .WithMany()
            .HasForeignKey(x => x.TaskId)
            .OnDelete(DeleteBehavior.Restrict);

        // MaintenanceTask
        var task = modelBuilder.Entity<MaintenanceTask>();
        task.ToTable("annual_plan_tasks");
        task.HasKey(x => x.Id);
        task.Property(x => x.Id).HasColumnName("id");
        task.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        task.Property(x => x.Type)
            .HasColumnName("type")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();
        task.Property(x => x.TranslationKey)
            .HasColumnName("translation_key")
            .HasMaxLength(200)
            .IsRequired();
        task.Property(x => x.OrderIndex)
            .HasColumnName("order_index")
            .IsRequired();
        task.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        task.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        task.HasIndex(x => new { x.TenantId, x.Type, x.OrderIndex });
        task.HasQueryFilter(x => _tenantContext.TenantId == null || x.TenantId == _tenantContext.TenantId);

        // Technician
        var tech = modelBuilder.Entity<Technician>();
        tech.ToTable("technicians");
        tech.HasKey(x => x.Id);
        tech.Property(x => x.Id).HasColumnName("id");
        tech.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        tech.Property(x => x.TranslationKey)
            .HasColumnName("translation_key")
            .HasMaxLength(200)
            .IsRequired();
        tech.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        tech.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        tech.HasQueryFilter(x => _tenantContext.TenantId == null || x.TenantId == _tenantContext.TenantId);

        // PreventivePlanDate
        var date = modelBuilder.Entity<PreventivePlanDate>();
        date.ToTable("annual_plan_dates");
        date.HasKey(x => x.Id);
        date.Property(x => x.Id).HasColumnName("id");
        date.Property(x => x.PlanItemId).HasColumnName("plan_item_id").IsRequired();
        date.Property(x => x.ScheduledDate).HasColumnName("scheduled_date").IsRequired();
        date.HasOne(x => x.PlanItem)
            .WithMany(i => i.Dates)
            .HasForeignKey(x => x.PlanItemId)
            .OnDelete(DeleteBehavior.Cascade);

        // SummerPlanExecution
        var exec = modelBuilder.Entity<SummerPlanExecution>();
        exec.ToTable("annual_plan_execution");
        exec.HasKey(x => x.Id);
        exec.Property(x => x.Id).HasColumnName("id");
        exec.Property(x => x.PlanItemId).HasColumnName("plan_item_id").IsRequired();
        exec.Property(x => x.PlannedStart).HasColumnName("planned_start");
        exec.Property(x => x.RequiredDays).HasColumnName("required_days");
        exec.Property(x => x.PlannedFinish).HasColumnName("planned_finish");
        exec.Property(x => x.Description).HasColumnName("description").HasMaxLength(2000);
        exec.Property(x => x.ActualStart).HasColumnName("actual_start");
        exec.Property(x => x.ActualFinish).HasColumnName("actual_finish");
        exec.HasOne(x => x.PlanItem)
            .WithOne(i => i.Execution)
            .HasForeignKey<SummerPlanExecution>(x => x.PlanItemId)
            .OnDelete(DeleteBehavior.Cascade);

        // SummerPlanWorker
        var worker = modelBuilder.Entity<SummerPlanWorker>();
        worker.ToTable("annual_plan_workers");
        worker.HasKey(x => x.Id);
        worker.Property(x => x.Id).HasColumnName("id");
        worker.Property(x => x.ExecutionId).HasColumnName("execution_id").IsRequired();
        worker.Property(x => x.TechnicianId).HasColumnName("technician_id").IsRequired();
        worker.HasOne(x => x.Execution)
            .WithMany(e => e.Workers)
            .HasForeignKey(x => x.ExecutionId)
            .OnDelete(DeleteBehavior.Cascade);
        worker.HasOne(x => x.Technician)
            .WithMany()
            .HasForeignKey(x => x.TechnicianId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private void ConfigureMaintenanceTaskLogs(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<MaintenanceTaskLog>();

        entity.ToTable("maintenance_tasks");

        entity.HasKey(x => x.Id);

        entity.Property(x => x.Id)
            .HasColumnName("id");

        entity.Property(x => x.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();

        entity.Property(x => x.TaskDate)
            .HasColumnName("task_date")
            .IsRequired();

        entity.Property(x => x.ImageUrl)
            .HasColumnName("image_url")
            .IsRequired();

        entity.Property(x => x.Description)
            .HasColumnName("description");

        entity.Property(x => x.IsConfirmed)
            .HasColumnName("is_confirmed")
            .IsRequired();

        entity.Property(x => x.CreatedByUserId)
            .HasColumnName("created_by_user_id")
            .IsRequired();

        entity.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        entity.HasIndex(x => new { x.TenantId, x.TaskDate });
        entity.HasIndex(x => new { x.TenantId, x.CreatedAt });

        entity.HasQueryFilter(x => _tenantContext.TenantId == null || x.TenantId == _tenantContext.TenantId);
    }

    private void ConfigureForklift(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Forklift>();

        entity.ToTable("forklifts");

        entity.HasKey(x => x.Id);

        entity.Property(x => x.Id)
            .HasColumnName("id");

        entity.Property(x => x.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();

        entity.Property(x => x.LicenseNumber)
            .HasColumnName("license_number")
            .HasMaxLength(200)
            .IsRequired();

        entity.Property(x => x.Description)
            .HasColumnName("description")
            .HasMaxLength(2000);

        entity.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        entity.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at");

        entity.Property(x => x.LastInspectionDate)
            .HasColumnName("last_inspection_date");

        entity.Property(x => x.InspectionExpiryDate)
            .HasColumnName("inspection_expiry_date");

        entity.Property(x => x.InspectionUpdatedAt)
            .HasColumnName("inspection_updated_at");

        entity.HasMany(x => x.Reports)
            .WithOne(x => x.Forklift)
            .HasForeignKey(x => x.ForkliftId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasQueryFilter(x => _tenantContext.TenantId == null || x.TenantId == _tenantContext.TenantId);
    }

    private void ConfigureForkliftReport(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ForkliftReport>();

        entity.ToTable("forklift_reports");

        entity.HasKey(x => x.Id);

        entity.Property(x => x.Id)
            .HasColumnName("id");

        entity.Property(x => x.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();

        entity.Property(x => x.ForkliftId)
            .HasColumnName("forklift_id")
            .IsRequired();

        entity.Property(x => x.ReportDate)
            .HasColumnName("report_date")
            .IsRequired();

        entity.Property(x => x.CreatedByUserId)
            .HasColumnName("created_by_user_id")
            .IsRequired();

        entity.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        entity.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at");

        entity.HasMany(x => x.Treatments)
            .WithOne(x => x.Report)
            .HasForeignKey(x => x.ReportId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasMany(x => x.Faults)
            .WithOne(x => x.Report)
            .HasForeignKey(x => x.ReportId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasMany(x => x.Inspections)
            .WithOne(x => x.Report)
            .HasForeignKey(x => x.ReportId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasQueryFilter(x => _tenantContext.TenantId == null || x.TenantId == _tenantContext.TenantId);
    }

    private void ConfigureForkliftTreatment(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ForkliftTreatment>();

        entity.ToTable("forklift_treatments");

        entity.HasKey(x => x.Id);

        entity.Property(x => x.Id)
            .HasColumnName("id");

        entity.Property(x => x.ReportId)
            .HasColumnName("report_id")
            .IsRequired();

        entity.Property(x => x.Date)
            .HasColumnName("date")
            .IsRequired();

        entity.Property(x => x.Description)
            .HasColumnName("description")
            .HasMaxLength(2000)
            .IsRequired();

        entity.Property(x => x.Technician)
            .HasColumnName("technician")
            .HasMaxLength(200)
            .IsRequired();

        entity.HasQueryFilter(x => _tenantContext.TenantId == null || x.Report.TenantId == _tenantContext.TenantId);
    }

    private void ConfigureForkliftFault(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ForkliftFault>();

        entity.ToTable("forklift_faults");

        entity.HasKey(x => x.Id);

        entity.Property(x => x.Id)
            .HasColumnName("id");

        entity.Property(x => x.ReportId)
            .HasColumnName("report_id")
            .IsRequired();

        entity.Property(x => x.FaultType)
            .HasColumnName("fault_type")
            .HasMaxLength(200)
            .IsRequired();

        entity.Property(x => x.Description)
            .HasColumnName("description")
            .HasMaxLength(2000)
            .IsRequired();

        entity.Property(x => x.RepairCost)
            .HasColumnName("repair_cost")
            .IsRequired();

        entity.HasQueryFilter(x => _tenantContext.TenantId == null || x.Report.TenantId == _tenantContext.TenantId);
    }

    private void ConfigureForkliftInspection(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ForkliftInspection>();

        entity.ToTable("forklift_inspections");

        entity.HasKey(x => x.Id);

        entity.Property(x => x.Id)
            .HasColumnName("id");

        entity.Property(x => x.ReportId)
            .HasColumnName("report_id")
            .IsRequired();

        entity.Property(x => x.TestDate)
            .HasColumnName("test_date")
            .IsRequired();

        entity.Property(x => x.ExpiryDate)
            .HasColumnName("expiry_date")
            .IsRequired();

        entity.HasQueryFilter(x => _tenantContext.TenantId == null || x.Report.TenantId == _tenantContext.TenantId);
    }
}

