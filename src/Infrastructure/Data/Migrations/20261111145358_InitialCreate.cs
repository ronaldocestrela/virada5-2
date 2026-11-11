using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Data.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Companies",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                BusinessType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                OpeningTime = table.Column<TimeOnly>(type: "time", nullable: false),
                ClosingTime = table.Column<TimeOnly>(type: "time", nullable: false),
                ScheduleModel = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                ClosedDays = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Companies", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "DemandSlots",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                DayOfWeek = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                Hour = table.Column<int>(type: "int", nullable: false),
                ExpectedVolume = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                RelativeVolume = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: false),
                Classification = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DemandSlots", x => x.Id);
                table.ForeignKey(
                    name: "FK_DemandSlots_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Employees",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                PrimaryRole = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
                SecondarySkills = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                PreferredDaysOff = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                UnavailableDays = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Employees", x => x.Id);
                table.ForeignKey(
                    name: "FK_Employees_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Rosters",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WeekStartDate = table.Column<DateOnly>(type: "date", nullable: false),
                WeekEndDate = table.Column<DateOnly>(type: "date", nullable: false),
                Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                SundayPolicy = table.Column<int>(type: "int", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Rosters", x => x.Id);
                table.ForeignKey(
                    name: "FK_Rosters_Companies_CompanyId",
                    column: x => x.CompanyId,
                    principalTable: "Companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Shifts",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                RosterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Date = table.Column<DateOnly>(type: "date", nullable: false),
                StartTime = table.Column<TimeOnly>(type: "time", nullable: false),
                EndTime = table.Column<TimeOnly>(type: "time", nullable: false),
                AssignedRole = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                CoverageOrigin = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Shifts", x => x.Id);
                table.ForeignKey(
                    name: "FK_Shifts_Employees_EmployeeId",
                    column: x => x.EmployeeId,
                    principalTable: "Employees",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Shifts_Rosters_RosterId",
                    column: x => x.RosterId,
                    principalTable: "Rosters",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Companies_TenantId",
            table: "Companies",
            column: "TenantId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_DemandSlots_CompanyId",
            table: "DemandSlots",
            column: "CompanyId");

        migrationBuilder.CreateIndex(
            name: "IX_DemandSlots_TenantId_CompanyId_DayOfWeek_Hour",
            table: "DemandSlots",
            columns: new[] { "TenantId", "CompanyId", "DayOfWeek", "Hour" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Employees_CompanyId",
            table: "Employees",
            column: "CompanyId");

        migrationBuilder.CreateIndex(
            name: "IX_Employees_TenantId_CompanyId_IsActive",
            table: "Employees",
            columns: new[] { "TenantId", "CompanyId", "IsActive" });

        migrationBuilder.CreateIndex(
            name: "IX_Rosters_CompanyId",
            table: "Rosters",
            column: "CompanyId");

        migrationBuilder.CreateIndex(
            name: "IX_Rosters_TenantId_CompanyId_WeekStartDate",
            table: "Rosters",
            columns: new[] { "TenantId", "CompanyId", "WeekStartDate" });

        migrationBuilder.CreateIndex(
            name: "IX_Shifts_EmployeeId",
            table: "Shifts",
            column: "EmployeeId");

        migrationBuilder.CreateIndex(
            name: "IX_Shifts_RosterId",
            table: "Shifts",
            column: "RosterId");

        migrationBuilder.CreateIndex(
            name: "IX_Shifts_TenantId_EmployeeId_Date",
            table: "Shifts",
            columns: new[] { "TenantId", "EmployeeId", "Date" });

        migrationBuilder.CreateIndex(
            name: "IX_Shifts_TenantId_RosterId_EmployeeId",
            table: "Shifts",
            columns: new[] { "TenantId", "RosterId", "EmployeeId" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "DemandSlots");

        migrationBuilder.DropTable(
            name: "Shifts");

        migrationBuilder.DropTable(
            name: "Employees");

        migrationBuilder.DropTable(
            name: "Rosters");

        migrationBuilder.DropTable(
            name: "Companies");
    }
}
