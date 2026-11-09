using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MaintTrack.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTranslationInfrastructure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "translated_texts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    field_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    source_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    translated_text = table.Column<string>(type: "text", nullable: false),
                    target_language = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_translated_texts", x => x.id);
                    table.CheckConstraint("ck_translated_texts_target_language", "target_language IN ('he', 'en', 'th')");
                });

            migrationBuilder.CreateTable(
                name: "translation_jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    field_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    source_text = table.Column<string>(type: "text", nullable: false),
                    source_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    target_language = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    processed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_translation_jobs", x => x.id);
                    table.CheckConstraint("ck_translation_jobs_attempt_count", "attempt_count >= 0");
                    table.CheckConstraint("ck_translation_jobs_status", "status IN ('pending', 'processing', 'completed', 'failed')");
                    table.CheckConstraint("ck_translation_jobs_target_language", "target_language IN ('he', 'en', 'th')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_translated_texts_tenant_field",
                table: "translated_texts",
                columns: new[] { "tenant_id", "entity_type", "entity_id", "field_name", "target_language" });

            migrationBuilder.CreateIndex(
                name: "ux_translated_texts_source_version",
                table: "translated_texts",
                columns: new[] { "tenant_id", "entity_type", "entity_id", "field_name", "source_hash", "target_language" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_translation_jobs_status_created_at",
                table: "translation_jobs",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_translation_jobs_tenant_entity",
                table: "translation_jobs",
                columns: new[] { "tenant_id", "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ux_translation_jobs_source_version",
                table: "translation_jobs",
                columns: new[] { "tenant_id", "entity_type", "entity_id", "field_name", "source_hash", "target_language" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "translated_texts");

            migrationBuilder.DropTable(
                name: "translation_jobs");
        }
    }
}
