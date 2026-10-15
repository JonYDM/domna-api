using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domna.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Sesiones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Sesiones",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    UsuarioId = table.Column<string>(type: "text", nullable: false),
                    Creada = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UltimaActividad = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Expira = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Dispositivo = table.Column<string>(type: "text", nullable: false),
                    Revocada = table.Column<bool>(type: "boolean", nullable: false),
                    MotivoRevocacion = table.Column<string>(type: "text", nullable: true),
                    RevocadaEl = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sesiones", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sesiones_UsuarioId",
                table: "Sesiones",
                column: "UsuarioId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Sesiones");
        }
    }
}
