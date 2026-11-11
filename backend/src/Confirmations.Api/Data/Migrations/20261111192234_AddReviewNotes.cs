using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Confirmations.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReviewNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExceptionNotes",
                table: "DeliveryConfirmations",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewerNote",
                table: "DeliveryConfirmations",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExceptionNotes",
                table: "DeliveryConfirmations");

            migrationBuilder.DropColumn(
                name: "ReviewerNote",
                table: "DeliveryConfirmations");
        }
    }
}
