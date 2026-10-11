using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domna.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Categorias",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Nombre = table.Column<string>(type: "text", nullable: false),
                    _tallas = table.Column<List<string>>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categorias", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Clientas",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Nombre = table.Column<string>(type: "text", nullable: false),
                    Apellidos = table.Column<string>(type: "text", nullable: false),
                    Telefono = table.Column<string>(type: "text", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: false),
                    Origen = table.Column<string>(type: "text", nullable: false),
                    Creada = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UltimoAcceso = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PenalizacionPendiente = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Rol = table.Column<string>(type: "text", nullable: false),
                    SucursalCajera = table.Column<string>(type: "text", nullable: true),
                    PinHash = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clientas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ColoresCatalogo",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Nombre = table.Column<string>(type: "text", nullable: false),
                    Hex = table.Column<string>(type: "text", nullable: false),
                    Familia = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ColoresCatalogo", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Productos",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Nombre = table.Column<string>(type: "text", nullable: false),
                    Descripcion = table.Column<string>(type: "text", nullable: false),
                    CategoriaId = table.Column<string>(type: "text", nullable: false),
                    Silueta = table.Column<string>(type: "text", nullable: false),
                    Precio = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Costo = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    PrecioAntes = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    CodigoBarras = table.Column<string>(type: "text", nullable: true),
                    Activo = table.Column<bool>(type: "boolean", nullable: false),
                    PermiteApartado = table.Column<bool>(type: "boolean", nullable: false),
                    Creado = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReabastecidoEl = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Productos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Recuperaciones",
                columns: table => new
                {
                    Token = table.Column<string>(type: "text", nullable: false),
                    ClientaId = table.Column<string>(type: "text", nullable: false),
                    Expira = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Usado = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Recuperaciones", x => x.Token);
                });

            migrationBuilder.CreateTable(
                name: "Colores",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ProductoId = table.Column<string>(type: "text", nullable: false),
                    Nombre = table.Column<string>(type: "text", nullable: false),
                    Hex = table.Column<string>(type: "text", nullable: false),
                    Familia = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Colores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Colores_Productos_ProductoId",
                        column: x => x.ProductoId,
                        principalTable: "Productos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Variantes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ProductoId = table.Column<string>(type: "text", nullable: false),
                    Talla = table.Column<string>(type: "text", nullable: false),
                    ColorId = table.Column<string>(type: "text", nullable: false),
                    Sku = table.Column<string>(type: "text", nullable: false),
                    CodigoBarras = table.Column<string>(type: "text", nullable: true),
                    StockTemixco = table.Column<int>(type: "integer", nullable: false),
                    StockAzteca = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Variantes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Variantes_Productos_ProductoId",
                        column: x => x.ProductoId,
                        principalTable: "Productos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Clientas_Email",
                table: "Clientas",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Clientas_Telefono",
                table: "Clientas",
                column: "Telefono",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Colores_ProductoId",
                table: "Colores",
                column: "ProductoId");

            migrationBuilder.CreateIndex(
                name: "IX_Variantes_CodigoBarras",
                table: "Variantes",
                column: "CodigoBarras");

            migrationBuilder.CreateIndex(
                name: "IX_Variantes_ProductoId",
                table: "Variantes",
                column: "ProductoId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Categorias");

            migrationBuilder.DropTable(
                name: "Clientas");

            migrationBuilder.DropTable(
                name: "Colores");

            migrationBuilder.DropTable(
                name: "ColoresCatalogo");

            migrationBuilder.DropTable(
                name: "Recuperaciones");

            migrationBuilder.DropTable(
                name: "Variantes");

            migrationBuilder.DropTable(
                name: "Productos");
        }
    }
}
