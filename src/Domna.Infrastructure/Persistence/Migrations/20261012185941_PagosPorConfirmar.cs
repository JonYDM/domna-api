using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domna.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PagosPorConfirmar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Estado",
                table: "Abonos",
                type: "text",
                nullable: false,
                defaultValue: "Confirmado"); // los abonos que ya existían fueron cobrados por la tienda

            migrationBuilder.AddColumn<string>(
                name: "MotivoRechazo",
                table: "Abonos",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RevisadoEl",
                table: "Abonos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RevisadoPor",
                table: "Abonos",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Estado",
                table: "Abonos");

            migrationBuilder.DropColumn(
                name: "MotivoRechazo",
                table: "Abonos");

            migrationBuilder.DropColumn(
                name: "RevisadoEl",
                table: "Abonos");

            migrationBuilder.DropColumn(
                name: "RevisadoPor",
                table: "Abonos");
        }
    }
}
