using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace appdevPUPSCOUT.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVerificationFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ClosureType",
                table: "Tickets",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "RejectReason",
                table: "Tickets",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VerificationStartedAt",
                table: "Tickets",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClosureType",
                table: "Tickets");

            migrationBuilder.DropColumn(
                name: "RejectReason",
                table: "Tickets");

            migrationBuilder.DropColumn(
                name: "VerificationStartedAt",
                table: "Tickets");
        }
    }
}
