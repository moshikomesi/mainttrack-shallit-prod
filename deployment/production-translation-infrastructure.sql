-- MaintTrack production translation infrastructure
-- PostgreSQL 16. One transaction. Do not run with dotnet ef database update.
--
-- Schema matches migration 20261109193225_AddTranslationInfrastructure and
-- MaintTrackDbContext.ConfigureTranslations:
--   translated_texts
--   translation_jobs
--
-- The only allowed source field is MaintenanceEntry.Description. This script
-- does not read or update MaintenanceEntries, and it does not backfill jobs.
-- Existing descriptions stay where they are. New rows are created later by
-- the application when a description is saved.
--
-- Tenant isolation is the tenant_id column on every row, included in both
-- unique source-version indexes. The model has no foreign key to tenants.
-- No photo, clearance, user, or machine row is changed.
-- __EFMigrationsHistory is not modified.
--
-- Re-running is safe when both tables are absent or already match this schema.
-- A partial or different translation schema aborts the transaction.

BEGIN;

SET LOCAL lock_timeout = '15s';
SET LOCAL statement_timeout = '120s';

DO $$
DECLARE
    jobs_exist boolean := to_regclass('public.translation_jobs') IS NOT NULL;
    texts_exist boolean := to_regclass('public.translated_texts') IS NOT NULL;
    expected_jobs integer;
    expected_texts integer;
BEGIN
    IF current_database() <> 'mainttrack_dev' THEN
        RAISE EXCEPTION 'Refusing database %.', current_database();
    END IF;

    IF to_regclass('public.tenants') IS NULL THEN
        RAISE EXCEPTION 'tenants is missing.';
    END IF;

    IF jobs_exist <> texts_exist THEN
        RAISE EXCEPTION 'Translation schema is partial: one of translation_jobs or translated_texts is missing.';
    END IF;

    IF NOT jobs_exist THEN
        RETURN;
    END IF;

    SELECT COUNT(*)
    INTO expected_jobs
    FROM information_schema.columns
    WHERE table_schema = 'public'
      AND table_name = 'translation_jobs'
      AND (
          (column_name = 'id' AND data_type = 'uuid' AND is_nullable = 'NO')
          OR (column_name = 'entity_type' AND data_type = 'character varying' AND character_maximum_length = 100 AND is_nullable = 'NO')
          OR (column_name = 'entity_id' AND data_type = 'uuid' AND is_nullable = 'NO')
          OR (column_name = 'field_name' AND data_type = 'character varying' AND character_maximum_length = 100 AND is_nullable = 'NO')
          OR (column_name = 'source_text' AND data_type = 'text' AND is_nullable = 'NO')
          OR (column_name = 'source_hash' AND data_type = 'character varying' AND character_maximum_length = 64 AND is_nullable = 'NO')
          OR (column_name = 'target_language' AND data_type = 'character varying' AND character_maximum_length = 8 AND is_nullable = 'NO')
          OR (column_name = 'status' AND data_type = 'character varying' AND character_maximum_length = 20 AND is_nullable = 'NO')
          OR (column_name = 'attempt_count' AND data_type = 'integer' AND is_nullable = 'NO')
          OR (column_name = 'claim_token' AND data_type = 'uuid' AND is_nullable = 'YES')
          OR (column_name = 'processed_at' AND data_type = 'timestamp with time zone' AND is_nullable = 'YES')
          OR (column_name = 'last_error' AND data_type = 'character varying' AND character_maximum_length = 2000 AND is_nullable = 'YES')
          OR (column_name = 'tenant_id' AND data_type = 'uuid' AND is_nullable = 'NO')
          OR (column_name = 'created_at' AND data_type = 'timestamp with time zone' AND is_nullable = 'NO')
          OR (column_name = 'updated_at' AND data_type = 'timestamp with time zone' AND is_nullable = 'YES')
      );

    SELECT COUNT(*)
    INTO expected_texts
    FROM information_schema.columns
    WHERE table_schema = 'public'
      AND table_name = 'translated_texts'
      AND (
          (column_name = 'id' AND data_type = 'uuid' AND is_nullable = 'NO')
          OR (column_name = 'entity_type' AND data_type = 'character varying' AND character_maximum_length = 100 AND is_nullable = 'NO')
          OR (column_name = 'entity_id' AND data_type = 'uuid' AND is_nullable = 'NO')
          OR (column_name = 'field_name' AND data_type = 'character varying' AND character_maximum_length = 100 AND is_nullable = 'NO')
          OR (column_name = 'source_hash' AND data_type = 'character varying' AND character_maximum_length = 64 AND is_nullable = 'NO')
          OR (column_name = 'translated_text' AND data_type = 'text' AND is_nullable = 'NO')
          OR (column_name = 'target_language' AND data_type = 'character varying' AND character_maximum_length = 8 AND is_nullable = 'NO')
          OR (column_name = 'tenant_id' AND data_type = 'uuid' AND is_nullable = 'NO')
          OR (column_name = 'created_at' AND data_type = 'timestamp with time zone' AND is_nullable = 'NO')
          OR (column_name = 'updated_at' AND data_type = 'timestamp with time zone' AND is_nullable = 'YES')
      );

    IF expected_jobs <> 15 OR expected_texts <> 10 THEN
        RAISE EXCEPTION
            'Existing translation tables do not match the committed schema (jobs %, texts %).',
            expected_jobs, expected_texts;
    END IF;

    IF (
        SELECT COUNT(*)
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'translation_jobs'
    ) <> 15 OR (
        SELECT COUNT(*)
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'translated_texts'
    ) <> 10 THEN
        RAISE EXCEPTION 'Existing translation tables have unexpected columns.';
    END IF;
END $$;

LOCK TABLE tenants IN SHARE ROW EXCLUSIVE MODE;

CREATE TABLE IF NOT EXISTS translated_texts (
    id uuid NOT NULL,
    entity_type character varying(100) NOT NULL,
    entity_id uuid NOT NULL,
    field_name character varying(100) NOT NULL,
    source_hash character varying(64) NOT NULL,
    translated_text text NOT NULL,
    target_language character varying(8) NOT NULL,
    tenant_id uuid NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NULL,
    CONSTRAINT "PK_translated_texts" PRIMARY KEY (id),
    CONSTRAINT ck_translated_texts_target_language
        CHECK (target_language IN ('he', 'en', 'th'))
);

CREATE TABLE IF NOT EXISTS translation_jobs (
    id uuid NOT NULL,
    entity_type character varying(100) NOT NULL,
    entity_id uuid NOT NULL,
    field_name character varying(100) NOT NULL,
    source_text text NOT NULL,
    source_hash character varying(64) NOT NULL,
    target_language character varying(8) NOT NULL,
    status character varying(20) NOT NULL,
    attempt_count integer NOT NULL,
    claim_token uuid NULL,
    processed_at timestamp with time zone NULL,
    last_error character varying(2000) NULL,
    tenant_id uuid NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NULL,
    CONSTRAINT "PK_translation_jobs" PRIMARY KEY (id),
    CONSTRAINT ck_translation_jobs_attempt_count CHECK (attempt_count >= 0),
    CONSTRAINT ck_translation_jobs_status
        CHECK (status IN ('pending', 'processing', 'completed', 'failed')),
    CONSTRAINT ck_translation_jobs_target_language
        CHECK (target_language IN ('he', 'en', 'th'))
);

CREATE INDEX IF NOT EXISTS ix_translated_texts_tenant_field
    ON translated_texts (tenant_id, entity_type, entity_id, field_name, target_language);

CREATE UNIQUE INDEX IF NOT EXISTS ux_translated_texts_source_version
    ON translated_texts (tenant_id, entity_type, entity_id, field_name, source_hash, target_language);

CREATE INDEX IF NOT EXISTS ix_translation_jobs_status_created_at
    ON translation_jobs (status, created_at);

CREATE INDEX IF NOT EXISTS ix_translation_jobs_tenant_entity
    ON translation_jobs (tenant_id, entity_type, entity_id);

CREATE UNIQUE INDEX IF NOT EXISTS ux_translation_jobs_source_version
    ON translation_jobs (tenant_id, entity_type, entity_id, field_name, source_hash, target_language);

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ck_translated_texts_target_language'
          AND conrelid = 'translated_texts'::regclass
    ) OR NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ck_translation_jobs_attempt_count'
          AND conrelid = 'translation_jobs'::regclass
    ) OR NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ck_translation_jobs_status'
          AND conrelid = 'translation_jobs'::regclass
    ) OR NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ck_translation_jobs_target_language'
          AND conrelid = 'translation_jobs'::regclass
    ) THEN
        RAISE EXCEPTION 'A required translation check constraint is missing.';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_indexes
        WHERE schemaname = 'public'
          AND indexname = 'ux_translated_texts_source_version'
          AND indexdef LIKE '%UNIQUE%'
    ) OR NOT EXISTS (
        SELECT 1 FROM pg_indexes
        WHERE schemaname = 'public'
          AND indexname = 'ux_translation_jobs_source_version'
          AND indexdef LIKE '%UNIQUE%'
    ) THEN
        RAISE EXCEPTION 'A required translation unique index is missing.';
    END IF;
END $$;

COMMIT;

-- Read-only confirmation. These statements are outside the transaction.
SELECT 'translated_texts' AS table_name, COUNT(*) AS rows FROM translated_texts
UNION ALL
SELECT 'translation_jobs', COUNT(*) FROM translation_jobs;

SELECT indexname, indexdef
FROM pg_indexes
WHERE schemaname = 'public'
  AND tablename IN ('translated_texts', 'translation_jobs')
ORDER BY tablename, indexname;

-- Rollback, only while both tables are still empty:
-- BEGIN;
--   DROP TABLE translation_jobs;
--   DROP TABLE translated_texts;
-- COMMIT;
-- Do not drop either table after the application has inserted a row.
