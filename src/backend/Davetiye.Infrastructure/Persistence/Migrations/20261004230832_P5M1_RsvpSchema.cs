using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Davetiye.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P5M1_RsvpSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "rsvp_configurations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    invitation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rsvp_configurations", x => x.id);
                    table.CheckConstraint("ck_rsvp_configurations_revision_nonnegative", "revision >= 0");
                    table.CheckConstraint("ck_rsvp_configurations_updated_after_created", "updated_at >= created_at");
                });

            migrationBuilder.CreateTable(
                name: "rsvp_submissions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    invitation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rsvp_submissions", x => x.id);
                    table.CheckConstraint("ck_rsvp_submissions_revision_nonnegative", "revision >= 0");
                    table.CheckConstraint("ck_rsvp_submissions_updated_after_submitted", "updated_at >= submitted_at");
                });

            migrationBuilder.CreateTable(
                name: "rsvp_questions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    configuration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    prompt = table.Column<string>(type: "text", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    is_required = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    semantic_role = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rsvp_questions", x => x.id);
                    table.CheckConstraint("ck_rsvp_questions_archive_after_creation", "archived_at IS NULL OR archived_at >= created_at");
                    table.CheckConstraint("ck_rsvp_questions_nonnegative_sort_order", "sort_order >= 0");
                    table.CheckConstraint("ck_rsvp_questions_participant_count_type", "semantic_role IS NULL OR (semantic_role = 1 AND type = 6)");
                    table.CheckConstraint("ck_rsvp_questions_prompt_not_blank", "length(btrim(prompt)) > 0");
                    table.CheckConstraint("ck_rsvp_questions_supported_type", "type IN (1, 2, 3, 4, 5, 6)");
                    table.ForeignKey(
                        name: "fk_rsvp_questions_rsvp_configurations_configuration_id",
                        column: x => x.configuration_id,
                        principalTable: "rsvp_configurations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rsvp_answers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    submission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_prompt_snapshot = table.Column<string>(type: "text", nullable: false),
                    question_type_snapshot = table.Column<int>(type: "integer", nullable: false),
                    is_required_snapshot = table.Column<bool>(type: "boolean", nullable: false),
                    semantic_role_snapshot = table.Column<int>(type: "integer", nullable: true),
                    text_value = table.Column<string>(type: "text", nullable: true),
                    number_value = table.Column<decimal>(type: "numeric", nullable: true),
                    boolean_value = table.Column<bool>(type: "boolean", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rsvp_answers", x => x.id);
                    table.CheckConstraint("ck_rsvp_answers_semantic_snapshot_type", "semantic_role_snapshot IS NULL OR (semantic_role_snapshot = 1 AND question_type_snapshot = 6)");
                    table.CheckConstraint("ck_rsvp_answers_supported_type", "question_type_snapshot IN (1, 2, 3, 4, 5, 6)");
                    table.CheckConstraint("ck_rsvp_answers_typed_value_pairing", "(question_type_snapshot IN (1, 2) AND text_value IS NOT NULL AND number_value IS NULL AND boolean_value IS NULL) OR (question_type_snapshot IN (3, 4) AND text_value IS NULL AND number_value IS NULL AND boolean_value IS NULL) OR (question_type_snapshot = 5 AND text_value IS NULL AND number_value IS NULL AND boolean_value IS NOT NULL) OR (question_type_snapshot = 6 AND text_value IS NULL AND number_value IS NOT NULL AND boolean_value IS NULL)");
                    table.ForeignKey(
                        name: "fk_rsvp_answers_rsvp_submissions_submission_id",
                        column: x => x.submission_id,
                        principalTable: "rsvp_submissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rsvp_manage_capabilities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    submission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    hmac_key_version = table.Column<int>(type: "integer", nullable: false),
                    hmac_digest = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rsvp_manage_capabilities", x => x.id);
                    table.CheckConstraint("ck_rsvp_manage_capabilities_digest_length", "octet_length(hmac_digest) = 32");
                    table.CheckConstraint("ck_rsvp_manage_capabilities_expiry_after_created", "expires_at > created_at");
                    table.CheckConstraint("ck_rsvp_manage_capabilities_key_version_positive", "hmac_key_version > 0");
                    table.CheckConstraint("ck_rsvp_manage_capabilities_purpose", "purpose = 'rsvp-manage'");
                    table.CheckConstraint("ck_rsvp_manage_capabilities_revoked_after_created", "revoked_at IS NULL OR revoked_at >= created_at");
                    table.ForeignKey(
                        name: "fk_rsvp_manage_capabilities_rsvp_submissions_submission_id",
                        column: x => x.submission_id,
                        principalTable: "rsvp_submissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rsvp_question_options",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_id = table.Column<Guid>(type: "uuid", nullable: false),
                    label = table.Column<string>(type: "text", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rsvp_question_options", x => x.id);
                    table.CheckConstraint("ck_rsvp_question_options_archive_after_creation", "archived_at IS NULL OR archived_at >= created_at");
                    table.CheckConstraint("ck_rsvp_question_options_label_not_blank", "length(btrim(label)) > 0");
                    table.CheckConstraint("ck_rsvp_question_options_nonnegative_sort_order", "sort_order >= 0");
                    table.ForeignKey(
                        name: "fk_rsvp_question_options_rsvp_questions_question_id",
                        column: x => x.question_id,
                        principalTable: "rsvp_questions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rsvp_answer_options",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    answer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    option_id = table.Column<Guid>(type: "uuid", nullable: false),
                    label_snapshot = table.Column<string>(type: "text", nullable: false),
                    sort_order_snapshot = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rsvp_answer_options", x => x.id);
                    table.CheckConstraint("ck_rsvp_answer_options_nonnegative_order_snapshot", "sort_order_snapshot >= 0");
                    table.ForeignKey(
                        name: "fk_rsvp_answer_options_rsvp_answers_answer_id",
                        column: x => x.answer_id,
                        principalTable: "rsvp_answers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_rsvp_answer_options_rsvp_question_options_option_id",
                        column: x => x.option_id,
                        principalTable: "rsvp_question_options",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_rsvp_answer_options_option_id",
                table: "rsvp_answer_options",
                column: "option_id");

            migrationBuilder.CreateIndex(
                name: "ux_rsvp_answer_options_answer_option",
                table: "rsvp_answer_options",
                columns: new[] { "answer_id", "option_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_rsvp_answers_submission_question",
                table: "rsvp_answers",
                columns: new[] { "submission_id", "question_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_rsvp_configurations_invitation",
                table: "rsvp_configurations",
                column: "invitation_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_rsvp_manage_capabilities_active_submission",
                table: "rsvp_manage_capabilities",
                column: "submission_id",
                unique: true,
                filter: "revoked_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_rsvp_manage_capabilities_hmac_digest",
                table: "rsvp_manage_capabilities",
                column: "hmac_digest",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_rsvp_question_options_active_question_order",
                table: "rsvp_question_options",
                columns: new[] { "question_id", "sort_order" },
                unique: true,
                filter: "archived_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_rsvp_questions_active_configuration_order",
                table: "rsvp_questions",
                columns: new[] { "configuration_id", "sort_order" },
                unique: true,
                filter: "archived_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_rsvp_questions_active_participant_count",
                table: "rsvp_questions",
                column: "configuration_id",
                unique: true,
                filter: "archived_at IS NULL AND semantic_role = 1");

            migrationBuilder.CreateIndex(
                name: "ix_rsvp_submissions_invitation_submitted_at",
                table: "rsvp_submissions",
                columns: new[] { "invitation_id", "submitted_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "rsvp_answer_options");

            migrationBuilder.DropTable(
                name: "rsvp_manage_capabilities");

            migrationBuilder.DropTable(
                name: "rsvp_answers");

            migrationBuilder.DropTable(
                name: "rsvp_question_options");

            migrationBuilder.DropTable(
                name: "rsvp_submissions");

            migrationBuilder.DropTable(
                name: "rsvp_questions");

            migrationBuilder.DropTable(
                name: "rsvp_configurations");
        }
    }
}
