-- =============================================================================
-- Dev/test only — local users and approver assignments for verifying the
-- Clearance approval workflow end to end.
-- =============================================================================
-- Run through ./scripts/seed-clearance-dev.sh (it supplies :password_hash).
--
-- Adds (never updates or deletes existing rows; safe to re-run):
--   Pilot Factory tenant (11111111-1111-1111-1111-111111111111)
--     clearance.approver   Worker  — assigned Clearance approver
--     clearance.submitter  Worker  — not an approver (creator of reports)
--     clearance.manager    Manager — not an approver (role alone grants nothing)
--   Second local tenant (061082e0-3c3d-4673-b860-fdf4bd309e4a)
--     clearance.other      Manager — approver in the OTHER tenant only
--
-- The existing "admin" (SuperAdmin) user is left unassigned: it may view the
-- queue but must not be able to decide.
-- Requires the Clearance schema (migration 20261019172521_AddClearanceFoundation).
-- =============================================================================

\set ON_ERROR_STOP on

BEGIN;

INSERT INTO users (id, email, username, password_hash, display_name, is_active, tenant_id, created_at, role_id,
                   enable_new_morning_round, enable_new_maintenance_log)
VALUES
    ('c1ea0000-0000-4000-8000-000000000001', 'clearance.approver@local.test', 'clearance.approver', :'password_hash',
     'Clearance Approver', true, '11111111-1111-1111-1111-111111111111', now(), 1, true, true),
    ('c1ea0000-0000-4000-8000-000000000002', 'clearance.submitter@local.test', 'clearance.submitter', :'password_hash',
     'Clearance Submitter', true, '11111111-1111-1111-1111-111111111111', now(), 1, true, true),
    ('c1ea0000-0000-4000-8000-000000000003', 'clearance.manager@local.test', 'clearance.manager', :'password_hash',
     'Clearance Manager', true, '11111111-1111-1111-1111-111111111111', now(), 2, true, true),
    ('c1ea0000-0000-4000-8000-000000000004', 'clearance.other@local.test', 'clearance.other', :'password_hash',
     'Other Tenant Approver', true, '061082e0-3c3d-4673-b860-fdf4bd309e4a', now(), 2, true, true)
ON CONFLICT (id) DO NOTHING;

INSERT INTO clearance_approvers (id, user_id, tenant_id, created_at)
VALUES
    ('c1ea0000-0000-4000-8000-0000000000a1', 'c1ea0000-0000-4000-8000-000000000001',
     '11111111-1111-1111-1111-111111111111', now()),
    ('c1ea0000-0000-4000-8000-0000000000a4', 'c1ea0000-0000-4000-8000-000000000004',
     '061082e0-3c3d-4673-b860-fdf4bd309e4a', now())
ON CONFLICT (tenant_id, user_id) DO NOTHING;

COMMIT;

-- Verification (read-only)
SELECT u.username, u.role_id, u.tenant_id, (a.id IS NOT NULL) AS is_clearance_approver
FROM users u
LEFT JOIN clearance_approvers a ON a.user_id = u.id AND a.tenant_id = u.tenant_id
ORDER BY u.tenant_id, u.username;
