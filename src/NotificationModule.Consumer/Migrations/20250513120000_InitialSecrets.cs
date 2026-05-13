using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NotificationModule.Consumer.Migrations;

/// <inheritdoc />
public partial class InitialSecrets : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "provider_secrets",
            columns: table => new
            {
                Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                EncryptedPayload = table.Column<byte[]>(type: "bytea", nullable: false),
                Nonce = table.Column<byte[]>(type: "bytea", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_provider_secrets", x => x.Provider);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "provider_secrets");
    }
}
