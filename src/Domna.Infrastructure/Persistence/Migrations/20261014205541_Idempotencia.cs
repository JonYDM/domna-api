using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domna.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Idempotencia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Idempotencias",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    UsuarioId = table.Column<string>(type: "text", nullable: false),
                    Ruta = table.Column<string>(type: "text", nullable: false),
                    Llave = table.Column<string>(type: "text", nullable: false),
                    CodigoEstado = table.Column<int>(type: "integer", nullable: true),
                    Cuerpo = table.Column<string>(type: "character varying(16384)", maxLength: 16384, nullable: true),
                    CreadoEl = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Idempotencias", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Idempotencias_UsuarioId_Ruta_Llave",
                table: "Idempotencias",
                columns: new[] { "UsuarioId", "Ruta", "Llave" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Idempotencias");
        }
    }
}
