# Entity Framework Core Queries Audit Report

**Generated:** Backend codebase analysis  
**Scope:** All DbContext usage, DbSet queries, LINQ, Include/ThenInclude, SaveChanges, and endpoint → query mapping.

---

## 1. DbContext & DbSet Usage Summary

| Location | Type | Description |
|----------|------|-------------|
| `MaintTrackDbContext.cs` | DbContext | Main EF Core context with query filters for multi-tenancy |
| `Program.cs` | Registration | `AddDbContext<MaintTrackDbContext>`; health check uses `dbContext.Database.CanConnectAsync()` |
| **Services using _dbContext:** | | |
| ForkliftReportsQueryService | Read-only | ForkliftReports, Forklifts |
| TreatmentService | Read/Write | Treatments, AuditLogs |
| ForkliftReportService | Read/Write | Forklifts, ForkliftReports, ForkliftTreatment, ForkliftFault, ForkliftInspection, AuditLogs |
| MachineService | Read/Write | Machines |
| AnnualPlanService | Read/Write | AnnualPlans, AnnualPlanItems, MaintenanceTasks, Technicians, AuditLogs |
| MorningRoundService | Read/Write | MorningRoundReports, MorningRoundTemplateItems, Users, AuditLogs |
| MorningRoundTemplateService | Read-only | MorningRoundTemplateItems |
| ForkliftService | Read/Write | Forklifts |
| MaintenanceEntryService | Read/Write | MaintenanceEntries, Machines, AuditLogs |
| LoginService | Read-only | Users (IgnoreQueryFilters for login) |
| **Endpoints using DbContext directly:** | | |
| AnnualPlanEndpoints | Read-only | MaintenanceTasks, Technicians |

**Raw SQL:** None found. No `FromSqlRaw`, `ExecuteSqlRaw`, or `FromSql` usage.

---

## 2. Query Inventory Table

For each query the table below lists: file, class, method, type, code snippet, entities, Include/Select/AsNoTracking, and notes.

| # | File | Class | Method | Query Type | Exact Query Code | Entities | Include? | Select projection? | AsNoTracking? | N+1 risk? | Loads too much? | Inefficient? | Suggested Improvement |
|---|------|--------|--------|------------|------------------|----------|----------|--------------------|---------------|-----------|------------------|--------------|------------------------|
| 1 | ForkliftReportsQueryService.cs | ForkliftReportsQueryService | GetReportsAsync | read | `_dbContext.ForkliftReports.AsNoTracking().AsQueryable()` + Where (ForkliftNumber, Search, FromDate, ToDate, Type) + GroupBy(ForkliftId, LicenseNumber) + Select(ForkliftReportListItemDto) + OrderBy + Skip/Take + ToListAsync | ForkliftReport, Forklift, ForkliftTreatment, ForkliftFault | No (navs used in Where/GroupBy) | Yes | Yes | No | No (pagination) | Contains() may not use index; GroupBy + Sum(Count) can be heavy | Add index on ReportDate, ForkliftId; consider ILike for search |
| 2 | ForkliftReportsQueryService.cs | ForkliftReportsQueryService | GetExpiringInspectionsAsync | read | `_dbContext.Forklifts.AsNoTracking().Where(f => f.InspectionExpiryDate != null && f.InspectionExpiryDate.Value <= thresholdUtc).Select(ExpiringInspectionDto).ToListAsync()` | Forklift | No | Yes | Yes | No | No | No | Index on InspectionExpiryDate if filtered often |
| 3 | TreatmentService.cs | TreatmentService | CreateAsync | write | AddAsync(Treatment), AddAsync(AuditLog), SaveChangesAsync | Treatment, AuditLog | N/A | N/A | N/A | No | No | No | — |
| 4 | TreatmentService.cs | TreatmentService | GetAsync | read | `_dbContext.Treatments.AsNoTracking()` + Where(equipmentType, fromDate, toDate) + OrderByDescending(Date) + Skip/Take + Select(TreatmentDto) + ToListAsync | Treatment | No | Yes | Yes | No | No | No | — |
| 5 | TreatmentService.cs | TreatmentService | GetByIdAsync | read | `_dbContext.Treatments.AsNoTracking().Where(t => t.Id == id).Select(TreatmentDto).FirstOrDefaultAsync()` | Treatment | No | Yes | Yes | No | No | No | — |
| 6 | ForkliftReportService.cs | ForkliftReportService | CreateAsync (lookup) | read | `_dbContext.Forklifts.FirstOrDefaultAsync(f => f.Id == request.ForkliftId, ct)` | Forklift | No | No (entity) | No (tracked) | No | No | No | Correct: entity is updated (inspection dates) |
| 7 | ForkliftReportService.cs | ForkliftReportService | CreateAsync | write | AddAsync(ForkliftReport with Treatments/Faults), AddAsync(AuditLog), SaveChangesAsync | ForkliftReport, ForkliftTreatment, ForkliftFault, AuditLog, Forklift (update) | N/A | N/A | N/A | No | No | No | — |
| 8 | ForkliftReportService.cs | ForkliftReportService | GetAsync | read | `_dbContext.ForkliftReports.AsNoTracking().OrderByDescending(r => r.ReportDate).Skip/Take.Select(r => ForkliftReportDto with Treatments/Faults/Inspections).ToListAsync()` | ForkliftReport, ForkliftTreatment, ForkliftFault, ForkliftInspection | No (projection) | Yes | Yes | No | No | No | — |
| 9 | ForkliftReportService.cs | ForkliftReportService | GetByIdAsync | read | Same as above with Where(r => r.Id == id).FirstOrDefaultAsync() | ForkliftReport, ForkliftTreatment, ForkliftFault, ForkliftInspection | No | Yes | Yes | No | No | No | — |
| 10 | MachineService.cs | MachineService | GetAllAsync | read | `_dbContext.Machines.AsNoTracking().Where(m => m.IsActive).OrderBy(m => m.Name).Select(MachineDto).ToListAsync()` | Machine | No | Yes | Yes | No | Yes (no pagination) | Yes for large tenant | Add pagination or keep if machine list is small |
| 11 | MachineService.cs | MachineService | CreateAsync | write | AddAsync(Machine), SaveChangesAsync | Machine | N/A | N/A | N/A | No | No | No | — |
| 12 | AnnualPlanEndpoints.cs | AnnualPlanEndpoints | GET /api/v1/annual-plans/tasks | read | `db.MaintenanceTasks.AsNoTracking().OrderBy(t => t.Type).ThenBy(t => t.OrderIndex).Select(t => new { Id, Type, TranslationKey, OrderIndex }).ToListAsync()` | MaintenanceTask | No | Yes | Yes | No | Yes (no pagination) | Low (tasks table usually small) | Optional: pagination if many tasks |
| 13 | AnnualPlanEndpoints.cs | AnnualPlanEndpoints | GET /api/v1/annual-plans/technicians | read | `db.Technicians.AsNoTracking().OrderBy(t => t.TranslationKey).Select(t => new { Id, TranslationKey }).ToListAsync()` | Technician | No | Yes | Yes | No | Yes (no pagination) | Low | Optional: pagination |
| 14 | AnnualPlanService.cs | AnnualPlanService | CreateOrUpdateAsync (tasks validation) | read | `_dbContext.MaintenanceTasks.Where(...).Select(t => t.Id).ToListAsync(ct)` | MaintenanceTask | No | Yes (Id only) | No | No | No | No | Use AsNoTracking() for read-only validation |
| 15 | AnnualPlanService.cs | AnnualPlanService | CreateOrUpdateAsync (technicians validation) | read | `_dbContext.Technicians.Where(...).Select(t => t.Id).ToListAsync(ct)` | Technician | No | Yes | No | No | No | No | Use AsNoTracking() for read-only validation |
| 16 | AnnualPlanService.cs | AnnualPlanService | CreateOrUpdateAsync (existing plan) | read | `_dbContext.AnnualPlans.Include(p => p.Items).ThenInclude(i => i.Dates).Include(p => p.Items).ThenInclude(i => i.Execution!).ThenInclude(e => e.Workers).FirstOrDefaultAsync(p => ...)` | AnnualPlan, AnnualPlanItem, PreventivePlanDate, SummerPlanExecution, SummerPlanWorker | Yes (Items, Dates, Execution, Workers) | No | No (tracked for Remove) | No | Loads full graph | Intentional for delete | — |
| 17 | AnnualPlanService.cs | AnnualPlanService | CreateOrUpdateAsync | write | Remove(existing), AddAsync(plan), AddAsync(AuditLog), SaveChangesAsync | AnnualPlan, AuditLog | N/A | N/A | N/A | No | No | No | — |
| 18 | AnnualPlanService.cs | AnnualPlanService | GetByYearAsync | read | `_dbContext.AnnualPlans.AsNoTracking().Where(...).Select(AnnualPlanDto with Items, Task, Dates, Execution, Workers, Technician).FirstOrDefaultAsync()` | AnnualPlan, AnnualPlanItem, MaintenanceTask, PreventivePlanDate, SummerPlanExecution, SummerPlanWorker, Technician | No (projection) | Yes | Yes | No | No | No | — |
| 19 | AnnualPlanService.cs | AnnualPlanService | GetListAsync | read | Same pattern as above, no year/type filter, OrderByDescending(Year).ThenBy(Type).Select(...).ToListAsync() | Same | No | Yes | Yes | No | Yes (all plans for tenant) | Yes if many plans | Add pagination for list |
| 20 | MorningRoundService.cs | MorningRoundService | GetByIdAsync | read | `from r in _dbContext.MorningRoundReports.AsNoTracking() join u in _dbContext.Users ... where r.Id == id select new { ... }.FirstOrDefaultAsync()` | MorningRoundReport, User | No (join) | Yes | Yes | No | No | No | — |
| 21 | MorningRoundService.cs | MorningRoundService | GetAsync | read | Same join + Where(FromDate, ToDate, PerformedByUserId) + OrderByDescending(ReportDate) + Skip/Take + ToListAsync; then in-memory Select to MorningRoundDto | MorningRoundReport, User | No | Yes (then in-memory map) | Yes | No | No | No | — |
| 22 | MorningRoundService.cs | MorningRoundService | CreateAsync (validation) | read | `_dbContext.MorningRoundTemplateItems.AsNoTracking().Where(x => parsedIds.Contains(x.Id) && x.IsActive).Select(x => x.Id).ToListAsync(ct)` | MorningRoundTemplateItem | No | Yes | Yes | No | No | No | — |
| 23 | MorningRoundService.cs | MorningRoundService | CreateAsync | write | AddAsync(report), AddAsync(auditLog), SaveChangesAsync | MorningRoundReport, AuditLog | N/A | N/A | N/A | No | No | No | — |
| 24 | MorningRoundTemplateService.cs | MorningRoundTemplateService | GetTemplateAsync | read | `_dbContext.MorningRoundTemplateItems.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.SortOrder).Select(MorningRoundTemplateItemDto).ToListAsync()` | MorningRoundTemplateItem | No | Yes | Yes | No | Yes (no pagination) | Low | — |
| 25 | ForkliftService.cs | ForkliftService | CreateAsync | write | AddAsync(Forklift), SaveChangesAsync | Forklift | N/A | N/A | N/A | No | No | No | — |
| 26 | ForkliftService.cs | ForkliftService | GetAsync | read | `_dbContext.Forklifts.AsNoTracking().OrderBy(f => f.LicenseNumber).Select(ForkliftDto).ToListAsync()` | Forklift | No | Yes | Yes | No | Yes (no pagination) | Yes for many forklifts | Add pagination |
| 27 | ForkliftService.cs | ForkliftService | GetByIdAsync | read | `_dbContext.Forklifts.AsNoTracking().Where(f => f.Id == id).Select(ForkliftDto).FirstOrDefaultAsync()` | Forklift | No | Yes | Yes | No | No | No | — |
| 28 | ForkliftService.cs | ForkliftService | UpdateAsync | read+write | `_dbContext.Forklifts.FirstOrDefaultAsync(f => f.Id == id)` (tracked), then property updates, SaveChangesAsync | Forklift | No | No | No | No | No | No | — |
| 29 | MaintenanceEntryService.cs | MaintenanceEntryService | CreateAsync (validation) | read | `_dbContext.Machines.AnyAsync(m => m.Id == request.MachineId && m.TenantId == tenantId, ct)` | Machine | No | N/A | No | No | No | No | AsNoTracking not needed for AnyAsync |
| 30 | MaintenanceEntryService.cs | MaintenanceEntryService | CreateAsync | write | AddAsync(entry), AddAsync(AuditLog), SaveChangesAsync | MaintenanceEntry, AuditLog | N/A | N/A | N/A | No | No | No | — |
| 31 | MaintenanceEntryService.cs | MaintenanceEntryService | UpdateAsync | read+write | `_dbContext.MaintenanceEntries.FirstOrDefaultAsync(e => e.Id == id)` (tracked), AddAsync(AuditLog), SaveChangesAsync | MaintenanceEntry, AuditLog | No | No | No | No | No | No | — |
| 32 | MaintenanceEntryService.cs | MaintenanceEntryService | DeleteAsync | read+write | `_dbContext.MaintenanceEntries.FirstOrDefaultAsync(e => e.Id == id)` (tracked), Remove(entry), AddAsync(AuditLog), SaveChangesAsync | MaintenanceEntry, AuditLog | No | No | No | No | No | No | — |
| 33 | MaintenanceEntryService.cs | MaintenanceEntryService | GetAsync | read | `from e in _dbContext.MaintenanceEntries.AsNoTracking() join m in _dbContext.Machines.AsNoTracking() on e.MachineId equals m.Id` + Where + OrderByDescending + Skip/Take + Select(MaintenanceEntryDto) + ToListAsync | MaintenanceEntry, Machine | No (join) | Yes | Yes | No | No | No | Index on Date, MachineId for filters |
| 34 | MaintenanceEntryService.cs | MaintenanceEntryService | GetByIdAsync | read | Same join, where e.Id == id, Select(MaintenanceEntryDto).FirstOrDefaultAsync() | MaintenanceEntry, Machine | No | Yes | Yes | No | No | No | — |
| 35 | LoginService.cs | LoginService | AuthenticateAsync | read | `_dbContext.Users.IgnoreQueryFilters().Where(u => u.IsActive && (EF.Functions.ILike(u.Username, ...) \|\| EF.Functions.ILike(u.Email, ...))).FirstOrDefaultAsync()` | User | No | No (returns entity) | No | No | No | No | Add AsNoTracking(); we only read and verify password |
| 36 | Program.cs | (inline) | GET /health | read | `dbContext.Database.CanConnectAsync()` | None (connection only) | N/A | N/A | N/A | No | No | No | — |

---

## 3. Potential Performance Problems

### 3.1 Missing AsNoTracking on read-only queries

| Location | Query | Recommendation |
|----------|--------|----------------|
| **AnnualPlanService.CreateOrUpdateAsync** | Validation: `MaintenanceTasks.Where(...).Select(t => t.Id).ToListAsync` | Add `.AsNoTracking()` before Where; query is read-only validation. |
| **AnnualPlanService.CreateOrUpdateAsync** | Validation: `Technicians.Where(...).Select(t => t.Id).ToListAsync` | Add `.AsNoTracking()`; read-only validation. |
| **LoginService.AuthenticateAsync** | `_dbContext.Users.IgnoreQueryFilters().Where(...).FirstOrDefaultAsync()` | Add `.AsNoTracking()`; user is only read for password verification, not updated. |

### 3.2 Includes that may be unnecessary

| Location | Include | Note |
|----------|--------|------|
| **AnnualPlanService.CreateOrUpdateAsync** | Include Items → Dates, Items → Execution → Workers | Required for cascading Remove(existing); no change suggested. |

All other read paths use Select projection instead of Include; no unnecessary Includes found.

### 3.3 Queries returning full entities instead of DTO projection

| Location | Issue | Recommendation |
|----------|--------|----------------|
| **ForkliftReportService.CreateAsync** | `FirstOrDefaultAsync(f => f.Id == request.ForkliftId)` returns full Forklift | Intentional: entity is updated (inspection dates). OK. |
| **ForkliftService.UpdateAsync** | `FirstOrDefaultAsync(f => f.Id == id)` returns full Forklift | Intentional: entity is updated. OK. |
| **MaintenanceEntryService.UpdateAsync / DeleteAsync** | FirstOrDefaultAsync returns full MaintenanceEntry | Intentional: update/delete. OK. |
| **LoginService.AuthenticateAsync** | Returns full User (including PasswordHash) | Required for password verification. Consider projecting to a small type if you ever need to avoid loading large fields. |

No clear “return full entity where DTO would suffice” in read-only APIs; list/get endpoints use projection.

### 3.4 Possible N+1 query patterns

| Location | Assessment |
|----------|------------|
| **ForkliftReportsQueryService.GetReportsAsync** | No N+1: single query with GroupBy/Select; navigation used in LINQ is translated to JOINs. |
| **ForkliftReportService.GetAsync / GetByIdAsync** | No N+1: Select with nested collections is one query (EF translates to JOINs). |
| **AnnualPlanService.GetByYearAsync / GetListAsync** | No N+1: single Select with nested collections. |
| **MorningRoundService.GetAsync** | No N+1: single join query, then in-memory DTO mapping. |

No N+1 patterns identified in the current code.

### 3.5 Queries inside loops

None found. All loops (e.g. request.Faults, request.Treatments) only build in-memory entities later added in one batch before SaveChangesAsync.

### 3.6 SaveChanges calls that could be batched

| Location | Pattern | Note |
|----------|--------|------|
| TreatmentService.CreateAsync | Add treatment + Add audit + 1× SaveChangesAsync | Already single round-trip. |
| ForkliftReportService.CreateAsync | Add report (with children) + Add audit + 1× SaveChangesAsync | OK. |
| MaintenanceEntryService.UpdateAsync | Load entry, add audit, 1× SaveChangesAsync | OK. |
| MaintenanceEntryService.DeleteAsync | Load entry, Remove, Add audit, 1× SaveChangesAsync | OK. |

No batching issues; each operation uses one SaveChangesAsync per request.

### 3.7 Missing pagination on list endpoints

| Endpoint / Method | Service/Method | Recommendation |
|-------------------|----------------|----------------|
| GET /api/v1/machines | MachineService.GetAllAsync | Add optional pageNumber/pageSize; or document that machine list is expected to be small. |
| GET /api/v1/forklifts | ForkliftService.GetAsync | Add pagination; could load many forklifts. |
| GET /api/v1/annual-plans (list path) | AnnualPlanService.GetListAsync | Add pagination when year/type not both provided; returns all plans for tenant. |
| GET /api/v1/annual-plans/tasks | AnnualPlanEndpoints (DbContext) | Optional pagination if task count grows. |
| GET /api/v1/annual-plans/technicians | AnnualPlanEndpoints (DbContext) | Optional pagination. |
| GET /api/morning-round/template | MorningRoundTemplateService.GetTemplateAsync | Usually small; optional pagination. |

### 3.8 Heavy filtering/sorting done in memory instead of DB

| Location | Assessment |
|----------|------------|
| **MorningRoundService.GetAsync** | Filtering (FromDate, ToDate, PerformedByUserId) and OrderByDescending(ReportDate) are in the IQueryable; only DTO construction is in-memory after ToListAsync. OK. |
| **MaintenanceEntryService.GetAsync** | Filtering and ordering in query; EF.Functions.ILike in DB. OK. |

No problematic in-memory filtering/sorting identified.

### 3.9 Possible missing indexes (based on filtering columns)

| Table | Columns used in Where/OrderBy/Join | Suggested index |
|-------|-------------------------------------|------------------|
| ForkliftReports | ReportDate, ForkliftId, (Forklift.LicenseNumber) | (TenantId, ReportDate), (ForkliftId, ReportDate) |
| Forklifts | InspectionExpiryDate | (TenantId, InspectionExpiryDate) where expiry queries are common |
| Treatments | TreatmentDate, EquipmentType, TenantId | Existing (TenantId, EquipmentType), (TenantId, TreatmentDate) in DbContext |
| MaintenanceEntries | Date, MachineId | (TenantId, Date), (MachineId, Date) |
| MorningRoundReports | ReportDate, PerformedByUserId | (TenantId, ReportDate) |
| AnnualPlans | TenantId, Year, Type | Unique index (TenantId, Year, Type) already configured |

Verify existing migrations for Treatment, MorningRoundReport, MaintenanceEntry, ForkliftReport; add composite indexes above if not present.

---

## 4. Endpoints → Database Query Mapping

| Endpoint | HTTP | Handler / Service | DB queries triggered |
|----------|------|-------------------|------------------------|
| /health | GET | Program.cs inline | `Database.CanConnectAsync()` (no entity query) |
| /api/v1/machines | GET | MachinesEndpoints → IMachineService.GetAllAsync | MachineService.GetAllAsync: Machines read (AsNoTracking, Select DTO) |
| /api/v1/machines | POST | MachinesEndpoints → IMachineService.CreateAsync | MachineService.CreateAsync: Add Machine, SaveChangesAsync |
| /api/v1/treatments | GET | TreatmentEndpoints → ITreatmentService.GetAsync | TreatmentService.GetAsync: Treatments read (AsNoTracking, Where, OrderBy, Skip/Take, Select DTO) |
| /api/v1/treatments/{id} | GET | TreatmentEndpoints → ITreatmentService.GetByIdAsync | TreatmentService.GetByIdAsync: Treatments read (AsNoTracking, Where Id, Select DTO) |
| /api/v1/treatments | POST | TreatmentEndpoints → ITreatmentService.CreateAsync | TreatmentService.CreateAsync: Add Treatment, Add AuditLog, SaveChangesAsync |
| /api/v1/forklifts | GET | ForkliftEndpoints → IForkliftService.GetAsync | ForkliftService.GetAsync: Forklifts read (AsNoTracking, OrderBy, Select DTO) |
| /api/v1/forklifts/{id} | GET | ForkliftEndpoints → IForkliftService.GetByIdAsync | ForkliftService.GetByIdAsync: Forklifts read (AsNoTracking, Where Id, Select DTO) |
| /api/v1/forklifts | POST | ForkliftEndpoints → IForkliftService.CreateAsync | ForkliftService.CreateAsync: Add Forklift, SaveChangesAsync |
| /api/v1/forklifts/{id} | PUT | ForkliftEndpoints → IForkliftService.UpdateAsync | ForkliftService.UpdateAsync: Forklifts FirstOrDefaultAsync (tracked), SaveChangesAsync |
| /api/v1/forklift | GET | ForkliftEndpoints → IForkliftReportService.GetAsync | ForkliftReportService.GetAsync: ForkliftReports read (AsNoTracking, OrderBy, Skip/Take, Select DTO with nested collections) |
| /api/v1/forklift/{id} | GET | ForkliftEndpoints → IForkliftReportService.GetByIdAsync | ForkliftReportService.GetByIdAsync: ForkliftReports read (AsNoTracking, Where Id, Select DTO) |
| /api/v1/forklift | POST | ForkliftEndpoints → IForkliftReportService.CreateAsync | ForkliftReportService.CreateAsync: Forklifts FirstOrDefaultAsync (tracked, update inspection), Add ForkliftReport + children, Add AuditLog, SaveChangesAsync |
| /api/v1/reports/forklifts | GET | ReportsEndpoints → IForkliftReportsQueryService.GetAsync | ForkliftReportsQueryService.GetAsync: GetReportsAsync (ForkliftReports query with GroupBy/Select) + optionally GetExpiringInspectionsAsync (Forklifts query) |
| /api/v1/annual-plans | GET (by year+type) | AnnualPlanEndpoints → IAnnualPlanService.GetByYearAsync | AnnualPlanService.GetByYearAsync: AnnualPlans read (AsNoTracking, Where, Select DTO with Items/Task/Dates/Execution/Workers) |
| /api/v1/annual-plans | GET (list) | AnnualPlanEndpoints → IAnnualPlanService.GetListAsync | AnnualPlanService.GetListAsync: AnnualPlans read (AsNoTracking, Where, OrderBy, Select DTO, ToListAsync) |
| /api/v1/annual-plans | POST | AnnualPlanEndpoints → IAnnualPlanService.CreateOrUpdateAsync | AnnualPlanService.CreateOrUpdateAsync: MaintenanceTasks read (validation), Technicians read (validation), AnnualPlans read with Include (existing plan), Remove + Add AnnualPlan + Add AuditLog, SaveChangesAsync |
| /api/v1/annual-plans/tasks | GET | AnnualPlanEndpoints → MaintTrackDbContext | db.MaintenanceTasks.AsNoTracking().OrderBy(...).Select(...).ToListAsync() |
| /api/v1/annual-plans/technicians | GET | AnnualPlanEndpoints → MaintTrackDbContext | db.Technicians.AsNoTracking().OrderBy(...).Select(...).ToListAsync() |
| /api/morning-round | GET | MorningRoundEndpoints → IMorningRoundService.GetAsync | MorningRoundService.GetAsync: MorningRoundReports join Users (AsNoTracking), Where, OrderBy, Skip/Take, ToListAsync + in-memory DTO map |
| /api/morning-round/{id} | GET | MorningRoundEndpoints → IMorningRoundService.GetByIdAsync | MorningRoundService.GetByIdAsync: MorningRoundReports join Users (AsNoTracking), Where Id, FirstOrDefaultAsync |
| /api/morning-round | POST | MorningRoundEndpoints → IMorningRoundService.CreateAsync | MorningRoundService.CreateAsync: MorningRoundTemplateItems read (validation), Add MorningRoundReport, Add AuditLog, SaveChangesAsync |
| /api/morning-round/template | GET | MorningRoundEndpoints → IMorningRoundTemplateService.GetTemplateAsync | MorningRoundTemplateService.GetTemplateAsync: MorningRoundTemplateItems read (AsNoTracking, Where IsActive, OrderBy, Select DTO) |
| /api/maintenance | GET | MaintenanceEntryEndpoints → IMaintenanceEntryService.GetAsync | MaintenanceEntryService.GetAsync: MaintenanceEntries join Machines (AsNoTracking), Where, OrderBy, Skip/Take, Select DTO |
| /api/maintenance/{id} | GET | MaintenanceEntryEndpoints → IMaintenanceEntryService.GetByIdAsync | MaintenanceEntryService.GetByIdAsync: MaintenanceEntries join Machines (AsNoTracking), Where Id, Select DTO |
| /api/maintenance | POST | MaintenanceEntryEndpoints → IMaintenanceEntryService.CreateAsync | MaintenanceEntryService.CreateAsync: Machines.AnyAsync (validation), Add MaintenanceEntry, Add AuditLog, SaveChangesAsync |
| /api/maintenance/{id} | PUT | MaintenanceEntryEndpoints → IMaintenanceEntryService.UpdateAsync | MaintenanceEntryService.UpdateAsync: MaintenanceEntries FirstOrDefaultAsync (tracked), Add AuditLog, SaveChangesAsync |
| /api/maintenance/{id} | DELETE | MaintenanceEntryEndpoints → IMaintenanceEntryService.DeleteAsync | MaintenanceEntryService.DeleteAsync: MaintenanceEntries FirstOrDefaultAsync (tracked), Remove entry, Add AuditLog, SaveChangesAsync |
| (Login / Auth) | — | Auth flow → ILoginService.AuthenticateAsync | LoginService.AuthenticateAsync: Users (IgnoreQueryFilters, Where IsActive + ILike Username/Email), FirstOrDefaultAsync |
| /api/upload (MapUploadEndpoints) | POST | UploadEndpoints → IFileStorageService | No database queries; file storage only |

---

## 5. Summary

- **Total distinct query/operation points:** 36 (including health check and write operations).
- **Read-only queries without AsNoTracking:** 3 (AnnualPlanService validation x2, LoginService).
- **List endpoints without pagination:** Machines, Forklifts, Annual plans list, Annual plan tasks/technicians, Morning round template.
- **N+1 / in-memory filtering:** None identified.
- **Raw SQL:** None.
- **Suggested next steps:** Add AsNoTracking where appropriate, add pagination for machines/forklifts/annual-plans list, verify indexes on ReportDate, MachineId, Date, InspectionExpiryDate, and add composite indexes if missing.
