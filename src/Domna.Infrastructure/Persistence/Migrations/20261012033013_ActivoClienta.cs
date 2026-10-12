using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Domna.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ActivoClienta : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Activo",
                table: "Clientas",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Activo",
                table: "Clientas");
        }
    }
}
