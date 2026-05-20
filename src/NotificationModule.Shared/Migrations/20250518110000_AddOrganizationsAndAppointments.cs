using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NotificationModule.Shared.Migrations;

/// <inheritdoc />
public partial class AddOrganizationsAndAppointments : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        var defaultOrganizationId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        migrationBuilder.CreateTable(
            name: "organizations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                TimeZone = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                OpenMrsBaseUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_organizations", x => x.Id);
            });

        migrationBuilder.InsertData(
            table: "organizations",
            columns: new[] { "Id", "Key", "Name", "TimeZone", "IsEnabled", "CreatedAt", "UpdatedAt" },
            values: new object[]
            {
                defaultOrganizationId,
                "default",
                "Default Organization",
                "UTC",
                true,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
            });

        migrationBuilder.AddColumn<Guid>(
            name: "OrganizationId",
            table: "provider_secrets",
            type: "uuid",
            nullable: false,
            defaultValue: defaultOrganizationId);

        migrationBuilder.DropPrimaryKey(
            name: "PK_provider_secrets",
            table: "provider_secrets");

        migrationBuilder.AddPrimaryKey(
            name: "PK_provider_secrets",
            table: "provider_secrets",
            columns: new[] { "OrganizationId", "Provider" });

        migrationBuilder.CreateTable(
            name: "appointments",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                AppointmentUuid = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                PatientUuid = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                PatientName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                PatientPhone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                PatientEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                StartDateTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Location = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                Instructions = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                SourceSystem = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                RawSourcePayload = table.Column<string>(type: "jsonb", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_appointments", x => x.Id);
                table.ForeignKey(
                    name: "FK_appointments_organizations_OrganizationId",
                    column: x => x.OrganizationId,
                    principalTable: "organizations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "scheduled_notifications",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                AppointmentId = table.Column<Guid>(type: "uuid", nullable: false),
                ReminderType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                ScheduledSendAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_scheduled_notifications", x => x.Id);
                table.ForeignKey(
                    name: "FK_scheduled_notifications_appointments_AppointmentId",
                    column: x => x.AppointmentId,
                    principalTable: "appointments",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_scheduled_notifications_organizations_OrganizationId",
                    column: x => x.OrganizationId,
                    principalTable: "organizations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_organizations_Key",
            table: "organizations",
            column: "Key",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_appointments_OrganizationId_AppointmentUuid",
            table: "appointments",
            columns: new[] { "OrganizationId", "AppointmentUuid" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_appointments_OrganizationId_StartDateTime",
            table: "appointments",
            columns: new[] { "OrganizationId", "StartDateTime" });

        migrationBuilder.CreateIndex(
            name: "IX_scheduled_notifications_AppointmentId",
            table: "scheduled_notifications",
            column: "AppointmentId");

        migrationBuilder.CreateIndex(
            name: "IX_scheduled_notifications_OrganizationId_Status_ScheduledSendAt",
            table: "scheduled_notifications",
            columns: new[] { "OrganizationId", "Status", "ScheduledSendAt" });

        migrationBuilder.AddForeignKey(
            name: "FK_provider_secrets_organizations_OrganizationId",
            table: "provider_secrets",
            column: "OrganizationId",
            principalTable: "organizations",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "scheduled_notifications");
        migrationBuilder.DropTable(name: "appointments");

        migrationBuilder.DropForeignKey(
            name: "FK_provider_secrets_organizations_OrganizationId",
            table: "provider_secrets");

        migrationBuilder.DropPrimaryKey(
            name: "PK_provider_secrets",
            table: "provider_secrets");

        migrationBuilder.DropColumn(
            name: "OrganizationId",
            table: "provider_secrets");

        migrationBuilder.AddPrimaryKey(
            name: "PK_provider_secrets",
            table: "provider_secrets",
            column: "Provider");

        migrationBuilder.DropTable(name: "organizations");
    }
}
