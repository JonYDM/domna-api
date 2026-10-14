using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domna.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TransferenciaComprobantes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BancoClabe",
                table: "Config",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BancoNombre",
                table: "Config",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BancoTitular",
                table: "Config",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ComprobanteClave",
                table: "Abonos",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BancoClabe",
                table: "Config");

            migrationBuilder.DropColumn(
                name: "BancoNombre",
                table: "Config");

            migrationBuilder.DropColumn(
                name: "BancoTitular",
                table: "Config");

            migrationBuilder.DropColumn(
                name: "ComprobanteClave",
                table: "Abonos");
        }
    }
}
