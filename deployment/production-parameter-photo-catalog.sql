-- MaintTrack production parameter-photo catalog deployment
-- PostgreSQL 16. One transaction. Not approved for execution by this file alone.
--
-- Run after, as separate transactions:
--   scripts/production-clearance-schema.sql
--   scripts/production-machine-parameter-machine-exclusions.sql
--   deployment/production-translation-infrastructure.sql
-- This script does not create translation, clearance, or machine-exclusion tables.
-- It reads machine_parameter_machine_visibility and array_feature_visibility.
--
-- Schema and seed match:
--   backend/MaintTrack.Infrastructure/Migrations/20261119132527_AddIndependentParameterPhotoCatalog.cs
--   backend/MaintTrack.Infrastructure/Migrations/20261119192332_AddParameterPhotoReviewRemediation.cs
--   backend/MaintTrack.Domain/MachineParameterPhotos/ParameterPhotoCatalog.cs
--   backend/MaintTrack.Infrastructure/Persistence/MaintTrackDbContext.cs
-- Photo assignment matches ParameterPhotoLegacyLinker:
--   exact machine.name = parameter.name_key, one machine with that name, same tenant
--   never the standalone Hopper parameter
--   never machine.largeHopper, machine.smallHopper, or machine.onion.hopper
--   the eight compressor machine names map to parameterPhoto.parameter.compressorRooms
--   existing visibility exclusions are copied and those legacy links are cleared
-- Sort order changes only for the compressor assignment, where eight machines share one parameter.
-- No photo row or image file is deleted.

BEGIN;

SET LOCAL lock_timeout = '15s';
SET LOCAL statement_timeout = '300s';

DO $$
BEGIN
    IF current_database() <> 'mainttrack_dev' THEN
        RAISE EXCEPTION 'Refusing database %.', current_database();
    END IF;

    IF to_regclass('public.machine_parameter_machine_visibility') IS NULL THEN
        RAISE EXCEPTION
            'machine_parameter_machine_visibility is missing. Run scripts/production-machine-parameter-machine-exclusions.sql first.';
    END IF;

    IF to_regclass('public.array_feature_visibility') IS NULL THEN
        RAISE EXCEPTION 'array_feature_visibility is missing. Apply the earlier feature-visibility schema before this script.';
    END IF;
END $$;

LOCK TABLE machine_parameter_photos IN SHARE ROW EXCLUSIVE MODE;
LOCK TABLE machines IN SHARE ROW EXCLUSIVE MODE;
LOCK TABLE tenants IN SHARE ROW EXCLUSIVE MODE;
LOCK TABLE machine_parameter_machine_visibility IN SHARE ROW EXCLUSIVE MODE;
LOCK TABLE array_feature_visibility IN SHARE ROW EXCLUSIVE MODE;

CREATE TEMP TABLE production_tenant (
    id uuid PRIMARY KEY
) ON COMMIT DROP;

INSERT INTO production_tenant (id)
VALUES ('beed1fc4-ffbb-4ea1-b7c8-d84584506842'::uuid);

CREATE TEMP TABLE compressor_machine_counts (
    name text PRIMARY KEY,
    photo_count integer NOT NULL
) ON COMMIT DROP;

INSERT INTO compressor_machine_counts (name, photo_count) VALUES
    ('machine.rooms.compressor1', 9),
    ('machine.rooms.compressor2', 9),
    ('machine.rooms.compressor3', 9),
    ('machine.rooms.compressor4', 9),
    ('machine.waterCooling.compressor1', 1),
    ('machine.waterCooling.compressor2', 1),
    ('machine.waterCooling.compressor3', 1),
    ('machine.waterCooling.compressor4', 1);

SELECT
    'preflight_database' AS section,
    current_database() AS database,
    (SELECT COUNT(*) FROM tenants) AS tenant_count,
    (SELECT COUNT(*) FROM tenants WHERE id = (SELECT id FROM production_tenant)) AS production_tenant_rows;

SELECT
    'preflight_compressor_counts' AS section,
    expected.name AS machine_name,
    expected.photo_count AS expected_photos,
    COUNT(DISTINCT machine.id) AS machine_rows,
    COUNT(photo.id) AS actual_photos
FROM compressor_machine_counts AS expected
LEFT JOIN machines AS machine
    ON machine.name = expected.name
   AND machine.tenant_id = (SELECT id FROM production_tenant)
LEFT JOIN machine_parameter_photos AS photo
    ON photo.machine_id = machine.id
   AND photo.tenant_id = machine.tenant_id
GROUP BY expected.name, expected.photo_count
ORDER BY expected.name;

DO $$
DECLARE
    machine_id_nullable text;
    parameter_column_count integer;
    index_definition text;
    comparison_mismatch_count integer;
    other_tenant_machine_count integer;
    cross_tenant_photo_count integer;
    duplicate_sort_count integer;
BEGIN
    IF current_database() <> 'mainttrack_dev' THEN
        RAISE EXCEPTION 'Refusing database %.', current_database();
    END IF;

    IF to_regclass('public.machine_parameter_machine_visibility') IS NULL
       OR to_regclass('public.array_feature_visibility') IS NULL
       OR to_regclass('public.machine_parameter_photos') IS NULL
       OR to_regclass('public.machines') IS NULL
       OR to_regclass('public.tenants') IS NULL THEN
        RAISE EXCEPTION 'A required existing table is missing.';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM information_schema.tables
        WHERE table_schema = 'public'
          AND table_name LIKE 'parameter_photo%'
    ) THEN
        RAISE EXCEPTION 'A parameter photo catalog table already exists.';
    END IF;

    IF (SELECT COUNT(*) FROM tenants WHERE id = (SELECT id FROM production_tenant)) <> 1 THEN
        RAISE EXCEPTION 'Production tenant was not found exactly once.';
    END IF;

    SELECT COUNT(*)
    INTO parameter_column_count
    FROM information_schema.columns
    WHERE table_schema = 'public'
      AND table_name = 'machine_parameter_photos'
      AND column_name = 'parameter_id';

    IF parameter_column_count <> 0 THEN
        RAISE EXCEPTION 'machine_parameter_photos.parameter_id already exists.';
    END IF;

    SELECT is_nullable
    INTO machine_id_nullable
    FROM information_schema.columns
    WHERE table_schema = 'public'
      AND table_name = 'machine_parameter_photos'
      AND column_name = 'machine_id';

    IF machine_id_nullable IS DISTINCT FROM 'NO' THEN
        RAISE EXCEPTION 'machine_parameter_photos.machine_id is not NOT NULL.';
    END IF;

    SELECT indexdef
    INTO index_definition
    FROM pg_indexes
    WHERE schemaname = 'public'
      AND tablename = 'machine_parameter_photos'
      AND indexname = 'IX_machine_parameter_photos_machine_id_sort_order';

    IF index_definition IS NULL OR position('WHERE' IN index_definition) > 0 THEN
        RAISE EXCEPTION 'The machine/sort unique index is missing or already filtered.';
    END IF;

    SELECT COUNT(*)
    INTO comparison_mismatch_count
    FROM (
        SELECT expected.name
        FROM compressor_machine_counts AS expected
        LEFT JOIN machines AS machine
            ON machine.name = expected.name
           AND machine.tenant_id = (SELECT id FROM production_tenant)
        LEFT JOIN machine_parameter_photos AS photo
            ON photo.machine_id = machine.id
           AND photo.tenant_id = machine.tenant_id
        GROUP BY expected.name, expected.photo_count
        HAVING COUNT(DISTINCT machine.id) <> 1
            OR COUNT(photo.id) <> expected.photo_count
    ) AS mismatches;

    IF comparison_mismatch_count <> 0 THEN
        RAISE EXCEPTION 'Compressor machine rows or photo counts do not match production preflight.';
    END IF;

    SELECT COUNT(*)
    INTO other_tenant_machine_count
    FROM machines AS machine
    INNER JOIN compressor_machine_counts AS expected
        ON expected.name = machine.name
    WHERE machine.tenant_id <> (SELECT id FROM production_tenant);

    IF other_tenant_machine_count <> 0 THEN
        RAISE EXCEPTION 'A compressor machine name exists for another tenant.';
    END IF;

    SELECT COUNT(*)
    INTO cross_tenant_photo_count
    FROM machine_parameter_photos AS photo
    INNER JOIN machines AS machine
        ON machine.id = photo.machine_id
    WHERE machine.tenant_id <> photo.tenant_id;

    IF cross_tenant_photo_count <> 0 THEN
        RAISE EXCEPTION 'A photo points at a machine owned by another tenant.';
    END IF;

    SELECT COUNT(*)
    INTO duplicate_sort_count
    FROM (
        SELECT machine_id, sort_order
        FROM machine_parameter_photos
        WHERE machine_id IS NOT NULL
        GROUP BY machine_id, sort_order
        HAVING COUNT(*) > 1
    ) AS duplicates;

    IF duplicate_sort_count <> 0 THEN
        RAISE EXCEPTION 'Existing photos already violate (machine_id, sort_order).';
    END IF;
END $$;

CREATE TEMP TABLE photo_before ON COMMIT DROP AS
SELECT
    id,
    tenant_id,
    machine_id,
    image_url,
    sort_order,
    created_by_user_id,
    created_at
FROM machine_parameter_photos;

ALTER TABLE machine_parameter_photos
    ALTER COLUMN machine_id DROP NOT NULL;

ALTER TABLE machine_parameter_photos
    ADD COLUMN parameter_id uuid NULL;

DROP INDEX "IX_machine_parameter_photos_machine_id_sort_order";

CREATE UNIQUE INDEX "IX_machine_parameter_photos_machine_id_sort_order"
    ON machine_parameter_photos (machine_id, sort_order)
    WHERE "machine_id" IS NOT NULL;

CREATE UNIQUE INDEX "IX_machine_parameter_photos_parameter_id_sort_order"
    ON machine_parameter_photos (parameter_id, sort_order)
    WHERE "parameter_id" IS NOT NULL;

CREATE INDEX "IX_machine_parameter_photos_tenant_id_parameter_id"
    ON machine_parameter_photos (tenant_id, parameter_id);

CREATE INDEX "IX_machine_parameter_photos_parameter_id_tenant_id"
    ON machine_parameter_photos (parameter_id, tenant_id);

ALTER TABLE machine_parameter_photos
    ADD CONSTRAINT ck_machine_parameter_photos_association
    CHECK (parameter_id IS NOT NULL OR machine_id IS NOT NULL);

CREATE TABLE parameter_photo_groups (
    id uuid NOT NULL,
    name_key character varying(200) NOT NULL,
    sort_order integer NOT NULL,
    is_active boolean NOT NULL,
    tenant_id uuid NOT NULL,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT "PK_parameter_photo_groups" PRIMARY KEY (id)
);

CREATE UNIQUE INDEX "IX_parameter_photo_groups_tenant_id_name_key"
    ON parameter_photo_groups (tenant_id, name_key);

ALTER TABLE parameter_photo_groups
    ADD CONSTRAINT "AK_parameter_photo_groups_id_tenant_id"
    UNIQUE (id, tenant_id);

CREATE TABLE parameter_photo_parameters (
    id uuid NOT NULL,
    group_id uuid NOT NULL,
    name_key character varying(200) NOT NULL,
    sort_order integer NOT NULL,
    is_active boolean NOT NULL,
    tenant_id uuid NOT NULL,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT "PK_parameter_photo_parameters" PRIMARY KEY (id)
);

CREATE INDEX "IX_parameter_photo_parameters_group_id"
    ON parameter_photo_parameters (group_id);

CREATE INDEX "IX_parameter_photo_parameters_group_id_tenant_id"
    ON parameter_photo_parameters (group_id, tenant_id);

CREATE UNIQUE INDEX "IX_parameter_photo_parameters_tenant_id_name_key"
    ON parameter_photo_parameters (tenant_id, name_key);

ALTER TABLE parameter_photo_parameters
    ADD CONSTRAINT "AK_parameter_photo_parameters_id_tenant_id"
    UNIQUE (id, tenant_id);

ALTER TABLE parameter_photo_parameters
    ADD CONSTRAINT "FK_parameter_photo_parameters_parameter_photo_groups_group_id_~"
    FOREIGN KEY (group_id, tenant_id)
    REFERENCES parameter_photo_groups (id, tenant_id)
    ON DELETE RESTRICT;

CREATE TABLE parameter_photo_parameter_exclusions (
    id uuid NOT NULL,
    parameter_id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT "PK_parameter_photo_parameter_exclusions" PRIMARY KEY (id)
);

CREATE INDEX "IX_parameter_photo_parameter_exclusions_parameter_id_tenant_id"
    ON parameter_photo_parameter_exclusions (parameter_id, tenant_id);

CREATE UNIQUE INDEX "IX_parameter_photo_parameter_exclusions_tenant_id_parameter_id"
    ON parameter_photo_parameter_exclusions (tenant_id, parameter_id);

ALTER TABLE parameter_photo_parameter_exclusions
    ADD CONSTRAINT "FK_parameter_photo_parameter_exclusions_parameter_photo_parame~"
    FOREIGN KEY (parameter_id, tenant_id)
    REFERENCES parameter_photo_parameters (id, tenant_id)
    ON DELETE RESTRICT;

ALTER TABLE machine_parameter_photos
    ADD CONSTRAINT "FK_machine_parameter_photos_parameter_photo_parameters_paramet~"
    FOREIGN KEY (parameter_id, tenant_id)
    REFERENCES parameter_photo_parameters (id, tenant_id)
    ON DELETE RESTRICT;

INSERT INTO parameter_photo_groups (id, tenant_id, name_key, sort_order, is_active, created_at)
SELECT gen_random_uuid(), tenant.id, spec.name_key, spec.sort_order, true, NOW()
FROM tenants AS tenant
CROSS JOIN (VALUES
    ('array.washing_system', 1),
    ('parameterPhoto.group.coolingSystem', 2),
    ('array.ginoshar', 3),
    ('array.packing_house', 4)
) AS spec(name_key, sort_order);

INSERT INTO parameter_photo_parameters (id, tenant_id, group_id, name_key, sort_order, is_active, created_at)
SELECT gen_random_uuid(), catalog_group.tenant_id, catalog_group.id, spec.name_key, spec.sort_order, true, NOW()
FROM parameter_photo_groups AS catalog_group
JOIN (VALUES
    ('array.washing_system', 'parameterPhoto.parameter.hopper', 1),
    ('array.washing_system', 'parameterPhoto.parameter.coolingSystemComputer', 2),
    ('parameterPhoto.group.coolingSystem', 'parameterPhoto.parameter.compressorRooms', 1),
    ('parameterPhoto.group.coolingSystem', 'parameterPhoto.parameter.controlRoomAboveCoolingRooms', 2),
    ('parameterPhoto.group.coolingSystem', 'parameterPhoto.parameter.humidityControllers', 3),
    ('array.ginoshar', 'machine.ginoshar.netPackingMachine', 1),
    ('array.ginoshar', 'machine.ginoshar.sensorPackingMachine', 2),
    ('array.ginoshar', 'machine.ginoshar.airCompressor', 3),
    ('array.ginoshar', 'parameterPhoto.parameter.bagPullersUnderViscose', 4),
    ('array.packing_house', 'machine.sorter1', 1),
    ('array.packing_house', 'machine.sorter2', 2),
    ('array.packing_house', 'machine.sorter3', 3),
    ('array.packing_house', 'machine.packing.poolElevators', 4),
    ('array.packing_house', 'machine.packing.conveyorSystem', 5),
    ('array.packing_house', 'machine.packing.fastVerbrocken', 6),
    ('array.packing_house', 'machine.packing.verbrocken', 7),
    ('array.packing_house', 'machine.packing.bulkFiller', 8),
    ('array.packing_house', 'machine.packing.metalDetector', 9),
    ('array.packing_house', 'machine.scale1', 10),
    ('array.packing_house', 'machine.packing1', 11),
    ('array.packing_house', 'machine.scale2', 12),
    ('array.packing_house', 'machine.packing2', 13),
    ('array.packing_house', 'machine.scale3', 14),
    ('array.packing_house', 'machine.packing3', 15),
    ('array.packing_house', 'machine.scale4', 16),
    ('array.packing_house', 'machine.packing4', 17),
    ('array.packing_house', 'machine.scale5', 18),
    ('array.packing_house', 'machine.packing5a', 19),
    ('array.packing_house', 'machine.packing5b', 20),
    ('array.packing_house', 'machine.sorterLine5', 21),
    ('array.packing_house', 'machine.scale6', 22),
    ('array.packing_house', 'machine.packing6a', 23),
    ('array.packing_house', 'machine.packing6b', 24),
    ('array.packing_house', 'machine.sorterLine6', 25),
    ('array.packing_house', 'machine.masters', 26),
    ('array.packing_house', 'machine.accumulatorOutside', 27),
    ('array.packing_house', 'machine.viscose', 28)
) AS spec(group_key, name_key, sort_order)
    ON catalog_group.name_key = spec.group_key
   AND catalog_group.tenant_id IS NOT NULL;

DO $$
DECLARE
    tenant_count integer;
BEGIN
    SELECT COUNT(*) INTO tenant_count FROM tenants;

    IF EXISTS (
        SELECT tenant.id
        FROM tenants AS tenant
        LEFT JOIN parameter_photo_groups AS catalog_group
            ON catalog_group.tenant_id = tenant.id
        GROUP BY tenant.id
        HAVING COUNT(catalog_group.id) <> 4
    ) OR EXISTS (
        SELECT tenant.id
        FROM tenants AS tenant
        LEFT JOIN parameter_photo_parameters AS parameter
            ON parameter.tenant_id = tenant.id
        GROUP BY tenant.id
        HAVING COUNT(parameter.id) <> 37
    ) OR (
        SELECT COUNT(*)
        FROM parameter_photo_parameters
        WHERE tenant_id = (SELECT id FROM production_tenant)
          AND name_key = 'parameterPhoto.parameter.compressorRooms'
          AND is_active = true
    ) <> 1 THEN
        RAISE EXCEPTION 'Catalog seed did not create 4 groups and 37 parameters for every tenant.';
    END IF;

    IF (SELECT COUNT(*) FROM parameter_photo_groups) <> tenant_count * 4
       OR (SELECT COUNT(*) FROM parameter_photo_parameters) <> tenant_count * 37 THEN
        RAISE EXCEPTION 'Catalog row totals do not match the tenant count.';
    END IF;
END $$;

CREATE TEMP TABLE name_match_plan ON COMMIT DROP AS
SELECT
    photo.id AS photo_id,
    photo.tenant_id,
    photo.machine_id,
    machine.name AS machine_name,
    photo.sort_order,
    photo.image_url,
    photo.created_by_user_id,
    photo.created_at,
    parameter.id AS parameter_id
FROM machine_parameter_photos AS photo
INNER JOIN machines AS machine
    ON machine.id = photo.machine_id
   AND machine.tenant_id = photo.tenant_id
INNER JOIN parameter_photo_parameters AS parameter
    ON parameter.tenant_id = photo.tenant_id
   AND parameter.name_key = machine.name
WHERE photo.parameter_id IS NULL
  AND photo.machine_id IS NOT NULL
  AND parameter.name_key <> 'parameterPhoto.parameter.hopper'
  AND machine.name NOT IN ('machine.largeHopper', 'machine.smallHopper', 'machine.onion.hopper')
  AND (
      SELECT COUNT(*)
      FROM machines AS named
      WHERE named.tenant_id = machine.tenant_id
        AND named.name = machine.name
  ) = 1
  AND NOT EXISTS (
      SELECT 1
      FROM machine_parameter_photos AS conflict
      WHERE conflict.id <> photo.id
        AND conflict.sort_order = photo.sort_order
        AND conflict.parameter_id = parameter.id
  );

DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM name_match_plan
        GROUP BY parameter_id, sort_order
        HAVING COUNT(*) > 1
    ) THEN
        RAISE EXCEPTION 'Name-matched photos would share a parameter sort order.';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM name_match_plan AS plan
        INNER JOIN compressor_machine_counts AS compressor
            ON compressor.name = plan.machine_name
    ) THEN
        RAISE EXCEPTION 'A compressor photo was selected by the name-match rule.';
    END IF;
END $$;

DO $$
DECLARE
    updated_count integer;
    planned_count integer;
BEGIN
    UPDATE machine_parameter_photos AS photo
    SET parameter_id = plan.parameter_id
    FROM name_match_plan AS plan
    INNER JOIN photo_before AS original
        ON original.id = plan.photo_id
    WHERE photo.id = plan.photo_id
      AND photo.tenant_id = original.tenant_id
      AND photo.machine_id = original.machine_id
      AND photo.image_url = original.image_url
      AND photo.sort_order = original.sort_order
      AND photo.created_by_user_id = original.created_by_user_id
      AND photo.created_at = original.created_at
      AND photo.parameter_id IS NULL;

    GET DIAGNOSTICS updated_count = ROW_COUNT;
    SELECT COUNT(*) INTO planned_count FROM name_match_plan;

    IF updated_count <> planned_count THEN
        RAISE EXCEPTION 'Name-match update changed % photos; the plan contains %.', updated_count, planned_count;
    END IF;
END $$;

CREATE TEMP TABLE compressor_link_plan ON COMMIT DROP AS
WITH parameter_max AS (
    SELECT parameter_id, MAX(sort_order) AS max_sort_order
    FROM machine_parameter_photos
    WHERE parameter_id IS NOT NULL
    GROUP BY parameter_id
),
machine_max AS (
    SELECT photo.tenant_id, MAX(photo.sort_order) AS max_sort_order
    FROM machine_parameter_photos AS photo
    INNER JOIN machines AS source_machine
        ON source_machine.id = photo.machine_id
       AND source_machine.tenant_id = photo.tenant_id
    INNER JOIN compressor_machine_counts AS expected
        ON expected.name = source_machine.name
    GROUP BY photo.tenant_id
),
ranked AS (
    SELECT
        photo_row.id AS photo_id,
        photo_row.tenant_id,
        photo_row.machine_id,
        machine.name AS machine_name,
        photo_row.sort_order AS current_sort_order,
        photo_row.image_url,
        photo_row.created_by_user_id,
        photo_row.created_at,
        parameter.id AS parameter_id,
        COALESCE(parameter_max.max_sort_order, 0) AS parameter_max_sort,
        COALESCE(machine_max.max_sort_order, 0) AS machine_max_sort,
        CASE
            WHEN COALESCE(parameter_max.max_sort_order, 0) >= COALESCE(machine_max.max_sort_order, 0)
            THEN COALESCE(parameter_max.max_sort_order, 0)
            ELSE COALESCE(machine_max.max_sort_order, 0)
        END AS sort_offset,
        ROW_NUMBER() OVER (
            PARTITION BY photo_row.tenant_id
            ORDER BY machine.name, photo_row.sort_order, photo_row.id
        ) AS photo_rank
    FROM machine_parameter_photos AS photo_row
    INNER JOIN machines AS machine
        ON machine.id = photo_row.machine_id
       AND machine.tenant_id = photo_row.tenant_id
    INNER JOIN compressor_machine_counts AS expected
        ON expected.name = machine.name
    INNER JOIN parameter_photo_parameters AS parameter
        ON parameter.tenant_id = photo_row.tenant_id
       AND parameter.name_key = 'parameterPhoto.parameter.compressorRooms'
    LEFT JOIN parameter_max
        ON parameter_max.parameter_id = parameter.id
    LEFT JOIN machine_max
        ON machine_max.tenant_id = photo_row.tenant_id
    WHERE photo_row.parameter_id IS NULL
      AND photo_row.machine_id IS NOT NULL
)
SELECT
    photo_id,
    tenant_id,
    machine_id,
    machine_name,
    current_sort_order,
    image_url,
    created_by_user_id,
    created_at,
    parameter_id,
    parameter_max_sort,
    machine_max_sort,
    sort_offset,
    photo_rank,
    sort_offset + photo_rank AS new_sort_order
FROM ranked;

DO $$
DECLARE
    planned_count integer;
    machine_collision_count integer;
    parameter_collision_count integer;
BEGIN
    SELECT COUNT(*) INTO planned_count
    FROM compressor_link_plan
    WHERE tenant_id = (SELECT id FROM production_tenant);

    IF planned_count <> 40 THEN
        RAISE EXCEPTION 'Compressor plan contains % production photos instead of 40.', planned_count;
    END IF;

    IF EXISTS (
        SELECT plan.machine_name
        FROM compressor_link_plan AS plan
        WHERE plan.tenant_id = (SELECT id FROM production_tenant)
        GROUP BY plan.machine_name
        HAVING COUNT(*) <> (
            SELECT expected.photo_count
            FROM compressor_machine_counts AS expected
            WHERE expected.name = plan.machine_name
        )
    ) THEN
        RAISE EXCEPTION 'Compressor plan does not match the per-machine counts.';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM compressor_link_plan
        WHERE new_sort_order <= parameter_max_sort
           OR new_sort_order <= machine_max_sort
           OR new_sort_order <> sort_offset + photo_rank
    ) THEN
        RAISE EXCEPTION 'A compressor sort order is not strictly above both maxima.';
    END IF;

    IF EXISTS (
        SELECT tenant_id
        FROM compressor_link_plan
        GROUP BY tenant_id
        HAVING COUNT(*) <> COUNT(DISTINCT new_sort_order)
            OR MIN(new_sort_order) <> MIN(sort_offset) + 1
            OR MAX(new_sort_order) <> MIN(sort_offset) + COUNT(*)
    ) THEN
        RAISE EXCEPTION 'Compressor sort orders are not one continuous range per tenant.';
    END IF;

    SELECT COUNT(*)
    INTO machine_collision_count
    FROM compressor_link_plan AS plan
    INNER JOIN machine_parameter_photos AS existing
        ON existing.machine_id = plan.machine_id
       AND existing.sort_order = plan.new_sort_order
       AND existing.id <> plan.photo_id;

    SELECT COUNT(*)
    INTO parameter_collision_count
    FROM compressor_link_plan AS plan
    INNER JOIN machine_parameter_photos AS existing
        ON existing.parameter_id = plan.parameter_id
       AND existing.sort_order = plan.new_sort_order
       AND existing.id <> plan.photo_id;

    IF machine_collision_count <> 0 OR parameter_collision_count <> 0 THEN
        RAISE EXCEPTION 'A compressor sort order collides with an existing photo.';
    END IF;
END $$;

DO $$
DECLARE
    updated_count integer;
    planned_count integer;
BEGIN
    UPDATE machine_parameter_photos AS photo
    SET parameter_id = plan.parameter_id,
        sort_order = plan.new_sort_order
    FROM compressor_link_plan AS plan
    INNER JOIN photo_before AS original
        ON original.id = plan.photo_id
    WHERE photo.id = plan.photo_id
      AND photo.tenant_id = original.tenant_id
      AND photo.machine_id = original.machine_id
      AND photo.image_url = original.image_url
      AND photo.created_by_user_id = original.created_by_user_id
      AND photo.created_at = original.created_at
      AND photo.sort_order = original.sort_order
      AND photo.parameter_id IS NULL;

    GET DIAGNOSTICS updated_count = ROW_COUNT;
    SELECT COUNT(*) INTO planned_count FROM compressor_link_plan;

    IF updated_count <> planned_count THEN
        RAISE EXCEPTION 'Compressor update changed % photos; the plan contains %.', updated_count, planned_count;
    END IF;
END $$;

INSERT INTO parameter_photo_parameter_exclusions (id, tenant_id, parameter_id, created_at)
SELECT gen_random_uuid(), parameter.tenant_id, parameter.id, NOW()
FROM parameter_photo_parameters AS parameter
WHERE (
        EXISTS (
            SELECT 1
            FROM machines AS machine
            INNER JOIN machine_parameter_machine_visibility AS exclusion
                ON exclusion.machine_id = machine.id
               AND exclusion.tenant_id = machine.tenant_id
            WHERE machine.tenant_id = parameter.tenant_id
              AND machine.name = parameter.name_key
        )
        OR EXISTS (
            SELECT 1
            FROM machines AS machine
            INNER JOIN array_feature_visibility AS visibility
                ON visibility.array_id = machine.array_id
               AND visibility.tenant_id = machine.tenant_id
            WHERE machine.tenant_id = parameter.tenant_id
              AND machine.name = parameter.name_key
              AND visibility.feature_key = 'machine_parameter_photos'
              AND visibility.is_visible = false
        )
    )
  AND NOT EXISTS (
        SELECT 1
        FROM parameter_photo_parameter_exclusions AS existing
        WHERE existing.tenant_id = parameter.tenant_id
          AND existing.parameter_id = parameter.id
    );

UPDATE machine_parameter_photos AS photo
SET parameter_id = NULL
WHERE photo.machine_id IS NOT NULL
  AND photo.parameter_id IS NOT NULL
  AND EXISTS (
        SELECT 1
        FROM parameter_photo_parameter_exclusions AS exclusion
        WHERE exclusion.parameter_id = photo.parameter_id
          AND exclusion.tenant_id = photo.tenant_id
    );

SELECT
    'reconciliation_totals' AS section,
    (SELECT COUNT(*) FROM photo_before) AS photos_before,
    (SELECT COUNT(*) FROM machine_parameter_photos) AS photos_after,
    (SELECT COUNT(*) FROM name_match_plan) AS name_match_planned,
    (SELECT COUNT(*) FROM compressor_link_plan) AS compressor_planned,
    (SELECT COUNT(*) FROM machine_parameter_photos WHERE parameter_id IS NOT NULL) AS linked_after,
    (SELECT COUNT(*) FROM parameter_photo_parameter_exclusions) AS exclusions;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM parameter_photo_parameters AS parameter
        INNER JOIN parameter_photo_groups AS catalog_group
            ON catalog_group.id = parameter.group_id
        WHERE parameter.tenant_id <> catalog_group.tenant_id
    ) THEN
        RAISE EXCEPTION 'A catalog parameter references a group from another tenant.';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM machine_parameter_photos AS photo
        INNER JOIN parameter_photo_parameters AS parameter
            ON parameter.id = photo.parameter_id
        WHERE photo.parameter_id IS NOT NULL
          AND photo.tenant_id <> parameter.tenant_id
    ) THEN
        RAISE EXCEPTION 'A photo references a parameter from another tenant.';
    END IF;
END $$;

SELECT
    'unlinked_photos' AS section,
    photo.tenant_id,
    machine.name AS machine_name,
    photo.id AS photo_id,
    photo.sort_order,
    CASE
        WHEN machine.name IN ('machine.largeHopper', 'machine.smallHopper', 'machine.onion.hopper')
            THEN 'hopper_machine_not_linked'
        WHEN (
            SELECT COUNT(*)
            FROM machines AS named
            WHERE named.tenant_id = machine.tenant_id
              AND named.name = machine.name
        ) <> 1
            THEN 'duplicate_machine_name'
        WHEN NOT EXISTS (
            SELECT 1
            FROM parameter_photo_parameters AS parameter
            WHERE parameter.tenant_id = photo.tenant_id
              AND parameter.name_key = machine.name
              AND parameter.name_key <> 'parameterPhoto.parameter.hopper'
        )
            THEN 'no_catalog_name_match'
        ELSE 'excluded_or_unresolved'
    END AS reason
FROM machine_parameter_photos AS photo
INNER JOIN machines AS machine
    ON machine.id = photo.machine_id
   AND machine.tenant_id = photo.tenant_id
WHERE photo.parameter_id IS NULL
ORDER BY photo.tenant_id, machine.name, photo.sort_order, photo.id;

DO $$
DECLARE
    photo_count_before integer;
    photo_count_after integer;
    changed_identity_count integer;
    unexpected_sort_change_count integer;
    unexpected_link_count integer;
    missed_name_match_count integer;
    missed_compressor_count integer;
    excluded_still_linked_count integer;
    hopper_linked_count integer;
BEGIN
    SELECT COUNT(*) INTO photo_count_before FROM photo_before;
    SELECT COUNT(*) INTO photo_count_after FROM machine_parameter_photos;

    IF photo_count_before <> photo_count_after
       OR EXISTS (
            SELECT 1
            FROM photo_before AS original
            WHERE NOT EXISTS (
                SELECT 1
                FROM machine_parameter_photos AS photo
                WHERE photo.id = original.id
            )
       )
       OR EXISTS (
            SELECT 1
            FROM machine_parameter_photos AS photo
            WHERE NOT EXISTS (
                SELECT 1
                FROM photo_before AS original
                WHERE original.id = photo.id
            )
       ) THEN
        RAISE EXCEPTION 'Photo ids do not reconcile. Before %, after %.', photo_count_before, photo_count_after;
    END IF;

    SELECT COUNT(*)
    INTO changed_identity_count
    FROM photo_before AS original
    INNER JOIN machine_parameter_photos AS photo
        ON photo.id = original.id
    WHERE photo.tenant_id IS DISTINCT FROM original.tenant_id
       OR photo.machine_id IS DISTINCT FROM original.machine_id
       OR photo.image_url IS DISTINCT FROM original.image_url
       OR photo.created_by_user_id IS DISTINCT FROM original.created_by_user_id
       OR photo.created_at IS DISTINCT FROM original.created_at;

    IF changed_identity_count <> 0 THEN
        RAISE EXCEPTION 'A photo identity field changed.';
    END IF;

    SELECT COUNT(*)
    INTO unexpected_sort_change_count
    FROM photo_before AS original
    INNER JOIN machine_parameter_photos AS photo
        ON photo.id = original.id
    LEFT JOIN compressor_link_plan AS compressor
        ON compressor.photo_id = original.id
    WHERE photo.sort_order IS DISTINCT FROM original.sort_order
      AND (
          compressor.photo_id IS NULL
          OR photo.sort_order IS DISTINCT FROM compressor.new_sort_order
      );

    IF unexpected_sort_change_count <> 0 THEN
        RAISE EXCEPTION 'A photo outside the compressor plan changed sort order.';
    END IF;

    SELECT COUNT(*)
    INTO unexpected_link_count
    FROM machine_parameter_photos AS photo
    WHERE photo.parameter_id IS NOT NULL
      AND NOT EXISTS (
          SELECT 1
          FROM name_match_plan AS plan
          WHERE plan.photo_id = photo.id
            AND plan.parameter_id = photo.parameter_id
      )
      AND NOT EXISTS (
          SELECT 1
          FROM compressor_link_plan AS plan
          WHERE plan.photo_id = photo.id
            AND plan.parameter_id = photo.parameter_id
            AND plan.new_sort_order = photo.sort_order
      );

    IF unexpected_link_count <> 0 THEN
        RAISE EXCEPTION '% photos are linked outside the approved mappings.', unexpected_link_count;
    END IF;

    SELECT COUNT(*)
    INTO missed_name_match_count
    FROM name_match_plan AS plan
    INNER JOIN machine_parameter_photos AS photo
        ON photo.id = plan.photo_id
    WHERE NOT EXISTS (
            SELECT 1
            FROM parameter_photo_parameter_exclusions AS exclusion
            WHERE exclusion.parameter_id = plan.parameter_id
              AND exclusion.tenant_id = plan.tenant_id
        )
      AND (
          photo.parameter_id IS DISTINCT FROM plan.parameter_id
          OR photo.sort_order IS DISTINCT FROM plan.sort_order
      );

    IF missed_name_match_count <> 0 THEN
        RAISE EXCEPTION 'A unique name-matched photo was not preserved on its catalog parameter.';
    END IF;

    SELECT COUNT(*)
    INTO missed_compressor_count
    FROM compressor_link_plan AS plan
    INNER JOIN machine_parameter_photos AS photo
        ON photo.id = plan.photo_id
    WHERE photo.parameter_id IS DISTINCT FROM plan.parameter_id
       OR photo.sort_order IS DISTINCT FROM plan.new_sort_order;

    IF missed_compressor_count <> 0 THEN
        RAISE EXCEPTION 'A compressor photo is not linked with its planned sort order.';
    END IF;

    SELECT COUNT(*)
    INTO excluded_still_linked_count
    FROM machine_parameter_photos AS photo
    INNER JOIN parameter_photo_parameter_exclusions AS exclusion
        ON exclusion.parameter_id = photo.parameter_id
       AND exclusion.tenant_id = photo.tenant_id
    WHERE photo.machine_id IS NOT NULL
      AND photo.parameter_id IS NOT NULL;

    IF excluded_still_linked_count <> 0 THEN
        RAISE EXCEPTION 'An excluded parameter still has a legacy photo link.';
    END IF;

    SELECT COUNT(*)
    INTO hopper_linked_count
    FROM machine_parameter_photos AS photo
    INNER JOIN machines AS machine
        ON machine.id = photo.machine_id
       AND machine.tenant_id = photo.tenant_id
    WHERE photo.parameter_id IS NOT NULL
      AND (
          machine.name IN ('machine.largeHopper', 'machine.smallHopper', 'machine.onion.hopper')
          OR EXISTS (
              SELECT 1
              FROM parameter_photo_parameters AS parameter
              WHERE parameter.id = photo.parameter_id
                AND parameter.tenant_id = photo.tenant_id
                AND parameter.name_key = 'parameterPhoto.parameter.hopper'
          )
      );

    IF hopper_linked_count <> 0 THEN
        RAISE EXCEPTION 'A hopper photo or the standalone Hopper parameter was linked.';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM machine_parameter_photos
        WHERE machine_id IS NOT NULL
        GROUP BY machine_id, sort_order
        HAVING COUNT(*) > 1
    ) OR EXISTS (
        SELECT 1
        FROM machine_parameter_photos
        WHERE parameter_id IS NOT NULL
        GROUP BY parameter_id, sort_order
        HAVING COUNT(*) > 1
    ) THEN
        RAISE EXCEPTION 'A unique sort pair exists after deployment.';
    END IF;
END $$;

COMMIT;
