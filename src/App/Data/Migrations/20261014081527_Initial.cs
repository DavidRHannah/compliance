using System;
using System.Collections.Generic;
using System.Text.Json;
using App.Data.Entities;
using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;
using Pgvector;

#nullable disable

namespace App.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:document_status", "failed,pending,processing,ready")
                .Annotation("Npgsql:Enum:engagement_status", "active,closed,draft")
                .Annotation("Npgsql:Enum:final_verdict", "fail,insufficient_evidence,pass")
                .Annotation("Npgsql:Enum:mapping_relationship", "equivalent,related,subset,superset")
                .Annotation("Npgsql:Enum:mention_method", "alias,llm")
                .Annotation("Npgsql:Enum:preliminary_verdict", "insufficient_evidence,interpretation_required,preliminary_fail,preliminary_pass")
                .Annotation("Npgsql:Enum:review_flag_reason", "decision_superseded,document_deleted,document_replaced")
                .Annotation("Npgsql:Enum:run_status", "completed,failed,queued,running")
                .Annotation("Npgsql:Enum:scope_item_type", "asset,system,vendor")
                .Annotation("Npgsql:Enum:user_role", "admin,client,consultant")
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.CreateTable(
                name: "clients",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_clients", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "frameworks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_frameworks", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_id = table.Column<string>(type: "text", nullable: false),
                    email = table.Column<string>(type: "text", nullable: false),
                    display_name = table.Column<string>(type: "text", nullable: false),
                    role = table.Column<UserRole>(type: "user_role", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: true),
                    disabled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                    table.CheckConstraint("ck_users_client_role", "(role = 'client') = (client_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_users_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "controls",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    framework_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ref_code = table.Column<string>(type: "text", nullable: false),
                    theme = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    statement = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_controls", x => x.id);
                    table.UniqueConstraint("ak_controls_id_framework_id", x => new { x.id, x.framework_id });
                    table.CheckConstraint("ck_controls_theme", "theme IN ('organizational', 'people', 'physical', 'technological')");
                    table.ForeignKey(
                        name: "fk_controls_frameworks_framework_id",
                        column: x => x.framework_id,
                        principalTable: "frameworks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "engagements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lead_consultant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<EngagementStatus>(type: "engagement_status", nullable: false, defaultValue: EngagementStatus.Draft),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_engagements", x => x.id);
                    table.ForeignKey(
                        name: "fk_engagements_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_engagements_users_lead_consultant_id",
                        column: x => x.lead_consultant_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "assessment_criteria",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    control_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    expected_evidence = table.Column<List<string>>(type: "text[]", nullable: false),
                    pass_conditions = table.Column<string>(type: "text", nullable: false),
                    fail_conditions = table.Column<string>(type: "text", nullable: false),
                    requires_operation_evidence = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assessment_criteria", x => x.id);
                    table.UniqueConstraint("ak_assessment_criteria_control_id_id", x => new { x.control_id, x.id });
                    table.ForeignKey(
                        name: "fk_assessment_criteria_controls_control_id",
                        column: x => x.control_id,
                        principalTable: "controls",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_assessment_criteria_users_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "control_mappings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_control_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_control_id = table.Column<Guid>(type: "uuid", nullable: false),
                    relationship = table.Column<MappingRelationship>(type: "mapping_relationship", nullable: false),
                    provider = table.Column<string>(type: "text", nullable: true),
                    fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_control_mappings", x => x.id);
                    table.CheckConstraint("ck_control_mappings_not_self", "source_control_id <> target_control_id");
                    table.ForeignKey(
                        name: "fk_control_mappings_controls_source_control_id",
                        column: x => x.source_control_id,
                        principalTable: "controls",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_control_mappings_controls_target_control_id",
                        column: x => x.target_control_id,
                        principalTable: "controls",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "assessment_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    triggered_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    model_id = table.Column<string>(type: "text", nullable: false),
                    prompt_version = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<RunStatus>(type: "run_status", nullable: false, defaultValue: RunStatus.Queued),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assessment_runs", x => x.id);
                    table.UniqueConstraint("ak_assessment_runs_id_engagement_id", x => new { x.id, x.engagement_id });
                    table.ForeignKey(
                        name: "fk_assessment_runs_engagements_engagement_id",
                        column: x => x.engagement_id,
                        principalTable: "engagements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_assessment_runs_users_triggered_by_id",
                        column: x => x.triggered_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "audit_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "text", nullable: false),
                    entity_type = table.Column<string>(type: "text", nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    details = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_audit_events_engagements_engagement_id",
                        column: x => x.engagement_id,
                        principalTable: "engagements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_audit_events_users_actor_id",
                        column: x => x.actor_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    uploaded_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "text", nullable: false),
                    mime_type = table.Column<string>(type: "text", nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    page_count = table.Column<int>(type: "integer", nullable: true),
                    content_hash = table.Column<string>(type: "text", nullable: true),
                    s3_key = table.Column<string>(type: "text", nullable: false),
                    s3_version_id = table.Column<string>(type: "text", nullable: true),
                    extracted_s3_key = table.Column<string>(type: "text", nullable: true),
                    text_source = table.Column<string>(type: "text", nullable: true),
                    replaces_document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<DocumentStatus>(type: "document_status", nullable: false, defaultValue: DocumentStatus.Pending),
                    processing_step = table.Column<string>(type: "text", nullable: true),
                    error = table.Column<string>(type: "text", nullable: true),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_documents", x => x.id);
                    table.UniqueConstraint("ak_documents_id_engagement_id", x => new { x.id, x.engagement_id });
                    table.CheckConstraint("ck_documents_failed_has_error", "(status = 'failed') = (error IS NOT NULL)");
                    table.CheckConstraint("ck_documents_not_self_replacing", "replaces_document_id <> id");
                    table.CheckConstraint("ck_documents_text_source", "text_source IN ('embedded', 'text_recognition')");
                    table.ForeignKey(
                        name: "fk_documents_documents_replaces_document_id_engagement_id",
                        columns: x => new { x.replaces_document_id, x.engagement_id },
                        principalTable: "documents",
                        principalColumns: new[] { "id", "engagement_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_documents_engagements_engagement_id",
                        column: x => x.engagement_id,
                        principalTable: "engagements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_documents_users_uploaded_by_id",
                        column: x => x.uploaded_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "engagement_frameworks",
                columns: table => new
                {
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    framework_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_engagement_frameworks", x => new { x.engagement_id, x.framework_id });
                    table.ForeignKey(
                        name: "fk_engagement_frameworks_engagements_engagement_id",
                        column: x => x.engagement_id,
                        principalTable: "engagements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_engagement_frameworks_frameworks_framework_id",
                        column: x => x.framework_id,
                        principalTable: "frameworks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "scope_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<ScopeItemType>(type: "scope_item_type", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    aliases = table.Column<List<string>>(type: "text[]", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scope_items", x => x.id);
                    table.UniqueConstraint("ak_scope_items_id_engagement_id", x => new { x.id, x.engagement_id });
                    table.ForeignKey(
                        name: "fk_scope_items_engagements_engagement_id",
                        column: x => x.engagement_id,
                        principalTable: "engagements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_scope_items_users_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "assessment_run_controls",
                columns: table => new
                {
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    control_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assessment_criteria_id = table.Column<Guid>(type: "uuid", nullable: false),
                    failed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assessment_run_controls", x => new { x.run_id, x.control_id });
                    table.CheckConstraint("ck_assessment_run_controls_failed_has_error", "(failed_at IS NULL) = (error IS NULL)");
                    table.ForeignKey(
                        name: "fk_assessment_run_controls_assessment_criteria_control_id_asse",
                        columns: x => new { x.control_id, x.assessment_criteria_id },
                        principalTable: "assessment_criteria",
                        principalColumns: new[] { "control_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_assessment_run_controls_assessment_runs_run_id",
                        column: x => x.run_id,
                        principalTable: "assessment_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_assessment_run_controls_controls_control_id",
                        column: x => x.control_id,
                        principalTable: "controls",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "document_chunks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chunk_index = table.Column<int>(type: "integer", nullable: false),
                    locator = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    section_title = table.Column<string>(type: "text", nullable: true),
                    content = table.Column<string>(type: "text", nullable: false),
                    token_count = table.Column<int>(type: "integer", nullable: false),
                    embedding = table.Column<Vector>(type: "vector(1024)", nullable: true),
                    embedding_model = table.Column<string>(type: "text", nullable: true),
                    chunker = table.Column<string>(type: "text", nullable: false),
                    superseded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    search_vector = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: false)
                        .Annotation("Npgsql:TsVectorConfig", "english")
                        .Annotation("Npgsql:TsVectorProperties", new[] { "content" })
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_chunks", x => x.id);
                    table.UniqueConstraint("ak_document_chunks_id_engagement_id", x => new { x.id, x.engagement_id });
                    table.ForeignKey(
                        name: "fk_document_chunks_documents_document_id_engagement_id",
                        columns: x => new { x.document_id, x.engagement_id },
                        principalTable: "documents",
                        principalColumns: new[] { "id", "engagement_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_document_chunks_engagements_engagement_id",
                        column: x => x.engagement_id,
                        principalTable: "engagements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "engagement_controls",
                columns: table => new
                {
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    control_id = table.Column<Guid>(type: "uuid", nullable: false),
                    framework_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_applicable = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    justification = table.Column<string>(type: "text", nullable: true),
                    updated_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_engagement_controls", x => new { x.engagement_id, x.control_id });
                    table.CheckConstraint("ck_engagement_controls_justification", "is_applicable OR justification IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_engagement_controls_controls_control_id_framework_id",
                        columns: x => new { x.control_id, x.framework_id },
                        principalTable: "controls",
                        principalColumns: new[] { "id", "framework_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_engagement_controls_engagement_frameworks_engagement_id_fra",
                        columns: x => new { x.engagement_id, x.framework_id },
                        principalTable: "engagement_frameworks",
                        principalColumns: new[] { "engagement_id", "framework_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_engagement_controls_engagements_engagement_id",
                        column: x => x.engagement_id,
                        principalTable: "engagements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_engagement_controls_users_updated_by_id",
                        column: x => x.updated_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "assessments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    control_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    verdict = table.Column<PreliminaryVerdict>(type: "preliminary_verdict", nullable: false),
                    rationale = table.Column<string>(type: "text", nullable: false),
                    is_current = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assessments", x => x.id);
                    table.UniqueConstraint("ak_assessments_id_engagement_id", x => new { x.id, x.engagement_id });
                    table.ForeignKey(
                        name: "fk_assessments_assessment_run_controls_run_id_control_id",
                        columns: x => new { x.run_id, x.control_id },
                        principalTable: "assessment_run_controls",
                        principalColumns: new[] { "run_id", "control_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_assessments_assessment_runs_run_id_engagement_id",
                        columns: x => new { x.run_id, x.engagement_id },
                        principalTable: "assessment_runs",
                        principalColumns: new[] { "id", "engagement_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_assessments_controls_control_id",
                        column: x => x.control_id,
                        principalTable: "controls",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_assessments_engagements_engagement_id",
                        column: x => x.engagement_id,
                        principalTable: "engagements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_assessments_scope_items_scope_item_id_engagement_id",
                        columns: x => new { x.scope_item_id, x.engagement_id },
                        principalTable: "scope_items",
                        principalColumns: new[] { "id", "engagement_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "chunk_scope_mentions",
                columns: table => new
                {
                    chunk_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    matched_text = table.Column<string>(type: "text", nullable: false),
                    method = table.Column<MentionMethod>(type: "mention_method", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chunk_scope_mentions", x => new { x.chunk_id, x.scope_item_id });
                    table.ForeignKey(
                        name: "fk_chunk_scope_mentions_document_chunks_chunk_id_engagement_id",
                        columns: x => new { x.chunk_id, x.engagement_id },
                        principalTable: "document_chunks",
                        principalColumns: new[] { "id", "engagement_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_chunk_scope_mentions_engagements_engagement_id",
                        column: x => x.engagement_id,
                        principalTable: "engagements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_chunk_scope_mentions_scope_items_scope_item_id_engagement_id",
                        columns: x => new { x.scope_item_id, x.engagement_id },
                        principalTable: "scope_items",
                        principalColumns: new[] { "id", "engagement_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "consultant_decisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    assessment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    decided_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    final_verdict = table.Column<FinalVerdict>(type: "final_verdict", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_consultant_decisions", x => x.id);
                    table.ForeignKey(
                        name: "fk_consultant_decisions_assessments_assessment_id",
                        column: x => x.assessment_id,
                        principalTable: "assessments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_consultant_decisions_users_decided_by_id",
                        column: x => x.decided_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "evidence_citations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    assessment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chunk_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quoted_text = table.Column<string>(type: "text", nullable: false),
                    char_start = table.Column<int>(type: "integer", nullable: true),
                    char_end = table.Column<int>(type: "integer", nullable: true),
                    is_verified = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_evidence_citations", x => x.id);
                    table.CheckConstraint("ck_evidence_citations_verified_range", "(is_verified AND char_start IS NOT NULL AND char_end > char_start) OR (NOT is_verified AND char_start IS NULL AND char_end IS NULL)");
                    table.ForeignKey(
                        name: "fk_evidence_citations_assessments_assessment_id_engagement_id",
                        columns: x => new { x.assessment_id, x.engagement_id },
                        principalTable: "assessments",
                        principalColumns: new[] { "id", "engagement_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_evidence_citations_document_chunks_chunk_id_engagement_id",
                        columns: x => new { x.chunk_id, x.engagement_id },
                        principalTable: "document_chunks",
                        principalColumns: new[] { "id", "engagement_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_evidence_citations_engagements_engagement_id",
                        column: x => x.engagement_id,
                        principalTable: "engagements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "review_flags",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    assessment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    engagement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<ReviewFlagReason>(type: "review_flag_reason", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    superseded_assessment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    cleared_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cleared_by_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_review_flags", x => x.id);
                    table.CheckConstraint("ck_review_flags_cleared_by", "(cleared_at IS NULL) = (cleared_by_id IS NULL)");
                    table.CheckConstraint("ck_review_flags_document_reason", "(reason IN ('document_deleted', 'document_replaced')) = (document_id IS NOT NULL)");
                    table.CheckConstraint("ck_review_flags_superseded_reason", "(reason = 'decision_superseded') = (superseded_assessment_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_review_flags_assessments_assessment_id_engagement_id",
                        columns: x => new { x.assessment_id, x.engagement_id },
                        principalTable: "assessments",
                        principalColumns: new[] { "id", "engagement_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_review_flags_assessments_superseded_assessment_id_engagemen",
                        columns: x => new { x.superseded_assessment_id, x.engagement_id },
                        principalTable: "assessments",
                        principalColumns: new[] { "id", "engagement_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_review_flags_documents_document_id_engagement_id",
                        columns: x => new { x.document_id, x.engagement_id },
                        principalTable: "documents",
                        principalColumns: new[] { "id", "engagement_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_review_flags_engagements_engagement_id",
                        column: x => x.engagement_id,
                        principalTable: "engagements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_review_flags_users_cleared_by_id",
                        column: x => x.cleared_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_assessment_criteria_control_id_version",
                table: "assessment_criteria",
                columns: new[] { "control_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_assessment_criteria_created_by_id",
                table: "assessment_criteria",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_assessment_criteria_one_active",
                table: "assessment_criteria",
                column: "control_id",
                unique: true,
                filter: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_assessment_run_controls_control_id_assessment_criteria_id",
                table: "assessment_run_controls",
                columns: new[] { "control_id", "assessment_criteria_id" });

            migrationBuilder.CreateIndex(
                name: "ix_assessment_runs_engagement_id_created_at",
                table: "assessment_runs",
                columns: new[] { "engagement_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_assessment_runs_one_active",
                table: "assessment_runs",
                column: "engagement_id",
                unique: true,
                filter: "status IN ('queued', 'running')");

            migrationBuilder.CreateIndex(
                name: "ix_assessment_runs_triggered_by_id",
                table: "assessment_runs",
                column: "triggered_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_assessments_control_id",
                table: "assessments",
                column: "control_id");

            migrationBuilder.CreateIndex(
                name: "ix_assessments_one_current",
                table: "assessments",
                columns: new[] { "engagement_id", "control_id", "scope_item_id" },
                unique: true,
                filter: "is_current")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_assessments_run_id_control_id",
                table: "assessments",
                columns: new[] { "run_id", "control_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_assessments_run_id_engagement_id",
                table: "assessments",
                columns: new[] { "run_id", "engagement_id" });

            migrationBuilder.CreateIndex(
                name: "ix_assessments_scope_item_id_engagement_id",
                table: "assessments",
                columns: new[] { "scope_item_id", "engagement_id" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_actor_id",
                table: "audit_events",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_engagement_id_occurred_at",
                table: "audit_events",
                columns: new[] { "engagement_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_entity_type_entity_id",
                table: "audit_events",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_chunk_scope_mentions_chunk_id_engagement_id",
                table: "chunk_scope_mentions",
                columns: new[] { "chunk_id", "engagement_id" });

            migrationBuilder.CreateIndex(
                name: "ix_chunk_scope_mentions_engagement_id",
                table: "chunk_scope_mentions",
                column: "engagement_id");

            migrationBuilder.CreateIndex(
                name: "ix_chunk_scope_mentions_scope_item_id_engagement_id",
                table: "chunk_scope_mentions",
                columns: new[] { "scope_item_id", "engagement_id" });

            migrationBuilder.CreateIndex(
                name: "ix_consultant_decisions_assessment_id_decided_at_id",
                table: "consultant_decisions",
                columns: new[] { "assessment_id", "decided_at", "id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_consultant_decisions_decided_by_id",
                table: "consultant_decisions",
                column: "decided_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_control_mappings_source_control_id_target_control_id",
                table: "control_mappings",
                columns: new[] { "source_control_id", "target_control_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_control_mappings_target_control_id",
                table: "control_mappings",
                column: "target_control_id");

            migrationBuilder.CreateIndex(
                name: "ix_controls_framework_id_ref_code",
                table: "controls",
                columns: new[] { "framework_id", "ref_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_document_chunks_document_id_engagement_id",
                table: "document_chunks",
                columns: new[] { "document_id", "engagement_id" });

            migrationBuilder.CreateIndex(
                name: "ix_document_chunks_embedding",
                table: "document_chunks",
                column: "embedding",
                filter: "deleted_at IS NULL AND superseded_at IS NULL")
                .Annotation("Npgsql:IndexMethod", "hnsw")
                .Annotation("Npgsql:IndexOperators", new[] { "vector_cosine_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_document_chunks_engagement_id",
                table: "document_chunks",
                column: "engagement_id");

            migrationBuilder.CreateIndex(
                name: "ix_document_chunks_one_live_position",
                table: "document_chunks",
                columns: new[] { "document_id", "chunk_index" },
                unique: true,
                filter: "superseded_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_document_chunks_search_vector",
                table: "document_chunks",
                column: "search_vector")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_documents_engagement_id_status",
                table: "documents",
                columns: new[] { "engagement_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_documents_one_live_copy",
                table: "documents",
                columns: new[] { "engagement_id", "content_hash" },
                unique: true,
                filter: "deleted_at IS NULL AND status <> 'failed'");

            migrationBuilder.CreateIndex(
                name: "ix_documents_one_live_replacement",
                table: "documents",
                column: "replaces_document_id",
                unique: true,
                filter: "deleted_at IS NULL AND status <> 'failed'");

            migrationBuilder.CreateIndex(
                name: "ix_documents_replaces_document_id_engagement_id",
                table: "documents",
                columns: new[] { "replaces_document_id", "engagement_id" });

            migrationBuilder.CreateIndex(
                name: "ix_documents_uploaded_by_id",
                table: "documents",
                column: "uploaded_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_engagement_controls_control_id_framework_id",
                table: "engagement_controls",
                columns: new[] { "control_id", "framework_id" });

            migrationBuilder.CreateIndex(
                name: "ix_engagement_controls_engagement_id_framework_id",
                table: "engagement_controls",
                columns: new[] { "engagement_id", "framework_id" });

            migrationBuilder.CreateIndex(
                name: "ix_engagement_controls_updated_by_id",
                table: "engagement_controls",
                column: "updated_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_engagement_frameworks_framework_id",
                table: "engagement_frameworks",
                column: "framework_id");

            migrationBuilder.CreateIndex(
                name: "ix_engagements_client_id",
                table: "engagements",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_engagements_lead_consultant_id",
                table: "engagements",
                column: "lead_consultant_id");

            migrationBuilder.CreateIndex(
                name: "ix_evidence_citations_assessment_id_engagement_id",
                table: "evidence_citations",
                columns: new[] { "assessment_id", "engagement_id" });

            migrationBuilder.CreateIndex(
                name: "ix_evidence_citations_chunk_id_engagement_id",
                table: "evidence_citations",
                columns: new[] { "chunk_id", "engagement_id" });

            migrationBuilder.CreateIndex(
                name: "ix_evidence_citations_engagement_id",
                table: "evidence_citations",
                column: "engagement_id");

            migrationBuilder.CreateIndex(
                name: "ix_frameworks_key_version",
                table: "frameworks",
                columns: new[] { "key", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_review_flags_assessment_id_engagement_id",
                table: "review_flags",
                columns: new[] { "assessment_id", "engagement_id" });

            migrationBuilder.CreateIndex(
                name: "ix_review_flags_cleared_by_id",
                table: "review_flags",
                column: "cleared_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_review_flags_document_id_engagement_id",
                table: "review_flags",
                columns: new[] { "document_id", "engagement_id" });

            migrationBuilder.CreateIndex(
                name: "ix_review_flags_one_open",
                table: "review_flags",
                columns: new[] { "assessment_id", "reason", "document_id" },
                unique: true,
                filter: "cleared_at IS NULL")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_review_flags_open_by_assessment",
                table: "review_flags",
                column: "assessment_id",
                filter: "cleared_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_review_flags_open_by_engagement",
                table: "review_flags",
                column: "engagement_id",
                filter: "cleared_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_review_flags_superseded_assessment_id_engagement_id",
                table: "review_flags",
                columns: new[] { "superseded_assessment_id", "engagement_id" });

            migrationBuilder.CreateIndex(
                name: "ix_scope_items_created_by_id",
                table: "scope_items",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_scope_items_engagement_id_type_name",
                table: "scope_items",
                columns: new[] { "engagement_id", "type", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_client_id",
                table: "users",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_external_id",
                table: "users",
                column: "external_id",
                unique: true);

            // Written by hand: EF cannot model expression indexes or triggers.

            migrationBuilder.Sql("CREATE UNIQUE INDEX ix_users_email_lower ON users (lower(email));");

            // Deleting a document copies deleted_at to its existing chunks, so they leave search.
            migrationBuilder.Sql("""
                CREATE FUNCTION documents_copy_deleted_at_to_chunks() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    UPDATE document_chunks
                    SET deleted_at = NEW.deleted_at
                    WHERE document_id = NEW.id AND engagement_id = NEW.engagement_id;
                    RETURN NULL;
                END
                $$;

                CREATE TRIGGER documents_copy_deleted_at_to_chunks
                AFTER UPDATE OF deleted_at ON documents
                FOR EACH ROW WHEN (OLD.deleted_at IS DISTINCT FROM NEW.deleted_at)
                EXECUTE FUNCTION documents_copy_deleted_at_to_chunks();
                """);

            // A chunk inserted after its document was deleted starts deleted. FOR SHARE makes a
            // concurrent delete wait for this insert, or this insert see the committed delete.
            migrationBuilder.Sql("""
                CREATE FUNCTION document_chunks_copy_deleted_at_from_document() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    SELECT d.deleted_at INTO NEW.deleted_at
                    FROM documents d
                    WHERE d.id = NEW.document_id AND d.engagement_id = NEW.engagement_id
                    FOR SHARE;
                    RETURN NEW;
                END
                $$;

                CREATE TRIGGER document_chunks_copy_deleted_at_from_document
                BEFORE INSERT ON document_chunks
                FOR EACH ROW
                EXECUTE FUNCTION document_chunks_copy_deleted_at_from_document();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER document_chunks_copy_deleted_at_from_document ON document_chunks;
                DROP FUNCTION document_chunks_copy_deleted_at_from_document();
                DROP TRIGGER documents_copy_deleted_at_to_chunks ON documents;
                DROP FUNCTION documents_copy_deleted_at_to_chunks();
                DROP INDEX ix_users_email_lower;
                """);

            migrationBuilder.DropTable(
                name: "audit_events");

            migrationBuilder.DropTable(
                name: "chunk_scope_mentions");

            migrationBuilder.DropTable(
                name: "consultant_decisions");

            migrationBuilder.DropTable(
                name: "control_mappings");

            migrationBuilder.DropTable(
                name: "engagement_controls");

            migrationBuilder.DropTable(
                name: "evidence_citations");

            migrationBuilder.DropTable(
                name: "review_flags");

            migrationBuilder.DropTable(
                name: "engagement_frameworks");

            migrationBuilder.DropTable(
                name: "document_chunks");

            migrationBuilder.DropTable(
                name: "assessments");

            migrationBuilder.DropTable(
                name: "documents");

            migrationBuilder.DropTable(
                name: "assessment_run_controls");

            migrationBuilder.DropTable(
                name: "scope_items");

            migrationBuilder.DropTable(
                name: "assessment_criteria");

            migrationBuilder.DropTable(
                name: "assessment_runs");

            migrationBuilder.DropTable(
                name: "controls");

            migrationBuilder.DropTable(
                name: "engagements");

            migrationBuilder.DropTable(
                name: "frameworks");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "clients");
        }
    }
}
