using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NotificationModule.Shared.Migrations;

public partial class MakePatientPiiFieldsNullable : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "PatientName",
            table: "appointments",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "character varying(200)",
            oldMaxLength: 200);

        migrationBuilder.AlterColumn<string>(
            name: "PatientPhone",
            table: "appointments",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "character varying(64)",
            oldMaxLength: 64);

        migrationBuilder.AlterColumn<string>(
            name: "PatientEmail",
            table: "appointments",
            type: "character varying(320)",
            maxLength: 320,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "character varying(320)",
            oldMaxLength: 320);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "PatientName",
            table: "appointments",
            type: "character varying(200)",
            maxLength: 200,
            nullable: false,
            defaultValue: "",
            oldClrType: typeof(string),
            oldType: "character varying(200)",
            oldMaxLength: 200,
            oldNullable: true);

        migrationBuilder.AlterColumn<string>(
            name: "PatientPhone",
            table: "appointments",
            type: "character varying(64)",
            maxLength: 64,
            nullable: false,
            defaultValue: "",
            oldClrType: typeof(string),
            oldType: "character varying(64)",
            oldMaxLength: 64,
            oldNullable: true);

        migrationBuilder.AlterColumn<string>(
            name: "PatientEmail",
            table: "appointments",
            type: "character varying(320)",
            maxLength: 320,
            nullable: false,
            defaultValue: "",
            oldClrType: typeof(string),
            oldType: "character varying(320)",
            oldMaxLength: 320,
            oldNullable: true);
    }
}
