# Backend Production-Readiness Audit Report

**Project:** MaintTrack  
**Scope:** backend/ (.NET API, EF Core, auth, multi-tenant, queries, logging, validation)  
**Date:** 2026-03-15  
**Audit type:** Inspection only — no code changes.

---

## STEP 1 — Build / Static Validation

### Build

- **dotnet build:** Not completed in audit environment (command timed out). **Action:** Run `dotnet build` locally and address any errors/warnings before production.
- **Solution structure:** Clean separation (Api, Application, Domain, Infrastructure, Tests). Api references Application, Domain, Infrastructure; no circular refs observed.

### Program.cs, Middleware, Services, DbContext

| Item | Finding | Risk |
|------|---------|------|
| **Program.cs** | JWT configured with ValidateIssuer, ValidateAudience, ValidateLifetime, ValidateIssuerSigningKey. Issuer/Audience from config. Key from `JwtOptions.Key`. | Low if key is overridden in production. |
| **Program.cs** | Single CORS policy named `"dev"` with origins `localhost:3000`, `localhost:5173`, `localhost:4173`, AllowCredentials. | **High:** No production CORS; non-localhost origins will be rejected. |
| **Program.cs** | `UseCors("dev")` applied unconditionally. | Production must register a separate CORS policy or app will not accept browser requests from production origin. |
| **Middleware order** | ErrorHandling → (Swagger in Dev) → HTTPS → CORS → Authentication → TenantMiddleware → Authorization. | Correct. |
| **Health** | `GET /health` uses `DbContext.CanConnectAsync()`; no auth. No tenant scope used. | OK for health checks. |
| **DbContext** | Tenant from `ITenantContext` (set by TenantMiddleware from JWT). No `TenantId` from client. | Good. |

### Warnings

- Static analysis was not run (no build output). Recommend: `dotnet build` and fix all warnings; run nullable and security analyzers if enabled.

---

## STEP 2 — Security Audit

### 1. JWT / Authentication

| Finding | Severity | Location | Recommendation |
|---------|----------|----------|-----------------|
| Token validation | OK | Program.cs | ValidateIssuer, ValidateAudience, ValidateLifetime, ValidateIssuerSigningKey all true. |
| Signing key | **Medium** | appsettings.json has `"Key": "CHANGE_ME_SUPER_SECRET_KEY"` | **Block production** until key is set via env or secure config (e.g. `Jwt:Key`). Never commit production key. |
| Expiration | OK | AuthEndpoints.cs | 8-hour expiry. No refresh token. |
| Clock skew | OK | Not set; defaults to 5 min. | Acceptable. |
| Issuer/Audience | OK | From JwtOptions. | Ensure production appsettings/env set correct Issuer/Audience. |
| Refresh token | Low | N/A | No refresh; users re-login after 8h. Document for UX. |

### 2. Authorization

| Finding | Severity | Location | Recommendation |
|---------|----------|----------|-----------------|
| Auth endpoints | OK | AuthEndpoints: login public; secure-test RequireAuthorization(). | Correct. |
| API endpoints | OK | All data endpoints use `.RequireAuthorization()`. | Good. |
| Role-based | **Medium** | MachinesEndpoints.cs: `RequireRole("SuperAdmin", "FactoryManager")` | Domain has `UserRole`: SuperAdmin, Manager, Worker. If DB/claims use `"Manager"` not `"FactoryManager"`, create machine will be denied. Align policy with actual role names (e.g. `"Manager"`) or add `FactoryManager` to enum and DB. |
| Maintenance update/delete | **Medium** | MaintenanceEntryService: Update checks creator or SuperAdmin; Delete checks SuperAdmin. Endpoints do not catch `UnauthorizedAccessException`. | UnauthorizedAccessException bubbles to ErrorHandlingMiddleware → 500. Handle in endpoint or middleware and return 403 Forbidden. |
| Health | OK | /health unauthenticated. | Acceptable. |

### 3. Multi-Tenant Isolation

| Finding | Severity | Location | Recommendation |
|---------|----------|----------|-----------------|
| Tenant source | OK | TenantMiddleware sets TenantId from JWT claim only. No client input. | Good. |
| Query filters | OK | MaintTrackDbContext applies `HasQueryFilter` on Users, Machines, MorningRoundReports, MorningRoundTemplateItems, AuditLogs, Treatments, Forklifts, ForkliftReports, AnnualPlans, MaintenanceTasks, Technicians; child entities (ForkliftTreatment, ForkliftFault, ForkliftInspection) filter via Report.TenantId. | Correct. |
| Login | OK | LoginService uses `IgnoreQueryFilters()` only for user lookup by username/email; tenant comes from user entity into JWT. | Correct. |
| Create/Update | OK | Create flows (Machine, Forklift, Treatment, etc.) set `TenantId = _tenantContext.TenantId.Value` and check for null. ForkliftReport Create validates ForkliftId via DbContext (filtered by tenant). | Good. |
| Raw SQL | OK | No FromSqlRaw/ExecuteSqlRaw found. | No raw-SQL tenant bypass. |
| AnnualPlanItem / child entities | OK | No tenant column; access only via parent Plan (filtered). | OK. |

### 4. Input Validation

| Finding | Severity | Location | Recommendation |
|---------|----------|----------|-----------------|
| Request DTOs | **Medium** | CreateMachineRequest, CreateForkliftRequest, CreateTreatmentRequest, etc. have no `[Required]`, `[StringLength]`, `[Range]` or FluentValidation. | Add data annotations or FluentValidation; reject invalid input with 400 and clear messages. |
| Duplicate checks | Low | TreatmentEndpoint checks `Cost < 0`; TreatmentService also checks. ForkliftReportService validates RepairCost, inspection dates. | Prefer single place (e.g. validator or service). |
| Enum validation | Low | CreateTreatmentRequest uses EquipmentType, TreatmentType (enums). Invalid values can cause model-binding issues. | Validate enum values explicitly if needed. |
| ForkliftReportsQuery | Low | ForkliftNumber, Search, Type, ExpiryDays from query. EF parameterizes; no SQL injection. | Optional: constrain ExpiryDays range and Type allowed values. |
| File upload | OK | MaintenanceTasksController: size 5MB, content-type whitelist. LocalFileStorageService: content-type and path sanitization. | Good. |

### 5. Secrets / Config

| Finding | Severity | Location | Recommendation |
|---------|----------|----------|-----------------|
| JWT key default | **High** | appsettings.json: `"Key": "CHANGE_ME_SUPER_SECRET_KEY"` | **Block production** if this value is used. Use env var or secrets manager. |
| Connection string | OK | Prefers `MAINTTRACK_DB_CONNECTION` env; falls back to config. appsettings.Development.json has dev connection string. | Ensure production does not use Development config; set env in production. |
| AllowedHosts | Low | `"*"` in appsettings.json. | Consider restricting in production. |

### 6. CORS / Cookies / Auth Headers

| Finding | Severity | Location | Recommendation |
|---------|----------|----------|-----------------|
| CORS policy | **High** | Only `"dev"` policy with localhost origins. | Add production CORS policy (e.g. named `"Production"`) with specific front-end origin(s); use `RequireCredentials()` only if needed and origin is exact. |
| AllowAnyOrigin | OK | Not used. | Good. |
| Credentials | OK | Dev uses AllowCredentials with specific origins. | Keep production origins explicit when using credentials. |

### 7. Error Handling

| Finding | Severity | Location | Recommendation |
|---------|----------|----------|-----------------|
| ErrorHandlingMiddleware | OK | Returns generic message and traceId; logs full exception. No stack trace in response. | Good. |
| Service exceptions | Low | Many services throw InvalidOperationException with business messages. Some endpoints catch and map to 400/404; others do not. | Consistently map InvalidOperationException/UnauthorizedAccessException to 400/403/404 where appropriate. |
| Maintenance Update/Delete | **Medium** | UnauthorizedAccessException → 500. | Return 403; see Authorization section. |

---

## STEP 3 — Query / Database Audit

### Positive

- **ForkliftReportsQueryService:** Uses `AsNoTracking()`, projection (no full-entity load for list).
- **ForkliftService:** GetAsync/GetByIdAsync use `AsNoTracking()` and projection.
- **ForkliftReportService:** GetAsync/GetByIdAsync use `AsNoTracking()` and projection.
- **TreatmentService:** GetAsync/GetByIdAsync use `AsNoTracking()` and projection.
- **MorningRoundService:** GetAsync/GetByIdAsync use `AsNoTracking()`, pagination (Skip/Take), join for DisplayName.
- **MaintenanceEntryService:** GetAsync uses join, pagination, projection; GetByIdAsync projection.
- **AnnualPlanService:** GetByYearAsync/GetListAsync use `AsNoTracking()` and projection.
- **MachineService:** Uses projection in GetAllAsync but **no** AsNoTracking (tracking not needed for read-only list).

### Findings

| # | File | Method | Issue | Likely SQL / Impact | Recommendation |
|---|------|--------|--------|----------------------|----------------|
| 1 | MachineService.cs | GetAllAsync | No `AsNoTracking()`. | Unnecessary change tracking for read-only list. | Add `.AsNoTracking()` before filtering/ordering. |
| 2 | ForkliftReportsQueryService.cs | GetReportsAsync | No pagination; returns all report groups for tenant. | Single large result set; can be heavy with many reports. | Add PageNumber/PageSize to ForkliftReportsQuery and apply Skip/Take; consider max page size. |
| 3 | ForkliftReportsQueryService.cs | GetReportsAsync | Uses `r.Forklift.LicenseNumber`, `r.Treatments.Any()`, `r.Faults.Any()`, `GroupBy` with aggregates. | JOINs and correlated subqueries; complex. May be slow with large data. | Ensure indexes on ForkliftReports(TenantId, ReportDate), Forklifts(TenantId); consider materialized view or summary table for heavy dashboards. |
| 4 | ForkliftReportsQueryService.cs | GetExpiringInspectionsAsync | No limit on number of results. | Returns all expiring forklifts for tenant. | Cap result count or add pagination if list can be large. |
| 5 | ForkliftReportService.cs | GetAsync | Returns full list of all reports for tenant; no pagination. | Loads all reports with treatments/faults/inspections. | Add pagination (Skip/Take) and optional date/range filters. |
| 6 | TreatmentService.cs | GetAsync | No pagination. | All treatments for tenant in date range. | Add pagination and/or enforce max result set. |
| 7 | MachineService.cs | GetAllAsync | Read-only list but entities tracked. | Extra memory and tracking overhead. | AsNoTracking (see 1). |
| 8 | AnnualPlanService.cs | GetListAsync | No pagination; returns all plans for tenant. | Usually small (plans per year/type); lower risk. | Add pagination if many plans per tenant. |
| 9 | AnnualPlanService.cs | CreateOrUpdateAsync | Uses `Include` + `ThenInclude` for full graph then `Remove(existing)`. | Loads full plan with items, dates, execution, workers. | Acceptable for replace semantics; ensure tenant filter is applied (it is via FirstOrDefault on AnnualPlans). |
| 10 | MaintenanceEntryService.cs | GetAsync | `EF.Functions.ILike(x.Entry.Description, $"%{search}%")` with request.Search. | Parameterized; no SQL injection. Index on Description unlikely. | Optional: limit search length; consider full-text if needed. |

### N+1 / Client Evaluation

- No obvious N+1: list endpoints use single query with projection or join.
- ForkliftReportService.GetAsync uses nested Select for Treatments/Faults/Inspections — EF translates to single query with joins/subqueries.
- No evidence of client-side evaluation in reviewed code; all reviewed LINQ uses server-translatable constructs.

### Cancellation

- Most service methods accept `CancellationToken ct` and pass it to EF. Endpoints generally pass `cancellationToken` from the request. Acceptable.

### Tenant Leakage in Queries

- No query found that bypasses tenant: all use filtered sets or explicit tenant checks. LoginService IgnoreQueryFilters is limited to user lookup; tenant in token comes from user entity.

---

## STEP 4 — API Design Audit

| Finding | Severity | Location | Recommendation |
|---------|----------|----------|-----------------|
| Route consistency | **Medium** | Most APIs under `/api/v1/`; Maintenance, Maintenance Tasks, and Morning Round under `/api/maintenance`, `/api/maintenance-tasks`, `/api/morning-round` (no version). | Standardize on `/api/v1/` for versioned APIs and document. |
| Maintenance Tasks route | Low | MaintenanceTasksController uses `/api/maintenance-tasks`. | Consider `/api/v1/maintenance-tasks` for consistency. |
| Thin endpoints | OK | Endpoints delegate to application/infrastructure services. | Good. |
| DTOs | OK | Responses use DTOs; domain entities not exposed. | Good. |
| Status codes | OK | 200/201/204/400/401/404 used. Update/Delete sometimes rely on exception → 500. | Map service exceptions to 403/404 consistently. |
| CreateOrUpdateAnnualPlan | Low | Returns 200 with `{ id }`; semantics are replace. | Consider 201 for create, 200 for replace; document behavior. |
| Maintenance Delete | **Medium** | DeleteAsync throws when entry is null OR user is not SuperAdmin. Caller gets same exception for "not found" and "forbidden". | Check entry null first and return 404 (or equivalent); then check role and return 403. |
| Validation at API | Low | Most validation in services. | Add request validation (attributes or FluentValidation) at API boundary. |

---

## STEP 5 — Reliability / Production Audit

| Finding | Severity | Location | Recommendation |
|---------|----------|----------|-----------------|
| Logging | Low | ErrorHandlingMiddleware logs unhandled exceptions. No structured logging of auth failures, tenant resolution, or critical business actions. | Add logging for login success/failure, tenant resolution failures, and key create/update/delete operations (with tenant/user, not sensitive data). |
| Audit trail | OK | AuditLog written for forklift report, treatment, maintenance, morning round, annual plan creation/update. | Consider audit for update/delete where missing (e.g. machine, forklift, treatment updates). |
| Retry / resilience | Low | No Polly or database retry for transient failures. | Consider retry for DbContext SaveChanges and critical external calls. |
| Health | OK | /health checks DB connectivity. | Consider separate readiness (DB + dependencies) vs liveness. |
| Timeouts | Low | No explicit command timeout on DbContext. | Rely on default or set sensible command timeout for long-running queries. |
| Transactions | OK | Single SaveChanges per use case; no multi-DbContext transactions. | Adequate for current design. |
| Concurrency | Low | No row version or concurrency token on entities. | If same entity is edited by two users, last write wins. Add optimistic concurrency where needed. |
| File handling | OK | LocalFileStorageService path sanitization and prefix check in DeleteAsync reduce path traversal risk. | Ensure wwwroot/uploads is not served with execute permissions. |
| Startup | Low | JWT config validated at startup (throw if missing). Connection string throw if not set. | Good. Add CORS policy validation for production (e.g. non-empty origins). |

---

## STEP 6 — Architecture Audit

| Finding | Severity | Location | Recommendation |
|---------|----------|----------|-----------------|
| Layers | OK | Api → Application (interfaces, DTOs, requests) → Infrastructure (services, DbContext); Domain entities in Domain. | Clear. |
| DbContext in API | Low | AnnualPlanEndpoints inject MaintTrackDbContext for tasks/technicians. | Prefer query services (e.g. IAnnualPlanTaskQuery) to keep API layer free of EF. |
| Service boundaries | OK | One primary service per aggregate/feature. | Good. |
| Duplicate validation | Low | Cost/RepairCost checked in endpoint and service in places. | Centralize in validator or service. |
| Cross-cutting | OK | Tenant and user context in middleware and scoped services; error handling in middleware. | Good. |
| Scaling | Low | Monolithic API; no caching. | For many factories/users, add caching (e.g. template items, tasks list) and query pagination as above. |

---

## STEP 7 — Final Report

### A. Critical Issues (Must Fix Before Production)

| # | Severity | File / Area | Explanation | Recommended Fix | Blocks Production? |
|---|----------|-------------|-------------|------------------|--------------------|
| 1 | **Critical** | appsettings.json / Config | Default JWT key `CHANGE_ME_SUPER_SECRET_KEY` must not be used in production. | Set `Jwt:Key` via environment or secure secret store; fail startup if key is default or weak. | **Yes** |
| 2 | **Critical** | Program.cs | Only CORS policy is `"dev"` (localhost). Production front-end origin not allowed. | Add production CORS policy with explicit origin(s); choose policy by environment (e.g. IsProduction). | **Yes** |

### B. High-Risk Scalability / Performance Issues

| # | Severity | File | Method / Endpoint | Explanation | Recommended Fix | Blocks? |
|---|----------|------|-------------------|-------------|------------------|--------|
| 3 | High | ForkliftReportsQueryService.cs | GetReportsAsync | Unbounded list; can return thousands of report groups. | Add pagination (PageNumber, PageSize) and enforce max page size. | No (degrades under load) |
| 4 | High | ForkliftReportService.cs | GetAsync | All reports for tenant loaded with children; no pagination. | Add pagination and optional filters. | No (degrades under load) |
| 5 | High | TreatmentService.cs | GetAsync | All treatments in range returned; no pagination. | Add pagination. | No (degrades under load) |

### C. Medium-Risk Issues

| # | Severity | File / Area | Explanation | Recommended Fix | Blocks? |
|---|----------|-------------|-------------|------------------|--------|
| 6 | Medium | MachinesEndpoints.cs | RequireRole("SuperAdmin", "FactoryManager") may not match DB/claims (e.g. "Manager"). | Align role names: use same string as stored in DB and in UserRole if parsed (e.g. "Manager"), or add FactoryManager consistently. | No (can block create machine if roles mismatch) |
| 7 | Medium | MaintenanceEntryEndpoints.cs, MaintenanceEntryService.cs | Update/Delete throw UnauthorizedAccessException; middleware returns 500. Delete treats "not found" same as "forbidden". | Catch UnauthorizedAccessException in endpoints and return 403. In DeleteAsync, if entry is null return 404 (or equivalent) before role check. | No (wrong status code / UX) |
| 8 | Medium | Request DTOs | No [Required]/[StringLength]/[Range] or FluentValidation. | Add validation at API boundary; return 400 with clear messages. | No (bad requests may hit services) |
| 9 | Medium | MaintenanceEntryService.cs | DeleteAsync(entry is null \|\| !SuperAdmin) throws same message. | First check entry is null → throw "not found" or return; then check SuperAdmin → 403. | No (UX) |
| 10 | Medium | API routes | /api/maintenance, /api/morning-round, /api/maintenance-tasks not under /api/v1. | Move to /api/v1/... for consistency and versioning. | No (consistency) |

### D. Low-Risk Improvements

- Add `AsNoTracking()` in MachineService.GetAllAsync.
- Cap or paginate GetExpiringInspectionsAsync (ForkliftReportsQueryService).
- Add production CORS policy name to config; validate at startup.
- Restrict AllowedHosts in production.
- Add structured logging for auth and critical actions.
- Consider retry policy for DB and critical operations.
- Consider optimistic concurrency (e.g. row version) for key entities.
- Use query services for tasks/technicians in AnnualPlanEndpoints instead of direct DbContext.

### E. Query Optimization Summary

- **AsNoTracking:** Add in MachineService.GetAllAsync.
- **Pagination:** Add for ForkliftReportsQueryService.GetReportsAsync, ForkliftReportService.GetAsync, TreatmentService.GetAsync; consider for GetExpiringInspectionsAsync and GetListAsync (annual plans).
- **Indexes:** Ensure (TenantId, ReportDate) and (TenantId, …) exist for main tables (present in DbContext for several entities).
- **Heavy report query:** ForkliftReportsQueryService.GetReportsAsync uses GroupBy and subqueries; monitor and consider summary table or caching for large tenants.

### F. Security Findings Summary

- **Critical:** Production JWT key must be set and not default; production CORS must be configured.
- **Good:** Tenant from JWT only; query filters applied; no raw SQL; file upload validated; error responses do not leak stack traces.
- **Fix:** Align role names for machine create; return 403 for unauthorized maintenance update/delete; validate request DTOs.

### G. Manual Tests Still Required

1. **Build:** Run `dotnet build` and fix all warnings.
2. **Auth:** Log in with valid/invalid credentials; call protected endpoint without token and with expired token; verify 401.
3. **Tenant:** With two tenants, ensure user A cannot see or modify tenant B data (machines, reports, treatments, etc.) via API.
4. **Roles:** As Manager (or FactoryManager per DB), verify machine create; as Worker verify rejection; verify maintenance update/delete as creator vs non-creator and SuperAdmin.
5. **CORS:** From production front-end origin, verify preflight and actual requests succeed after adding production CORS.
6. **Pagination:** Verify maintenance and morning round list pagination; after adding pagination to reports/treatments, verify.
7. **Validation:** Send invalid requests (missing required fields, negative cost, invalid enum) and verify 400 and messages.
8. **Health:** Call /health and verify response and DB down behavior.

### H. Recommended Fix Order

1. **Before any production deploy:** Set production JWT key (env/secret); add and use production CORS policy.
2. **Before go-live:** Align machine-create role names; fix maintenance update/delete to return 403 and fix delete "not found" vs "forbidden"; add request validation for key DTOs.
3. **Soon after:** Add pagination to forklift reports overview, forklift report list, and treatments; add AsNoTracking in MachineService.GetAllAsync.
4. **Next:** Logging improvements; optional retry and concurrency; route versioning consistency.

---

## Safe to Ship Now?

**No.** Do not ship to production until:

1. **JWT key** is set from a secure source (not default in config).
2. **CORS** is configured for the production front-end origin(s).

After those two fixes, the backend is **conditionally** safe to ship for a controlled rollout, with the understanding that:

- Unauthorized maintenance update/delete will return 500 instead of 403 until fixed.
- Role name mismatch may block machine creation for some roles until fixed.
- Several list endpoints may degrade with large data until pagination is added.

---

## Top 5 Fixes to Do First

1. **Set production JWT key** (environment or secret store) and ensure app does not start with default key in production.
2. **Add production CORS policy** and use it when not in development (e.g. by environment).
3. **Return 403 for unauthorized maintenance update/delete** and fix delete to return 404 when entry is not found.
4. **Align role for machine create** (e.g. "Manager" vs "FactoryManager") so policy matches DB/claims.
5. **Add request validation** (data annotations or FluentValidation) for Create* and Update* DTOs and return 400 with clear messages.

---

## Especially Dangerous Tenant / Auth / Query Issues

- **Tenant:** None identified. Tenant is derived only from JWT; query filters are applied; create operations set tenant from context; no client-supplied tenant id.
- **Auth:** Default JWT key in config is the main auth risk; fix before production. Role name mismatch (FactoryManager vs Manager) can lock out legitimate users from creating machines.
- **Query:** No tenant leakage found. Highest risk is **unbounded list endpoints** (forklift reports overview, report list, treatments) under heavy data or many tenants — add pagination and caps to avoid timeouts and memory pressure.

---

*End of report. No code was modified; findings are recommendations only.*
