using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Data.Migrations;

/// <inheritdoc />
public partial class AddShiftSwapRequests : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ShiftSwapRequests",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                RosterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                RequesterEmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                RequesterShiftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TargetEmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TargetShiftId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                Status = table.Column<string>(type: "nvarchar(35)", maxLength: 35, nullable: false),
                Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                DecisionNotes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                RequestedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                PeerRespondedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                ReviewedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                ReviewedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ShiftSwapRequests", x => x.Id);
                table.ForeignKey(
                    name: "FK_ShiftSwapRequests_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_ShiftSwapRequests_Employees_RequesterEmployeeId",
                    column: x => x.RequesterEmployeeId,
                    principalTable: "Employees",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_ShiftSwapRequests_Employees_TargetEmployeeId",
                    column: x => x.TargetEmployeeId,
                    principalTable: "Employees",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_ShiftSwapRequests_Rosters_RosterId",
                    column: x => x.RosterId,
                    principalTable: "Rosters",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_ShiftSwapRequests_Shifts_RequesterShiftId",
                    column: x => x.RequesterShiftId,
                    principalTable: "Shifts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_ShiftSwapRequests_Shifts_TargetShiftId",
                    column: x => x.TargetShiftId,
                    principalTable: "Shifts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ShiftSwapRequests_CompanyId",
            table: "ShiftSwapRequests",
            column: "CompanyId");

        migrationBuilder.CreateIndex(
            name: "IX_ShiftSwapRequests_RequesterEmployeeId",
            table: "ShiftSwapRequests",
            column: "RequesterEmployeeId");

        migrationBuilder.CreateIndex(
            name: "IX_ShiftSwapRequests_RequesterShiftId",
            table: "ShiftSwapRequests",
            column: "RequesterShiftId");

        migrationBuilder.CreateIndex(
            name: "IX_ShiftSwapRequests_RosterId",
            table: "ShiftSwapRequests",
            column: "RosterId");

        migrationBuilder.CreateIndex(
            name: "IX_ShiftSwapRequests_TargetEmployeeId",
            table: "ShiftSwapRequests",
            column: "TargetEmployeeId");

        migrationBuilder.CreateIndex(
            name: "IX_ShiftSwapRequests_TargetShiftId",
            table: "ShiftSwapRequests",
            column: "TargetShiftId");

        migrationBuilder.CreateIndex(
            name: "IX_ShiftSwapRequests_TenantId_CompanyId_Status",
            table: "ShiftSwapRequests",
            columns: new[] { "TenantId", "CompanyId", "Status" });

        migrationBuilder.CreateIndex(
            name: "IX_ShiftSwapRequests_TenantId_RequesterShiftId_Status",
            table: "ShiftSwapRequests",
            columns: new[] { "TenantId", "RequesterShiftId", "Status" });

        migrationBuilder.CreateIndex(
            name: "IX_ShiftSwapRequests_TenantId_RosterId_Status",
            table: "ShiftSwapRequests",
            columns: new[] { "TenantId", "RosterId", "Status" });

        migrationBuilder.CreateIndex(
            name: "IX_ShiftSwapRequests_TenantId_TargetShiftId_Status",
            table: "ShiftSwapRequests",
            columns: new[] { "TenantId", "TargetShiftId", "Status" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ShiftSwapRequests");
    }
}
