using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApiInsightStudio.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTypeToWarningsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "Warnings",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Type",
                table: "Warnings");
        }
    }
}
