using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NotificationModule.Shared.Migrations;

public partial class AddDataRetentionAndBillingLedger : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "PiiPurgedAt",
            table: "appointments",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_appointments_PiiPurgedAt_CreatedAt",
            table: "appointments",
            columns: new[] { "PiiPurgedAt", "CreatedAt" });

        migrationBuilder.CreateTable(
            name: "billing_delivery_events",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ReminderType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CorrelationId = table.Column<Guid>(type: "uuid", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_billing_delivery_events", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_billing_delivery_events_OccurredAt",
            table: "billing_delivery_events",
            column: "OccurredAt");

        migrationBuilder.CreateIndex(
            name: "IX_billing_delivery_events_OrganizationId_OccurredAt",
            table: "billing_delivery_events",
            columns: new[] { "OrganizationId", "OccurredAt" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "billing_delivery_events");

        migrationBuilder.DropIndex(
            name: "IX_appointments_PiiPurgedAt_CreatedAt",
            table: "appointments");

        migrationBuilder.DropColumn(
            name: "PiiPurgedAt",
            table: "appointments");
    }
}
