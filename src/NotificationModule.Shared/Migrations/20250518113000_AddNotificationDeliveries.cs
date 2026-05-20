using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NotificationModule.Shared.Migrations;

/// <inheritdoc />
public partial class AddNotificationDeliveries : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "notification_deliveries",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                AppointmentId = table.Column<Guid>(type: "uuid", nullable: false),
                ScheduledNotificationId = table.Column<Guid>(type: "uuid", nullable: false),
                Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                FailedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_notification_deliveries", x => x.Id);
                table.ForeignKey(
                    name: "FK_notification_deliveries_appointments_AppointmentId",
                    column: x => x.AppointmentId,
                    principalTable: "appointments",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_notification_deliveries_organizations_OrganizationId",
                    column: x => x.OrganizationId,
                    principalTable: "organizations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_notification_deliveries_scheduled_notifications_ScheduledNotificationId",
                    column: x => x.ScheduledNotificationId,
                    principalTable: "scheduled_notifications",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_notification_deliveries_AppointmentId",
            table: "notification_deliveries",
            column: "AppointmentId");

        migrationBuilder.CreateIndex(
            name: "IX_notification_deliveries_OrganizationId_Status_UpdatedAt",
            table: "notification_deliveries",
            columns: new[] { "OrganizationId", "Status", "UpdatedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_notification_deliveries_ScheduledNotificationId_Provider",
            table: "notification_deliveries",
            columns: new[] { "ScheduledNotificationId", "Provider" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "notification_deliveries");
    }
}
