using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domna.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Movimientos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Movimientos",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Fecha = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProductoId = table.Column<string>(type: "text", nullable: false),
                    VarianteId = table.Column<string>(type: "text", nullable: false),
                    Sucursal = table.Column<string>(type: "text", nullable: false),
                    Delta = table.Column<int>(type: "integer", nullable: false),
                    Motivo = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Movimientos", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Movimientos_ProductoId_Fecha",
                table: "Movimientos",
                columns: new[] { "ProductoId", "Fecha" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Movimientos");
        }
    }
}
