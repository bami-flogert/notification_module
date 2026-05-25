using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NotificationModule.Shared.Migrations;

public partial class AddProviderMessageId : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ProviderMessageId",
            table: "notification_deliveries",
            type: "character varying(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ProviderMessageId",
            table: "billing_delivery_events",
            type: "character varying(128)",
            maxLength: 128,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ProviderMessageId",
            table: "notification_deliveries");

        migrationBuilder.DropColumn(
            name: "ProviderMessageId",
            table: "billing_delivery_events");
    }
}
