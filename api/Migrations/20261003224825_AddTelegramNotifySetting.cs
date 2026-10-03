using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApiInsightStudio.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTelegramNotifySetting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "NotifyTelegram",
                table: "ProjectAutomationSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NotifyTelegram",
                table: "ProjectAutomationSettings");
        }
    }
}
