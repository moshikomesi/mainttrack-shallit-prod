-- =============================================================================
-- MaintTrack — Production data: Complete machine/component hierarchy (Sprint)
-- =============================================================================
-- Purpose:
--   The Machine → Component hierarchy is the canonical maintenance model used
--   by Morning Round V2, Maintenance Log V2, Maintenance Reports and
--   Treatments. This script completes the component catalog and machine
--   mappings for 9 machine groups whose hierarchy was previously incomplete:
--
--     הופר (Hopper)          → machine.onion.hopper
--     ניקוי ייבש (Dry Clean)  → machine.dryCleaningProcess
--     משקל (Scale)            → machine.scale1 .. machine.scale6
--     סדרן (Line Sorter)      → machine.sorterLine5, machine.sorterLine6
--     אריזה (Packing)         → machine.packing1..4, packing5a/5b, packing6a/6b
--     פולישר (Polisher)       → machine.polisher1 .. machine.polisher3
--     צוברים (Accumulators)   → machine.accumulatorOutside
--     ממינות (Sorters)        → machine.sorter1 .. machine.sorter3
--     ויסכון (Viscon line)    → machine.viscose
--
-- IMPORTANT — machine-name mapping notes (read before modifying):
--   • "ניקוי ייבש" (given, double-yod) is applied to `machine.dryCleaningProcess`,
--     the only real, active machine row for dry cleaning (the translation key
--     `machine.dryCleaning` exists in translations.ts but has NO corresponding
--     row in the canonical machines seed — it is unused, so it is out of scope).
--   • "סדרן" is matched ONLY to machine.sorterLine5 / machine.sorterLine6
--     (Hebrew label "סדרן 5"/"סדרן 6"). It is intentionally NOT applied to
--     machine.sorter1/2/3, whose Hebrew label is the different word "ממיינת"
--     — per the "do not merge similar names" rule this distinction is treated
--     as intentional, exactly like מיסב/מיסבים or בוכנה/בוכנות.
--   • "ממינות" does not appear verbatim anywhere in the repository. The closest
--     and only plausible match is "ממיינת" / "ממיינות" (sorter/sorters), the
--     Hebrew label of machine.sorter1/2/3 — reinforced by the component list
--     itself (טריגרים/triggers, מצלמה/camera match the existing checklist text
--     "כיוון טריגרים בממיינת" for these exact machines). This mapping is an
--     explicit assumption — flagged in the deployment report — and can be
--     corrected with a follow-up script if wrong.
--   • "צוברים" is matched to `machine.accumulatorOutside` only. The
--     translation key `machine.accumulatorInside` exists but — like
--     `machine.dryCleaning` — has no corresponding row in the canonical
--     machines seed, so there is no second accumulator machine to map today.
--
-- IMPORTANT — component-name rules followed exactly as specified:
--   • No normalization, merging, singularizing, or pluralizing of any name.
--   • Every component name below is reused ONLY when it exactly matches an
--     existing `machine_components.name_key` Hebrew value (see the full
--     existing catalog in the comment block below). Otherwise a brand new
--     catalog row is created, byte-for-byte identical to the name supplied.
--   • Distinctions such as מיסב vs מיסבים, בוכנה vs בוכנות, מסוע vs מסועים,
--     קפיץ vs קפיצים, מרעד vs מרעדים, and "גומיות/כוכבים" (existing,
--     RUBBER_STARS) vs "גומיות כוכבים" (new, no slash) are preserved as
--     distinct, separate catalog rows — never merged.
--
--   Existing catalog reused as-is (exact matches only):
--     MOTOR=מנוע, SHAFT=ציר, BEARING=מיסב, BELT=סרט, CHAIN=שרשרת,
--     CONVEYOR=מסוע, SCALE=משקל, COIL=סליל, CONVEYOR_BELT=סרט מסוע,
--     BEARINGS=מיסבים
--
-- i18n integration:
--   `machine_components.name_key` is a lookup key into the app's single,
--   existing translation dictionary (frontend/src/i18n/translations.ts,
--   `Record<string, Record<'en'|'he'|'th', string>>`), resolved at render
--   time via `t(key)` in frontend/src/context/LanguageContext.tsx. This is
--   the SAME mechanism/namespace already used by every other machine
--   component (`maintenanceComponent.motor`, `maintenanceComponent.shaft`,
--   etc.) — no new translation mechanism is introduced.
--   All 42 new components below use a proper `maintenanceComponent.xxx` key
--   (camelCase, matching the existing naming convention exactly), and a
--   corresponding `en`/`he`/`th` entry has been added to translations.ts for
--   every single one — the app's only 3 supported languages. The Hebrew
--   value of each new translation entry is byte-for-byte identical to the
--   name supplied for this task (no normalization/merging/singularizing).
--
-- PREREQUISITES (must already be applied — all part of this sprint):
--   1. scripts/production-machine-components-schema.sql (tables)
--   2. scripts/production-seed-arrays-machines.sql (arrays + machines)
--   3. scripts/machine_components_seed.sql (base 22-component catalog)
--   4. scripts/production-hierarchy-map-alignment.sql (RUBBER_STARS,
--      BEARINGS, OVERHAUL catalog rows + machine.conveyors.general, etc.)
--
-- Safe properties:
--   • Idempotent — components are guarded by NOT EXISTS on (tenant_id, code)
--     OR (tenant_id, name_key); mappings are guarded by NOT EXISTS on
--     (machine_id, component_id). Safe to re-run any number of times.
--   • Applies to ALL tenants automatically (CROSS JOIN tenants / name-based
--     JOIN on machines) — no tenant UUID needs to be supplied.
--   • Never deletes any row. The only UPDATE is a narrowly-scoped repair
--     pass (Section 1.1) that re-points `name_key` for the 42 new component
--     codes above to their correct translation key — a no-op unless an
--     earlier revision of this exact script already ran. No existing
--     component/mapping created by any other script is ever touched.
--   • Wrapped in a single transaction — either applies fully or not at all.
--
-- Usage (direct psql against production host):
--   psql "host=$PROD_PGHOST user=$PROD_PGUSER dbname=$PROD_DB sslmode=require" \
--        -v ON_ERROR_STOP=1 \
--        -f scripts/production-machine-components-complete.sql
--
-- Usage (docker-based environments, e.g. TEST):
--   docker exec -i mainttrack-postgres psql -U postgres -d mainttrack_dev \
--        -v ON_ERROR_STOP=1 < scripts/production-machine-components-complete.sql
-- =============================================================================

BEGIN;

-- =============================================================================
-- SECTION 1: NEW machine_components catalog rows (exact Hebrew text preserved)
-- =============================================================================

INSERT INTO machine_components (id, tenant_id, code, name_key, sort_order, is_active, created_at)
SELECT v.id, t.id, v.code, v.name_key, v.sort_order, true, NOW()
FROM tenants t
CROSS JOIN (VALUES
    -- shared by הופר / ניקוי ייבש
    ('c3000002-0000-0000-0000-000000000001'::uuid, 'BINDERS',              'maintenanceComponent.binders',              26),
    ('c3000002-0000-0000-0000-000000000002'::uuid, 'CONTROL_PANEL',        'maintenanceComponent.controlPanel',         27),
    ('c3000002-0000-0000-0000-000000000003'::uuid, 'GEARBOX',              'maintenanceComponent.gearbox',              28),
    -- ניקוי ייבש only
    ('c3000002-0000-0000-0000-000000000004'::uuid, 'RUBBER_AND_STARS',     'maintenanceComponent.rubberAndStars',       29),
    -- משקל
    ('c3000002-0000-0000-0000-000000000005'::uuid, 'OPENING_COMPARTMENT',  'maintenanceComponent.openingCompartment',   30),
    ('c3000002-0000-0000-0000-000000000006'::uuid, 'WEIGHING_COMPARTMENT', 'maintenanceComponent.weighingCompartment',  31),
    ('c3000002-0000-0000-0000-000000000007'::uuid, 'WEIGHING_CARD',        'maintenanceComponent.weighingCard',         32),
    ('c3000002-0000-0000-0000-000000000008'::uuid, 'PISTONS',              'maintenanceComponent.pistons',              33),
    ('c3000002-0000-0000-0000-000000000009'::uuid, 'CONVEYORS',            'maintenanceComponent.conveyors',            34),
    ('c3000002-0000-0000-0000-000000000010'::uuid, 'TRANSDUCER',           'maintenanceComponent.transducer',           35),
    ('c3000002-0000-0000-0000-000000000011'::uuid, 'UPPER_CARROT_STOPPER', 'maintenanceComponent.upperCarrotStopper',   36),
    ('c3000002-0000-0000-0000-000000000012'::uuid, 'VIBRATORS',            'maintenanceComponent.vibrators',            37),
    ('c3000002-0000-0000-0000-000000000013'::uuid, 'ELECTRIC_PANEL',       'maintenanceComponent.electricPanel',        38),
    -- סדרן (shared with אריזה / ויסכון where noted)
    ('c3000002-0000-0000-0000-000000000014'::uuid, 'PISTON',               'maintenanceComponent.piston',               39),
    ('c3000002-0000-0000-0000-000000000015'::uuid, 'CLAMPS',               'maintenanceComponent.clamps',               40),
    ('c3000002-0000-0000-0000-000000000016'::uuid, 'SENSOR',               'maintenanceComponent.sensor',               41),
    ('c3000002-0000-0000-0000-000000000017'::uuid, 'ENCODER',              'maintenanceComponent.encoder',              42),
    ('c3000002-0000-0000-0000-000000000018'::uuid, 'PUSHER',               'maintenanceComponent.pusher',               43),
    -- אריזה
    ('c3000002-0000-0000-0000-000000000019'::uuid, 'TRANSPORT_STRAP',      'maintenanceComponent.transportStrap',       44),
    ('c3000002-0000-0000-0000-000000000020'::uuid, 'TRANSPORT_WHEELS',     'maintenanceComponent.transportWheels',      45),
    ('c3000002-0000-0000-0000-000000000021'::uuid, 'ROUND_KNIFE',          'maintenanceComponent.roundKnife',           46),
    ('c3000002-0000-0000-0000-000000000022'::uuid, 'THERMOSTAT',           'maintenanceComponent.thermostat',           47),
    ('c3000002-0000-0000-0000-000000000023'::uuid, 'HEATING_ELEMENT',      'maintenanceComponent.heatingElement',       48),
    ('c3000002-0000-0000-0000-000000000024'::uuid, 'SPRINGS',              'maintenanceComponent.springs',              49),
    -- פולישר
    ('c3000002-0000-0000-0000-000000000025'::uuid, 'BRUSHES',              'maintenanceComponent.brushes',              50),
    ('c3000002-0000-0000-0000-000000000026'::uuid, 'KOPLONG',              'maintenanceComponent.koplong',              51),
    ('c3000002-0000-0000-0000-000000000027'::uuid, 'POLI',                 'maintenanceComponent.poli',                 52),
    ('c3000002-0000-0000-0000-000000000028'::uuid, 'FRONT_BELT',           'maintenanceComponent.frontBelt',            53),
    ('c3000002-0000-0000-0000-000000000029'::uuid, 'REAR_BELT',            'maintenanceComponent.rearBelt',             54),
    ('c3000002-0000-0000-0000-000000000030'::uuid, 'DRIVE_WHEEL',          'maintenanceComponent.driveWheel',           55),
    ('c3000002-0000-0000-0000-000000000031'::uuid, 'BUSHING',              'maintenanceComponent.bushing',              56),
    -- צוברים (shared with ממינות where noted)
    ('c3000002-0000-0000-0000-000000000032'::uuid, 'CONTROL_BOARD',        'maintenanceComponent.controlBoard',         57),
    -- ממינות
    ('c3000002-0000-0000-0000-000000000033'::uuid, 'VIBRATOR',             'maintenanceComponent.vibrator',             58),
    ('c3000002-0000-0000-0000-000000000034'::uuid, 'SPRING',               'maintenanceComponent.spring',               59),
    ('c3000002-0000-0000-0000-000000000035'::uuid, 'LONG_BLUE_BELT',       'maintenanceComponent.longBlueBelt',         60),
    ('c3000002-0000-0000-0000-000000000036'::uuid, 'SHORT_BLUE_BELT',      'maintenanceComponent.shortBlueBelt',        61),
    ('c3000002-0000-0000-0000-000000000037'::uuid, 'TRIGGERS',             'maintenanceComponent.triggers',             62),
    ('c3000002-0000-0000-0000-000000000038'::uuid, 'CAMERA',               'maintenanceComponent.camera',               63),
    ('c3000002-0000-0000-0000-000000000039'::uuid, 'TENSIONER',            'maintenanceComponent.tensioner',            64),
    ('c3000002-0000-0000-0000-000000000040'::uuid, 'TIMING_BELT',          'maintenanceComponent.timingBelt',           65),
    ('c3000002-0000-0000-0000-000000000041'::uuid, 'DVC',                  'maintenanceComponent.dvc',                  66),
    -- ויסכון
    ('c3000002-0000-0000-0000-000000000042'::uuid, 'CONVEYOR_WHEELS',      'maintenanceComponent.conveyorWheels',       67)
) AS v(id, code, name_key, sort_order)
WHERE NOT EXISTS (
    SELECT 1 FROM machine_components mc
    WHERE mc.tenant_id = t.id
      AND (mc.code = v.code OR mc.name_key = v.name_key)
);

-- 1.1 Repair pass — idempotency safety net for any environment where an
-- earlier revision of this script was already applied with a different
-- name_key (e.g. a raw-text placeholder). Re-points name_key to the correct
-- translation key for existing rows, matched by `code` only. No-op on a
-- fresh run against a database that never saw an earlier revision.
UPDATE machine_components mc
SET name_key = v.name_key
FROM (VALUES
    ('BINDERS', 'maintenanceComponent.binders'), ('CONTROL_PANEL', 'maintenanceComponent.controlPanel'),
    ('GEARBOX', 'maintenanceComponent.gearbox'), ('RUBBER_AND_STARS', 'maintenanceComponent.rubberAndStars'),
    ('OPENING_COMPARTMENT', 'maintenanceComponent.openingCompartment'), ('WEIGHING_COMPARTMENT', 'maintenanceComponent.weighingCompartment'),
    ('WEIGHING_CARD', 'maintenanceComponent.weighingCard'), ('PISTONS', 'maintenanceComponent.pistons'),
    ('CONVEYORS', 'maintenanceComponent.conveyors'), ('TRANSDUCER', 'maintenanceComponent.transducer'),
    ('UPPER_CARROT_STOPPER', 'maintenanceComponent.upperCarrotStopper'), ('VIBRATORS', 'maintenanceComponent.vibrators'),
    ('ELECTRIC_PANEL', 'maintenanceComponent.electricPanel'), ('PISTON', 'maintenanceComponent.piston'),
    ('CLAMPS', 'maintenanceComponent.clamps'), ('SENSOR', 'maintenanceComponent.sensor'),
    ('ENCODER', 'maintenanceComponent.encoder'), ('PUSHER', 'maintenanceComponent.pusher'),
    ('TRANSPORT_STRAP', 'maintenanceComponent.transportStrap'), ('TRANSPORT_WHEELS', 'maintenanceComponent.transportWheels'),
    ('ROUND_KNIFE', 'maintenanceComponent.roundKnife'), ('THERMOSTAT', 'maintenanceComponent.thermostat'),
    ('HEATING_ELEMENT', 'maintenanceComponent.heatingElement'), ('SPRINGS', 'maintenanceComponent.springs'),
    ('BRUSHES', 'maintenanceComponent.brushes'), ('KOPLONG', 'maintenanceComponent.koplong'),
    ('POLI', 'maintenanceComponent.poli'), ('FRONT_BELT', 'maintenanceComponent.frontBelt'),
    ('REAR_BELT', 'maintenanceComponent.rearBelt'), ('DRIVE_WHEEL', 'maintenanceComponent.driveWheel'),
    ('BUSHING', 'maintenanceComponent.bushing'), ('CONTROL_BOARD', 'maintenanceComponent.controlBoard'),
    ('VIBRATOR', 'maintenanceComponent.vibrator'), ('SPRING', 'maintenanceComponent.spring'),
    ('LONG_BLUE_BELT', 'maintenanceComponent.longBlueBelt'), ('SHORT_BLUE_BELT', 'maintenanceComponent.shortBlueBelt'),
    ('TRIGGERS', 'maintenanceComponent.triggers'), ('CAMERA', 'maintenanceComponent.camera'),
    ('TENSIONER', 'maintenanceComponent.tensioner'), ('TIMING_BELT', 'maintenanceComponent.timingBelt'),
    ('DVC', 'maintenanceComponent.dvc'), ('CONVEYOR_WHEELS', 'maintenanceComponent.conveyorWheels')
) AS v(code, name_key)
WHERE mc.code = v.code
  AND mc.name_key IS DISTINCT FROM v.name_key;

-- =============================================================================
-- SECTION 2: NEW machine_component_mappings
--   Only pairs that do not already exist are inserted (NOT EXISTS guard).
--   sort_order continues after any pre-existing mappings for machines that
--   were already partially mapped (machine.dryCleaningProcess had MOTOR/
--   SHAFT/RUBBER at 1-3; machine.polisher1/2/3 had BRUSH/SHAFT/COUPLING/
--   BELT/PULLEY/MOTOR at 1-6).
-- =============================================================================

INSERT INTO machine_component_mappings (id, tenant_id, machine_id, component_id, sort_order, is_active, created_at)
SELECT gen_random_uuid(), m.tenant_id, m.id, mc.id, spec.sort_order, true, NOW()
FROM (VALUES
    -- הופר → machine.onion.hopper (all new)
    ('machine.onion.hopper', 'CHAIN',          1),
    ('machine.onion.hopper', 'BELT',           2),
    ('machine.onion.hopper', 'BEARINGS',       3),
    ('machine.onion.hopper', 'BINDERS',        4),
    ('machine.onion.hopper', 'CONTROL_PANEL',  5),
    ('machine.onion.hopper', 'MOTOR',          6),
    ('machine.onion.hopper', 'GEARBOX',        7),
    ('machine.onion.hopper', 'SHAFT',          8),

    -- ניקוי ייבש → machine.dryCleaningProcess (MOTOR=1, SHAFT=2, RUBBER=3 already exist)
    ('machine.dryCleaningProcess', 'CHAIN',            4),
    ('machine.dryCleaningProcess', 'BELT',             5),
    ('machine.dryCleaningProcess', 'BEARINGS',         6),
    ('machine.dryCleaningProcess', 'BINDERS',          7),
    ('machine.dryCleaningProcess', 'CONTROL_PANEL',    8),
    ('machine.dryCleaningProcess', 'GEARBOX',          9),
    ('machine.dryCleaningProcess', 'RUBBER_AND_STARS', 10),
    ('machine.dryCleaningProcess', 'CONVEYORS',        11),

    -- משקל → machine.scale1..scale6 (all new)
    ('machine.scale1', 'OPENING_COMPARTMENT',  1), ('machine.scale1', 'WEIGHING_COMPARTMENT', 2), ('machine.scale1', 'WEIGHING_CARD', 3), ('machine.scale1', 'PISTONS', 4), ('machine.scale1', 'CONVEYORS', 5), ('machine.scale1', 'SHAFT', 6), ('machine.scale1', 'TRANSDUCER', 7), ('machine.scale1', 'UPPER_CARROT_STOPPER', 8), ('machine.scale1', 'VIBRATORS', 9), ('machine.scale1', 'ELECTRIC_PANEL', 10), ('machine.scale1', 'BEARINGS', 11),
    ('machine.scale2', 'OPENING_COMPARTMENT',  1), ('machine.scale2', 'WEIGHING_COMPARTMENT', 2), ('machine.scale2', 'WEIGHING_CARD', 3), ('machine.scale2', 'PISTONS', 4), ('machine.scale2', 'CONVEYORS', 5), ('machine.scale2', 'SHAFT', 6), ('machine.scale2', 'TRANSDUCER', 7), ('machine.scale2', 'UPPER_CARROT_STOPPER', 8), ('machine.scale2', 'VIBRATORS', 9), ('machine.scale2', 'ELECTRIC_PANEL', 10), ('machine.scale2', 'BEARINGS', 11),
    ('machine.scale3', 'OPENING_COMPARTMENT',  1), ('machine.scale3', 'WEIGHING_COMPARTMENT', 2), ('machine.scale3', 'WEIGHING_CARD', 3), ('machine.scale3', 'PISTONS', 4), ('machine.scale3', 'CONVEYORS', 5), ('machine.scale3', 'SHAFT', 6), ('machine.scale3', 'TRANSDUCER', 7), ('machine.scale3', 'UPPER_CARROT_STOPPER', 8), ('machine.scale3', 'VIBRATORS', 9), ('machine.scale3', 'ELECTRIC_PANEL', 10), ('machine.scale3', 'BEARINGS', 11),
    ('machine.scale4', 'OPENING_COMPARTMENT',  1), ('machine.scale4', 'WEIGHING_COMPARTMENT', 2), ('machine.scale4', 'WEIGHING_CARD', 3), ('machine.scale4', 'PISTONS', 4), ('machine.scale4', 'CONVEYORS', 5), ('machine.scale4', 'SHAFT', 6), ('machine.scale4', 'TRANSDUCER', 7), ('machine.scale4', 'UPPER_CARROT_STOPPER', 8), ('machine.scale4', 'VIBRATORS', 9), ('machine.scale4', 'ELECTRIC_PANEL', 10), ('machine.scale4', 'BEARINGS', 11),
    ('machine.scale5', 'OPENING_COMPARTMENT',  1), ('machine.scale5', 'WEIGHING_COMPARTMENT', 2), ('machine.scale5', 'WEIGHING_CARD', 3), ('machine.scale5', 'PISTONS', 4), ('machine.scale5', 'CONVEYORS', 5), ('machine.scale5', 'SHAFT', 6), ('machine.scale5', 'TRANSDUCER', 7), ('machine.scale5', 'UPPER_CARROT_STOPPER', 8), ('machine.scale5', 'VIBRATORS', 9), ('machine.scale5', 'ELECTRIC_PANEL', 10), ('machine.scale5', 'BEARINGS', 11),
    ('machine.scale6', 'OPENING_COMPARTMENT',  1), ('machine.scale6', 'WEIGHING_COMPARTMENT', 2), ('machine.scale6', 'WEIGHING_CARD', 3), ('machine.scale6', 'PISTONS', 4), ('machine.scale6', 'CONVEYORS', 5), ('machine.scale6', 'SHAFT', 6), ('machine.scale6', 'TRANSDUCER', 7), ('machine.scale6', 'UPPER_CARROT_STOPPER', 8), ('machine.scale6', 'VIBRATORS', 9), ('machine.scale6', 'ELECTRIC_PANEL', 10), ('machine.scale6', 'BEARINGS', 11),

    -- סדרן → machine.sorterLine5, machine.sorterLine6 (all new)
    ('machine.sorterLine5', 'CONVEYOR', 1), ('machine.sorterLine5', 'SHAFT', 2), ('machine.sorterLine5', 'PISTON', 3), ('machine.sorterLine5', 'CLAMPS', 4), ('machine.sorterLine5', 'ELECTRIC_PANEL', 5), ('machine.sorterLine5', 'SENSOR', 6), ('machine.sorterLine5', 'BEARING', 7), ('machine.sorterLine5', 'CHAIN', 8), ('machine.sorterLine5', 'MOTOR', 9), ('machine.sorterLine5', 'ENCODER', 10), ('machine.sorterLine5', 'PUSHER', 11),
    ('machine.sorterLine6', 'CONVEYOR', 1), ('machine.sorterLine6', 'SHAFT', 2), ('machine.sorterLine6', 'PISTON', 3), ('machine.sorterLine6', 'CLAMPS', 4), ('machine.sorterLine6', 'ELECTRIC_PANEL', 5), ('machine.sorterLine6', 'SENSOR', 6), ('machine.sorterLine6', 'BEARING', 7), ('machine.sorterLine6', 'CHAIN', 8), ('machine.sorterLine6', 'MOTOR', 9), ('machine.sorterLine6', 'ENCODER', 10), ('machine.sorterLine6', 'PUSHER', 11),

    -- אריזה → machine.packing1..4, packing5a/5b, packing6a/6b (all new)
    ('machine.packing1', 'BEARING', 1), ('machine.packing1', 'SHAFT', 2), ('machine.packing1', 'BELT', 3), ('machine.packing1', 'TRANSPORT_STRAP', 4), ('machine.packing1', 'TRANSPORT_WHEELS', 5), ('machine.packing1', 'SENSOR', 6), ('machine.packing1', 'MOTOR', 7), ('machine.packing1', 'ROUND_KNIFE', 8), ('machine.packing1', 'THERMOSTAT', 9), ('machine.packing1', 'HEATING_ELEMENT', 10), ('machine.packing1', 'SPRINGS', 11), ('machine.packing1', 'PISTON', 12),
    ('machine.packing2', 'BEARING', 1), ('machine.packing2', 'SHAFT', 2), ('machine.packing2', 'BELT', 3), ('machine.packing2', 'TRANSPORT_STRAP', 4), ('machine.packing2', 'TRANSPORT_WHEELS', 5), ('machine.packing2', 'SENSOR', 6), ('machine.packing2', 'MOTOR', 7), ('machine.packing2', 'ROUND_KNIFE', 8), ('machine.packing2', 'THERMOSTAT', 9), ('machine.packing2', 'HEATING_ELEMENT', 10), ('machine.packing2', 'SPRINGS', 11), ('machine.packing2', 'PISTON', 12),
    ('machine.packing3', 'BEARING', 1), ('machine.packing3', 'SHAFT', 2), ('machine.packing3', 'BELT', 3), ('machine.packing3', 'TRANSPORT_STRAP', 4), ('machine.packing3', 'TRANSPORT_WHEELS', 5), ('machine.packing3', 'SENSOR', 6), ('machine.packing3', 'MOTOR', 7), ('machine.packing3', 'ROUND_KNIFE', 8), ('machine.packing3', 'THERMOSTAT', 9), ('machine.packing3', 'HEATING_ELEMENT', 10), ('machine.packing3', 'SPRINGS', 11), ('machine.packing3', 'PISTON', 12),
    ('machine.packing4', 'BEARING', 1), ('machine.packing4', 'SHAFT', 2), ('machine.packing4', 'BELT', 3), ('machine.packing4', 'TRANSPORT_STRAP', 4), ('machine.packing4', 'TRANSPORT_WHEELS', 5), ('machine.packing4', 'SENSOR', 6), ('machine.packing4', 'MOTOR', 7), ('machine.packing4', 'ROUND_KNIFE', 8), ('machine.packing4', 'THERMOSTAT', 9), ('machine.packing4', 'HEATING_ELEMENT', 10), ('machine.packing4', 'SPRINGS', 11), ('machine.packing4', 'PISTON', 12),
    ('machine.packing5a', 'BEARING', 1), ('machine.packing5a', 'SHAFT', 2), ('machine.packing5a', 'BELT', 3), ('machine.packing5a', 'TRANSPORT_STRAP', 4), ('machine.packing5a', 'TRANSPORT_WHEELS', 5), ('machine.packing5a', 'SENSOR', 6), ('machine.packing5a', 'MOTOR', 7), ('machine.packing5a', 'ROUND_KNIFE', 8), ('machine.packing5a', 'THERMOSTAT', 9), ('machine.packing5a', 'HEATING_ELEMENT', 10), ('machine.packing5a', 'SPRINGS', 11), ('machine.packing5a', 'PISTON', 12),
    ('machine.packing5b', 'BEARING', 1), ('machine.packing5b', 'SHAFT', 2), ('machine.packing5b', 'BELT', 3), ('machine.packing5b', 'TRANSPORT_STRAP', 4), ('machine.packing5b', 'TRANSPORT_WHEELS', 5), ('machine.packing5b', 'SENSOR', 6), ('machine.packing5b', 'MOTOR', 7), ('machine.packing5b', 'ROUND_KNIFE', 8), ('machine.packing5b', 'THERMOSTAT', 9), ('machine.packing5b', 'HEATING_ELEMENT', 10), ('machine.packing5b', 'SPRINGS', 11), ('machine.packing5b', 'PISTON', 12),
    ('machine.packing6a', 'BEARING', 1), ('machine.packing6a', 'SHAFT', 2), ('machine.packing6a', 'BELT', 3), ('machine.packing6a', 'TRANSPORT_STRAP', 4), ('machine.packing6a', 'TRANSPORT_WHEELS', 5), ('machine.packing6a', 'SENSOR', 6), ('machine.packing6a', 'MOTOR', 7), ('machine.packing6a', 'ROUND_KNIFE', 8), ('machine.packing6a', 'THERMOSTAT', 9), ('machine.packing6a', 'HEATING_ELEMENT', 10), ('machine.packing6a', 'SPRINGS', 11), ('machine.packing6a', 'PISTON', 12),
    ('machine.packing6b', 'BEARING', 1), ('machine.packing6b', 'SHAFT', 2), ('machine.packing6b', 'BELT', 3), ('machine.packing6b', 'TRANSPORT_STRAP', 4), ('machine.packing6b', 'TRANSPORT_WHEELS', 5), ('machine.packing6b', 'SENSOR', 6), ('machine.packing6b', 'MOTOR', 7), ('machine.packing6b', 'ROUND_KNIFE', 8), ('machine.packing6b', 'THERMOSTAT', 9), ('machine.packing6b', 'HEATING_ELEMENT', 10), ('machine.packing6b', 'SPRINGS', 11), ('machine.packing6b', 'PISTON', 12),

    -- פולישר → machine.polisher1..3 (BRUSH=1,SHAFT=2,COUPLING=3,BELT=4,PULLEY=5,MOTOR=6 already exist)
    ('machine.polisher1', 'BRUSHES', 7), ('machine.polisher1', 'KOPLONG', 8), ('machine.polisher1', 'POLI', 9), ('machine.polisher1', 'FRONT_BELT', 10), ('machine.polisher1', 'REAR_BELT', 11), ('machine.polisher1', 'DRIVE_WHEEL', 12), ('machine.polisher1', 'BEARING', 13), ('machine.polisher1', 'BUSHING', 14),
    ('machine.polisher2', 'BRUSHES', 7), ('machine.polisher2', 'KOPLONG', 8), ('machine.polisher2', 'POLI', 9), ('machine.polisher2', 'FRONT_BELT', 10), ('machine.polisher2', 'REAR_BELT', 11), ('machine.polisher2', 'DRIVE_WHEEL', 12), ('machine.polisher2', 'BEARING', 13), ('machine.polisher2', 'BUSHING', 14),
    ('machine.polisher3', 'BRUSHES', 7), ('machine.polisher3', 'KOPLONG', 8), ('machine.polisher3', 'POLI', 9), ('machine.polisher3', 'FRONT_BELT', 10), ('machine.polisher3', 'REAR_BELT', 11), ('machine.polisher3', 'DRIVE_WHEEL', 12), ('machine.polisher3', 'BEARING', 13), ('machine.polisher3', 'BUSHING', 14),

    -- צוברים → machine.accumulatorOutside (all new)
    ('machine.accumulatorOutside', 'MOTOR', 1), ('machine.accumulatorOutside', 'BELT', 2), ('machine.accumulatorOutside', 'SHAFT', 3), ('machine.accumulatorOutside', 'CONTROL_BOARD', 4), ('machine.accumulatorOutside', 'BEARING', 5), ('machine.accumulatorOutside', 'ENCODER', 6), ('machine.accumulatorOutside', 'SCALE', 7),

    -- ממינות → machine.sorter1..3 (all new)
    ('machine.sorter1', 'VIBRATOR', 1), ('machine.sorter1', 'SPRING', 2), ('machine.sorter1', 'LONG_BLUE_BELT', 3), ('machine.sorter1', 'SHORT_BLUE_BELT', 4), ('machine.sorter1', 'TRIGGERS', 5), ('machine.sorter1', 'COIL', 6), ('machine.sorter1', 'CAMERA', 7), ('machine.sorter1', 'SHAFT', 8), ('machine.sorter1', 'TENSIONER', 9), ('machine.sorter1', 'MOTOR', 10), ('machine.sorter1', 'SENSOR', 11), ('machine.sorter1', 'CONVEYOR_BELT', 12), ('machine.sorter1', 'BEARING', 13), ('machine.sorter1', 'TIMING_BELT', 14), ('machine.sorter1', 'DVC', 15), ('machine.sorter1', 'CONTROL_BOARD', 16),
    ('machine.sorter2', 'VIBRATOR', 1), ('machine.sorter2', 'SPRING', 2), ('machine.sorter2', 'LONG_BLUE_BELT', 3), ('machine.sorter2', 'SHORT_BLUE_BELT', 4), ('machine.sorter2', 'TRIGGERS', 5), ('machine.sorter2', 'COIL', 6), ('machine.sorter2', 'CAMERA', 7), ('machine.sorter2', 'SHAFT', 8), ('machine.sorter2', 'TENSIONER', 9), ('machine.sorter2', 'MOTOR', 10), ('machine.sorter2', 'SENSOR', 11), ('machine.sorter2', 'CONVEYOR_BELT', 12), ('machine.sorter2', 'BEARING', 13), ('machine.sorter2', 'TIMING_BELT', 14), ('machine.sorter2', 'DVC', 15), ('machine.sorter2', 'CONTROL_BOARD', 16),
    ('machine.sorter3', 'VIBRATOR', 1), ('machine.sorter3', 'SPRING', 2), ('machine.sorter3', 'LONG_BLUE_BELT', 3), ('machine.sorter3', 'SHORT_BLUE_BELT', 4), ('machine.sorter3', 'TRIGGERS', 5), ('machine.sorter3', 'COIL', 6), ('machine.sorter3', 'CAMERA', 7), ('machine.sorter3', 'SHAFT', 8), ('machine.sorter3', 'TENSIONER', 9), ('machine.sorter3', 'MOTOR', 10), ('machine.sorter3', 'SENSOR', 11), ('machine.sorter3', 'CONVEYOR_BELT', 12), ('machine.sorter3', 'BEARING', 13), ('machine.sorter3', 'TIMING_BELT', 14), ('machine.sorter3', 'DVC', 15), ('machine.sorter3', 'CONTROL_BOARD', 16),

    -- ויסכון → machine.viscose (all new)
    ('machine.viscose', 'CHAIN', 1), ('machine.viscose', 'SENSOR', 2), ('machine.viscose', 'SHAFT', 3), ('machine.viscose', 'PISTON', 4), ('machine.viscose', 'MOTOR', 5), ('machine.viscose', 'CONVEYOR', 6), ('machine.viscose', 'PUSHER', 7), ('machine.viscose', 'CONVEYOR_WHEELS', 8), ('machine.viscose', 'ELECTRIC_PANEL', 9)
) AS spec(machine_name, component_code, sort_order)
JOIN machines m
    ON m.name = spec.machine_name
   AND m.is_active = true
JOIN machine_components mc
    ON mc.tenant_id = m.tenant_id
   AND mc.code = spec.component_code
WHERE NOT EXISTS (
    SELECT 1
    FROM machine_component_mappings existing
    WHERE existing.machine_id = m.id
      AND existing.component_id = mc.id
);

COMMIT;

-- =============================================================================
-- SECTION 3: POST-RUN VERIFICATION (read-only)
-- =============================================================================

-- New catalog rows present (expect 42)
SELECT COUNT(*) AS new_components_present
FROM machine_components
WHERE code IN (
    'BINDERS','CONTROL_PANEL','GEARBOX','RUBBER_AND_STARS','OPENING_COMPARTMENT',
    'WEIGHING_COMPARTMENT','WEIGHING_CARD','PISTONS','CONVEYORS','TRANSDUCER',
    'UPPER_CARROT_STOPPER','VIBRATORS','ELECTRIC_PANEL','PISTON','CLAMPS','SENSOR',
    'ENCODER','PUSHER','TRANSPORT_STRAP','TRANSPORT_WHEELS','ROUND_KNIFE','THERMOSTAT',
    'HEATING_ELEMENT','SPRINGS','BRUSHES','KOPLONG','POLI','FRONT_BELT','REAR_BELT',
    'DRIVE_WHEEL','BUSHING','CONTROL_BOARD','VIBRATOR','SPRING','LONG_BLUE_BELT',
    'SHORT_BLUE_BELT','TRIGGERS','CAMERA','TENSIONER','TIMING_BELT','DVC','CONVEYOR_WHEELS'
);

-- Component counts per targeted machine
SELECT m.name, COUNT(mcm.id) AS component_count
FROM machines m
LEFT JOIN machine_component_mappings mcm ON mcm.machine_id = m.id AND mcm.is_active = true
WHERE m.is_active = true
  AND m.name IN (
    'machine.onion.hopper', 'machine.dryCleaningProcess',
    'machine.scale1','machine.scale2','machine.scale3','machine.scale4','machine.scale5','machine.scale6',
    'machine.sorterLine5','machine.sorterLine6',
    'machine.packing1','machine.packing2','machine.packing3','machine.packing4',
    'machine.packing5a','machine.packing5b','machine.packing6a','machine.packing6b',
    'machine.polisher1','machine.polisher2','machine.polisher3',
    'machine.accumulatorOutside',
    'machine.sorter1','machine.sorter2','machine.sorter3',
    'machine.viscose'
  )
GROUP BY m.name
ORDER BY m.name;

-- =============================================================================
-- ROLLBACK (reference only — run manually and selectively if ever needed)
-- =============================================================================
-- BEGIN;
--   DELETE FROM machine_component_mappings mcm
--     USING machines m, machine_components mc
--     WHERE mcm.machine_id = m.id AND mcm.component_id = mc.id
--     AND mc.code IN (
--       'BINDERS','CONTROL_PANEL','GEARBOX','RUBBER_AND_STARS','OPENING_COMPARTMENT',
--       'WEIGHING_COMPARTMENT','WEIGHING_CARD','PISTONS','CONVEYORS','TRANSDUCER',
--       'UPPER_CARROT_STOPPER','VIBRATORS','ELECTRIC_PANEL','PISTON','CLAMPS','SENSOR',
--       'ENCODER','PUSHER','TRANSPORT_STRAP','TRANSPORT_WHEELS','ROUND_KNIFE','THERMOSTAT',
--       'HEATING_ELEMENT','SPRINGS','BRUSHES','KOPLONG','POLI','FRONT_BELT','REAR_BELT',
--       'DRIVE_WHEEL','BUSHING','CONTROL_BOARD','VIBRATOR','SPRING','LONG_BLUE_BELT',
--       'SHORT_BLUE_BELT','TRIGGERS','CAMERA','TENSIONER','TIMING_BELT','DVC','CONVEYOR_WHEELS'
--     );
--   -- Also remove the mappings this script added that reused PRE-EXISTING
--   -- catalog codes (BEARINGS/BEARING/CHAIN/BELT/MOTOR/SHAFT/CONVEYOR/SCALE/
--   -- COIL/CONVEYOR_BELT) for the specific machines touched above, e.g.:
--   --   DELETE FROM machine_component_mappings mcm USING machines m, machine_components mc
--   --     WHERE mcm.machine_id = m.id AND mcm.component_id = mc.id
--   --     AND m.name = 'machine.onion.hopper' AND mc.code IN ('CHAIN','BELT','BEARINGS','MOTOR','SHAFT');
--   --   (repeat per machine as needed — omitted in full here since these are
--   --    additive, non-destructive rows with no impact if left in place)
--   DELETE FROM machine_components
--     WHERE code IN (
--       'BINDERS','CONTROL_PANEL','GEARBOX','RUBBER_AND_STARS','OPENING_COMPARTMENT',
--       'WEIGHING_COMPARTMENT','WEIGHING_CARD','PISTONS','CONVEYORS','TRANSDUCER',
--       'UPPER_CARROT_STOPPER','VIBRATORS','ELECTRIC_PANEL','PISTON','CLAMPS','SENSOR',
--       'ENCODER','PUSHER','TRANSPORT_STRAP','TRANSPORT_WHEELS','ROUND_KNIFE','THERMOSTAT',
--       'HEATING_ELEMENT','SPRINGS','BRUSHES','KOPLONG','POLI','FRONT_BELT','REAR_BELT',
--       'DRIVE_WHEEL','BUSHING','CONTROL_BOARD','VIBRATOR','SPRING','LONG_BLUE_BELT',
--       'SHORT_BLUE_BELT','TRIGGERS','CAMERA','TENSIONER','TIMING_BELT','DVC','CONVEYOR_WHEELS'
--     );
-- COMMIT;
-- =============================================================================
