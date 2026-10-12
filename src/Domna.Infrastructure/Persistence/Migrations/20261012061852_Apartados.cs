using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domna.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Apartados : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Apartados",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Folio = table.Column<string>(type: "text", nullable: false),
                    ClientaId = table.Column<string>(type: "text", nullable: false),
                    ClientaNombre = table.Column<string>(type: "text", nullable: false),
                    ClientaTelefono = table.Column<string>(type: "text", nullable: false),
                    Subtotal = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Penalizacion = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Modalidad = table.Column<string>(type: "text", nullable: false),
                    Canal = table.Column<string>(type: "text", nullable: false),
                    Entrega = table.Column<string>(type: "text", nullable: false),
                    RequiereTraslado = table.Column<bool>(type: "boolean", nullable: false),
                    EstadoEntrega = table.Column<string>(type: "text", nullable: false),
                    Creado = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    VenceEl = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ListoEstimado = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Estado = table.Column<string>(type: "text", nullable: false),
                    CerradoEl = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Penalizado = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Apartados", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Config",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Nombre = table.Column<string>(type: "text", nullable: false),
                    Slug = table.Column<string>(type: "text", nullable: false),
                    AnticipoPct = table.Column<int>(type: "integer", nullable: false),
                    VigenciaSinAnticipoDias = table.Column<int>(type: "integer", nullable: false),
                    VigenciaConAnticipoDias = table.Column<int>(type: "integer", nullable: false),
                    Penalizacion = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    LimiteApartadosActivos = table.Column<int>(type: "integer", nullable: false),
                    DiasTraslado = table.Column<int>(type: "integer", nullable: false),
                    Suspendida = table.Column<bool>(type: "boolean", nullable: false),
                    DiasNovedad = table.Column<int>(type: "integer", nullable: false),
                    CatalogoPublico = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Config", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Abonos",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ApartadoId = table.Column<string>(type: "text", nullable: false),
                    Monto = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Fecha = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Metodo = table.Column<string>(type: "text", nullable: false),
                    Concepto = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Abonos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Abonos_Apartados_ApartadoId",
                        column: x => x.ApartadoId,
                        principalTable: "Apartados",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LineasApartado",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ApartadoId = table.Column<string>(type: "text", nullable: false),
                    VarianteId = table.Column<string>(type: "text", nullable: false),
                    ProductoId = table.Column<string>(type: "text", nullable: false),
                    Nombre = table.Column<string>(type: "text", nullable: false),
                    Silueta = table.Column<string>(type: "text", nullable: false),
                    Talla = table.Column<string>(type: "text", nullable: false),
                    ColorNombre = table.Column<string>(type: "text", nullable: false),
                    ColorHex = table.Column<string>(type: "text", nullable: false),
                    Precio = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Costo = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Cantidad = table.Column<int>(type: "integer", nullable: false),
                    SucursalOrigen = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LineasApartado", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LineasApartado_Apartados_ApartadoId",
                        column: x => x.ApartadoId,
                        principalTable: "Apartados",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Abonos_ApartadoId",
                table: "Abonos",
                column: "ApartadoId");

            migrationBuilder.CreateIndex(
                name: "IX_Apartados_Folio",
                table: "Apartados",
                column: "Folio",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LineasApartado_ApartadoId",
                table: "LineasApartado",
                column: "ApartadoId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Abonos");

            migrationBuilder.DropTable(
                name: "Config");

            migrationBuilder.DropTable(
                name: "LineasApartado");

            migrationBuilder.DropTable(
                name: "Apartados");
        }
    }
}
