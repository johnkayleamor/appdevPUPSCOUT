using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace appdevPUPSCOUT.Data.Migrations
{
    /// <inheritdoc />
    public partial class HashOtpCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Code",
                table: "OtpCodes");

            migrationBuilder.AddColumn<string>(
                name: "Salt",
                table: "OtpCodes",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Attempts",
                table: "OtpCodes",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "CodeHash",
                table: "OtpCodes",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RequestIp",
                table: "OtpCodes",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Attempts",
                table: "OtpCodes");

            migrationBuilder.DropColumn(
                name: "CodeHash",
                table: "OtpCodes");

            migrationBuilder.DropColumn(
                name: "RequestIp",
                table: "OtpCodes");

            migrationBuilder.DropColumn(
                name: "Salt",
                table: "OtpCodes");

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "OtpCodes",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }
    }
}
