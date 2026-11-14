using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tulaziva.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonalServicesCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Categories",
                columns: new[] { "Id", "Key" },
                values: new object[] { 11, "personalServices" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: 11);
        }
    }
}
