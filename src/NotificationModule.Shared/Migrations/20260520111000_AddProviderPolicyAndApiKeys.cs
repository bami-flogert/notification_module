using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NotificationModule.Shared.Migrations;

public partial class AddProviderPolicyAndApiKeys : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "PreferredProvider",
            table: "organizations",
            type: "character varying(64)",
            maxLength: 64,
            nullable: false,
            defaultValue: "SwiftSend");

        migrationBuilder.AddColumn<string>(
            name: "FallbackProviders",
            table: "organizations",
            type: "character varying(500)",
            maxLength: 500,
            nullable: true);

        migrationBuilder.CreateTable(
            name: "organization_api_keys",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                Salt = table.Column<byte[]>(type: "bytea", nullable: false),
                KeyHash = table.Column<byte[]>(type: "bytea", nullable: false),
                IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_organization_api_keys", x => x.Id);
                table.ForeignKey(
                    name: "FK_organization_api_keys_organizations_OrganizationId",
                    column: x => x.OrganizationId,
                    principalTable: "organizations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_organization_api_keys_OrganizationId",
            table: "organization_api_keys",
            column: "OrganizationId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "organization_api_keys");

        migrationBuilder.DropColumn(name: "FallbackProviders", table: "organizations");
        migrationBuilder.DropColumn(name: "PreferredProvider", table: "organizations");
    }
}

