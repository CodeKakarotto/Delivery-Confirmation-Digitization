using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Confirmations.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddConfirmationTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditLog",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    Action = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    EntityId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ChangedFields = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLog", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditLog_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Orders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Zone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DeliverTo = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    TotalProducts = table.Column<int>(type: "int", nullable: false),
                    DeliveryDate = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScanDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    StoredFileName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    PageCount = table.Column<int>(type: "int", nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    UploadedByUserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(450)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScanDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScanDocuments_AspNetUsers_DeletedByUserId",
                        column: x => x.DeletedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScanDocuments_AspNetUsers_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DeliveryConfirmations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScanDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<int>(type: "int", nullable: true),
                    Zone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    OrderNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    DeliverTo = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    TotalProducts = table.Column<int>(type: "int", nullable: true),
                    DeliveryInstructions = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SignaturePresent = table.Column<bool>(type: "bit", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ReviewedByUserId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ManagerNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ResolvedByUserId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    VoidReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    VoidedByUserId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    VoidedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryConfirmations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeliveryConfirmations_AspNetUsers_ResolvedByUserId",
                        column: x => x.ResolvedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryConfirmations_AspNetUsers_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryConfirmations_AspNetUsers_VoidedByUserId",
                        column: x => x.VoidedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryConfirmations_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryConfirmations_ScanDocuments_ScanDocumentId",
                        column: x => x.ScanDocumentId,
                        principalTable: "ScanDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OcrResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScanDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Zone = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ZoneConfidence = table.Column<float>(type: "real", nullable: true),
                    OrderNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    OrderNumberConfidence = table.Column<float>(type: "real", nullable: true),
                    DeliverTo = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DeliverToConfidence = table.Column<float>(type: "real", nullable: true),
                    TotalProducts = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    TotalProductsConfidence = table.Column<float>(type: "real", nullable: true),
                    DeliveryInstructions = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    DeliveryInstructionsConfidence = table.Column<float>(type: "real", nullable: true),
                    SignatureMarkDetected = table.Column<bool>(type: "bit", nullable: true),
                    SignatureConfidence = table.Column<float>(type: "real", nullable: true),
                    RawText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OcrFailed = table.Column<bool>(type: "bit", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OcrResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OcrResults_ScanDocuments_ScanDocumentId",
                        column: x => x.ScanDocumentId,
                        principalTable: "ScanDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ValidationFlags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeliveryConfirmationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Field = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Message = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ResolvedByUserId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResolutionNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ValidationFlags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ValidationFlags_AspNetUsers_ResolvedByUserId",
                        column: x => x.ResolvedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ValidationFlags_DeliveryConfirmations_DeliveryConfirmationId",
                        column: x => x.DeliveryConfirmationId,
                        principalTable: "DeliveryConfirmations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_EntityType_EntityId",
                table: "AuditLog",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_UserId",
                table: "AuditLog",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryConfirmations_OrderId",
                table: "DeliveryConfirmations",
                column: "OrderId",
                unique: true,
                filter: "[Status] = 'Approved'");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryConfirmations_OrderNumber",
                table: "DeliveryConfirmations",
                column: "OrderNumber");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryConfirmations_ResolvedByUserId",
                table: "DeliveryConfirmations",
                column: "ResolvedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryConfirmations_ReviewedByUserId",
                table: "DeliveryConfirmations",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryConfirmations_ScanDocumentId",
                table: "DeliveryConfirmations",
                column: "ScanDocumentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryConfirmations_Status",
                table: "DeliveryConfirmations",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryConfirmations_VoidedByUserId",
                table: "DeliveryConfirmations",
                column: "VoidedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OcrResults_ScanDocumentId",
                table: "OcrResults",
                column: "ScanDocumentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_OrderNumber",
                table: "Orders",
                column: "OrderNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScanDocuments_DeletedByUserId",
                table: "ScanDocuments",
                column: "DeletedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ScanDocuments_Sha256",
                table: "ScanDocuments",
                column: "Sha256");

            migrationBuilder.CreateIndex(
                name: "IX_ScanDocuments_StoredFileName",
                table: "ScanDocuments",
                column: "StoredFileName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScanDocuments_UploadedAt",
                table: "ScanDocuments",
                column: "UploadedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ScanDocuments_UploadedByUserId",
                table: "ScanDocuments",
                column: "UploadedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ValidationFlags_DeliveryConfirmationId",
                table: "ValidationFlags",
                column: "DeliveryConfirmationId");

            migrationBuilder.CreateIndex(
                name: "IX_ValidationFlags_ResolvedByUserId",
                table: "ValidationFlags",
                column: "ResolvedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ValidationFlags_Type",
                table: "ValidationFlags",
                column: "Type");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLog");

            migrationBuilder.DropTable(
                name: "OcrResults");

            migrationBuilder.DropTable(
                name: "ValidationFlags");

            migrationBuilder.DropTable(
                name: "DeliveryConfirmations");

            migrationBuilder.DropTable(
                name: "Orders");

            migrationBuilder.DropTable(
                name: "ScanDocuments");
        }
    }
}
