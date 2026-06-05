# Pre-Production Audit Report — React + Vite + TypeScript Frontend

**Date:** March 15, 2025  
**Scope:** Frontend only (backend out of scope)

---

## Static checks

| Check | Result |
|-------|--------|
| `npm run build` | ✅ Passed (Vite build completed in ~839ms) |
| `npx tsc --noEmit` | ✅ Passed (no type errors) |

**Notes:**
- `npm` reported: `Unknown env config "devdir"` — environment/config warning only; does not affect build.
- No typing or build risks identified from these commands.

---

## 1. Critical issues

| # | Severity | File | Explanation | Recommended fix |
|---|----------|------|-------------|-----------------|
| 1 | **Critical** | `src/App.tsx` | **Auth state vs token mismatch.** `isLoggedIn` is derived only from local React state (`username`). Token is stored in `localStorage` via `authStorage` and used by `apiFetch`. On full page reload or new tab, `username` is reset to `''`, so the user is always sent to `/login` even when a valid token exists. Users must re-login after every refresh. | Initialize auth from token on app load: e.g. on mount read `getToken()` (and optionally decode/user info). If token exists, set `username` (or equivalent) so `isLoggedIn` is true and user stays on the app. Alternatively introduce a small bootstrap that checks token and sets a single source of truth (e.g. context) before rendering routes. |
| 2 | **Critical** | `src/App.tsx` | **Logout does not clear token.** `handleLogout` only clears `username` and `userRole` and navigates to `/login`. It never calls `authService.logout()` (which calls `clearToken()`). The token remains in `localStorage`, so the session is still valid for API calls and a new “login” does not necessarily invalidate the old session on the server. | In `handleLogout`, call `authService.logout()` (or `clearToken()` from `authStorage`) before clearing state and navigating to `/login`. |
| 3 | **Critical** | `frontend/.env` | **Production API URL.** `VITE_API_URL` is set to `http://localhost:5062`. For production builds, this must be overridden (e.g. via CI/deploy env or a production `.env.production`) so the app targets the real API. | Document and configure production `VITE_API_URL` for the build (e.g. `.env.production` or deploy-time env). Ensure production builds never use the dev URL by default. |

---

## 2. Medium-risk issues

| # | Severity | File | Explanation | Recommended fix |
|---|----------|------|-------------|-----------------|
| 4 | Medium | `src/components/ReportDetailsScreen.tsx` (line 83) | **`useEffect` depends on `t`.** The data-fetching `useEffect` has dependency array `[reportId, reportType, t]`. `t` from `useLanguage()` is recreated every render, so when the user changes language the effect re-runs and refetches the same report unnecessarily. | Remove `t` from the dependency array (report data does not depend on translations). Use `[reportId, reportType]` for all three report-type effects. |
| 5 | Medium | `src/components/HomeScreen.tsx` (lines 18–29) | **Unnecessary API call on mount.** A `useEffect` runs `getForkliftReportsOverview({ inspectionExpiringSoon: true })` on every Home mount. The result is only used to surface errors via `console.error`; no state or UI uses the data. This adds latency and load for no user benefit. | Remove the “test” `useEffect` or replace it with real usage (e.g. show expiring inspections on Home and store result in state). If it was only for debugging, remove it. |
| 6 | Medium | `src/components/ForkliftReportsScreen.tsx` (lines 40–50) | **Forklifts loaded without loading state.** `loadForklifts()` runs in a `useEffect` with no loading flag. The main UI uses a single `loading` for reports; forklift list can appear to pop in after load. Errors are only logged. | Add `isLoadingForklifts` (and optionally `forkliftsError`) and set them in the forklift-loading effect. Show a loading/empty state for the forklift selector until loaded and surface errors to the user. |
| 7 | Medium | `src/components/ReportsListScreen.tsx` (lines 361–396) | **No loading indicator for report lists.** When `isLoadingMorning`, `isLoadingMaintenance`, or `isLoadingTreatments` is true, the list area shows `filteredReports.map` (empty) and the empty-state block is hidden (it only shows when `!isLoading*`). User sees a blank list with no “Loading…” feedback. | When any of these loading flags is true, show a loading message or skeleton (e.g. “Loading reports…”) in the list area instead of an empty list. |
| 8 | Medium | `src/api/apiClient.ts` (lines 3–9) | **Empty base URL when `VITE_API_URL` missing.** If `VITE_API_URL` is not set, `getBaseUrl()` returns `''`, so requests become same-origin relative. That may be intended for same-origin deploy, but it is easy to misconfigure (e.g. missing env in production). | Document that production must set `VITE_API_URL` when the API is on another origin. Optionally log a warning in development when `VITE_API_URL` is missing, or fail fast in production if needed. |
| 9 | Medium | `src/components/MaintenanceLogScreen.tsx` (line 110) | **Unsafe type assertion.** `(e.target as any).dataset.entryId` bypasses type safety. `HTMLInputElement` has `dataset`; the cast is used to pass a custom data attribute. | Use a type that includes `dataset`: e.g. `(e.target as HTMLInputElement & { dataset: { entryId?: string } }).dataset.entryId`, or assign `entryId` in a ref/key and read from there instead of `dataset`. |

---

## 3. Low-risk improvements

| # | Severity | File | Explanation | Recommended fix |
|---|----------|------|-------------|-----------------|
| 10 | Low | Multiple | **Service import inconsistency.** `treatmentService.ts` re-exports `treatmentsService.ts`. Some files import from `treatmentService` (e.g. `TreatmentsReportScreen`, `ReportsListScreen`) and one from `treatmentsService` (`ReportDetailsScreen`). Works but is inconsistent. | Standardize on one module name (e.g. `treatmentsService`) and update all imports. Remove or deprecate `treatmentService.ts` if it only re-exports. |
| 11 | Low | `src/components/ForkliftReportsScreen.tsx` (lines 20, 25) | **Unused state setters.** `_setForkliftNumber` and `_setType` are prefixed to satisfy no-unused rules but are never used. Either the UI is incomplete or the state is redundant. | Use the setters for filters (e.g. forklift number, type) or remove the state and any related UI that was planned. |
| 12 | Low | `src/context/LanguageContext.tsx` | **`t` recreated every render.** The `t` function is defined inline in the provider, so it is a new reference each render. Any child that includes `t` in `useEffect` or `useMemo` deps will re-run when the parent re-renders. | Memoize `t` with `useCallback` keyed by `language` so the reference is stable when the language does not change. |
| 13 | Low | `src/main.tsx` (lines 7–14) | **Async init before render.** `initDevAuth()` is awaited before `createRoot(...).render()`. In production this is a no-op; in dev it can delay first paint if login is slow. | Acceptable for dev-only; optionally render a minimal shell immediately and run `initDevAuth()` after first paint, or show a brief “Loading…” only in development. |
| 14 | Low | `vite.config.ts` | **No `base`.** If the app is deployed under a subpath (e.g. `https://example.com/app/`), assets and client-side routes may break without `base: '/app/'`. | If deployment uses a subpath, set `base` in `vite.config.ts` to that path. |

---

## 4. Code quality improvements

| # | Severity | File | Explanation | Recommended fix |
|---|----------|------|-------------|-----------------|
| 15 | Low | `src/components/AnnualPlansScreen.tsx` (line 96) | **Explicit `any`.** `updateSummerRow(..., value: any)` weakens type safety for the field value. | Type `value` as a union of the possible field value types (e.g. `string \| number`) or as `SummerMaintenanceRow[keyof SummerMaintenanceRow]`. |
| 16 | Low | `src/services/annualPlansService.ts` (line 26) | **`body: unknown` for save.** `saveAnnualPlan(body: unknown)` accepts any payload. Callers build typed objects but the API contract is not enforced at the service boundary. | Define a proper request type (e.g. `SaveAnnualPlanRequest`) and use it as the argument type for `saveAnnualPlan`. |
| 17 | Low | Services | **Duplicate `buildSearchParams`.** `maintenanceService.ts`, `morningRoundsService.ts`, and `treatmentsService.ts` each define a local `buildSearchParams`. Logic is similar (build `URLSearchParams`, return `?${q}` or `''`). | Extract a shared helper (e.g. in `api/` or `utils/`) that accepts a record of optional string/number params and returns the query string. Reuse in all services. |
| 18 | Low | `src/components/Logo.tsx` (line 13) | **Unused prop.** `showText` is destructured as `_showText` and never used. | Remove the prop from the interface and usage, or implement the “show text” behavior. |
| 19 | Low | `src/components/ReportsListScreen.tsx` (line 151) | **Forklift-reports tab.** When `selectedType === 'forklift-reports'` the component renders `<ForkliftReportsScreen />`, which has its own loading and state. The parent does not show a loading state for that tab switch. | Consider a brief loading or transition state when switching to forklift reports if the child’s initial load is slow, or leave as-is if UX is acceptable. |

---

## 5. Production safety

| # | Severity | File | Explanation | Recommended fix |
|---|----------|------|-------------|-----------------|
| 20 | Medium | Multiple (see list) | **`console.error` / `console.log` in production.** Many screens log errors (and in one place, debug logs) to the console. `AnnualPlansScreen` uses `process.env.NODE_ENV === 'development'` for some logs but not all; others have no guard. | Prefer a single logging/error-reporting utility that no-ops or forwards to an error service in production. Remove or guard `console.log`/`console.error` in production, or strip them at build time. |
| 21 | Low | `src/dev/devAuthBootstrap.ts` (line 14) | **`console.error` in dev bootstrap.** Logs when dev auto-login fails. Acceptable for dev; ensure this file is tree-shaken or not loaded in production (it already returns early when `import.meta.env.PROD`). | No change required if PROD is guaranteed; optionally use a dev-only logger. |
| 22 | Low | `src/components/LoginScreen.tsx` (line 21) | **Login errors only logged.** Failed login is caught and `console.error(err)` is called; the user may see no feedback. | Set local error state and display a user-visible message (e.g. “Invalid credentials” or the API error message) on the login form. |
| 23 | Low | `src/components/ReportDetailsScreen.tsx`, others | **Hardcoded error messages.** Strings like `'Failed to load morning round report'` are in English only. The app uses `LanguageContext` elsewhere. | Move these strings into the translation map in `LanguageContext` and use `t('error.loadMorningRound')` etc. for consistency and i18n. |

**Files with console usage:**

- `HomeScreen.tsx` — `console.error` (line 25)
- `TreatmentsReportScreen.tsx` — `console.error` (line 66)
- `ReportsListScreen.tsx` — `console.error` (lines 76, 114, 152)
- `ReportDetailsScreen.tsx` — `console.error` (lines 68, 97, 126, 149)
- `MorningRoundScreen.tsx` — `console.error` (lines 46, 124)
- `MaintenanceLogScreen.tsx` — `console.error` (line 58, 177)
- `LoginScreen.tsx` — `console.error` (line 21)
- `ForkliftReportsScreen.tsx` — `console.error` (lines 47, 68)
- `ForkliftReportScreen.tsx` — `console.error` (line 48)
- `ForkliftReportDetailsScreen.tsx` — `console.error` (line 31)
- `AnnualPlansScreen.tsx` — `console.log` (lines 121–122, 172, 262), `console.error` (lines 128, 247, 271)
- `AnnualPlanReportScreen.tsx` — `console.error` (line 36)
- `devAuthBootstrap.ts` — `console.error` (line 14)

---

## 6. Performance risks

| # | Severity | File | Explanation | Recommended fix |
|---|----------|------|-------------|-----------------|
| 24 | Low | `src/context/LanguageContext.tsx` | **Unstable `t` reference.** As in item 12, `t` changes every render. Components that list `t` in dependency arrays (e.g. `ReportDetailsScreen`) will re-run effects when any parent re-renders. | Memoize `t` with `useCallback` keyed by `language`. |
| 25 | Low | `src/components/ReportsListScreen.tsx` | **Multiple useEffects per report type.** Separate effects for morning, maintenance, and treatments run when `selectedType` and `debouncedSearch` change. Each effect is correctly cancelled. No redundant network calls observed from logic. | Consider a single effect that dispatches by `selectedType` to avoid three separate effect subscriptions, or leave as-is if readability is preferred. |
| 26 | Low | `src/components/AnnualPlansScreen.tsx` | **Large component.** File is ~689 lines with many useState hooks and inline handlers. State updates can cause full component re-renders. | Split into smaller components (e.g. preventive plan form, summer plan table, year/type selector) and/or extract custom hooks for plan loading and saving to reduce re-render scope. |
| 27 | Low | App-level | **No memoization of route handlers.** `handleNavigate`, `handleSelectReport`, etc. are recreated every render of `AppRoutes`. They are passed to children; if those children are not memoized, they may re-render unnecessarily. | Wrap handlers in `useCallback` if child re-renders become a concern, or memoize heavy list children (e.g. report list items) with `React.memo`. |

---

## 7. Manual QA checks still required

- **Auth and token:** After fixing critical auth/logout issues, manually verify: login → refresh (should stay logged in if token is used); logout → confirm token cleared and re-login required; 401 from API clears token and redirects to login.
- **Production build and env:** Run `npm run build` with production `VITE_API_URL` and `npm run preview`; verify all API calls go to the correct host and that no dev-only code runs.
- **i18n:** Switch language (EN/HE/TH) on key screens (login, home, reports, settings) and confirm layout (RTL/LTR), labels, and error messages.
- **Error and empty states:** For each report type and the reports list, simulate network failure or empty data and confirm user-visible error and empty states (no blank screens).
- **Forms:** Submit morning round, maintenance log, treatments, and forklift report with invalid or missing required fields; confirm validation messages and that no unhandled errors reach the console in production.
- **Forklift reports:** Load forklift list and reports; apply filters; open a report detail; confirm loading and error states and that data matches the selected filters.
- **Annual plans:** Load preventive and summer plans for a year; edit and save; confirm success/error toasts and that no debug logs appear in production.

---

## Summary

- **Build and TypeScript:** No issues; both pass.
- **Critical:** Fix auth persistence (use token on load), clear token on logout, and configure production `VITE_API_URL`.
- **Medium:** Address unnecessary Home API call, missing loading states (Reports list, forklifts), effect dependency on `t`, logout not clearing token, and unsafe `dataset` cast.
- **Low / quality:** Consolidate service naming, remove or use unused state/props, type `any`/`unknown` properly, centralize query building and error messages, reduce console usage in production, and consider memoization and component size.

No files were modified; this report is analysis only.
