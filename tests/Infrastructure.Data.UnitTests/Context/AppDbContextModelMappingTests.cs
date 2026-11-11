using Application.Common.Interfaces;
using Domain.Entities;
using Domain.Enums;
using Domain.ValueObjects;
using Infrastructure.Data.Context;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using NSubstitute;

namespace Infrastructure.Data.UnitTests.Context;

public class AppDbContextModelMappingTests
{
    [Fact]
    public void Model_WhenBuiltForSqlServer_ShouldMapExpectedTableNamesAndNonGeneratedKeys()
    {
        // Arrange
        using var context = CreateSqlServerModelContext();

        // Act
        var model = context.Model;

        // Assert
        AssertTableAndKey<Company>(model, "Companies");
        AssertTableAndKey<DemandSlot>(model, "DemandSlots");
        AssertTableAndKey<Employee>(model, "Employees");
        AssertTableAndKey<Roster>(model, "Rosters");
        AssertTableAndKey<Shift>(model, "Shifts");
    }

    [Fact]
    public void Model_WhenBuiltForSqlServer_ShouldConfigureCompanyConstraintsAndIndexes()
    {
        // Arrange
        using var context = CreateSqlServerModelContext();
        var entityType = context.Model.FindEntityType(typeof(Company))!;

        // Assert
        var nameProperty = entityType.FindProperty(nameof(Company.Name))!;
        nameProperty.IsNullable.Should().BeFalse();
        nameProperty.GetMaxLength().Should().Be(Company.MaxNameLength);

        AssertStringEnumProperty(entityType, nameof(Company.BusinessType), 50);
        AssertStringEnumProperty(entityType, nameof(Company.ScheduleModel), 50);

        var closedDaysProperty = entityType.FindProperty(nameof(Company.ClosedDays))!;
        closedDaysProperty.IsNullable.Should().BeFalse();
        closedDaysProperty.GetMaxLength().Should().Be(200);

        var tenantIndex = entityType.FindIndex(entityType.FindProperty(nameof(Company.TenantId))!);
        tenantIndex.Should().NotBeNull();
        tenantIndex!.IsUnique.Should().BeTrue();
        tenantIndex.GetDatabaseName().Should().Be("IX_Companies_TenantId");
    }

    [Fact]
    public void Model_WhenBuiltForSqlServer_ShouldConfigureDemandSlotPrecisionForeignKeyAndUniqueIndex()
    {
        // Arrange
        using var context = CreateSqlServerModelContext();
        var entityType = context.Model.FindEntityType(typeof(DemandSlot))!;

        // Assert
        AssertStringEnumProperty(entityType, nameof(DemandSlot.DayOfWeek), 20);

        var expectedVolumeProperty = entityType.FindProperty(nameof(DemandSlot.ExpectedVolume))!;
        expectedVolumeProperty.IsNullable.Should().BeFalse();
        expectedVolumeProperty.GetPrecision().Should().Be(18);
        expectedVolumeProperty.GetScale().Should().Be(2);

        var relativeVolumeProperty = entityType.FindProperty(nameof(DemandSlot.RelativeVolume))!;
        relativeVolumeProperty.IsNullable.Should().BeFalse();
        relativeVolumeProperty.GetPrecision().Should().Be(5);
        relativeVolumeProperty.GetScale().Should().Be(4);

        AssertStringEnumProperty(entityType, nameof(DemandSlot.Classification), 30);

        var companyFk = entityType.GetForeignKeys()
            .SingleOrDefault(fk => fk.PrincipalEntityType.ClrType == typeof(Company));
        companyFk.Should().NotBeNull();
        companyFk!.DeleteBehavior.Should().Be(DeleteBehavior.Restrict);

        var slotIndex = entityType.FindIndex([
            entityType.FindProperty(nameof(DemandSlot.TenantId))!,
            entityType.FindProperty(nameof(DemandSlot.CompanyId))!,
            entityType.FindProperty(nameof(DemandSlot.DayOfWeek))!,
            entityType.FindProperty(nameof(DemandSlot.Hour))!
        ]);
        slotIndex.Should().NotBeNull();
        slotIndex!.IsUnique.Should().BeTrue();
        slotIndex.GetDatabaseName().Should().Be("IX_DemandSlots_TenantId_CompanyId_DayOfWeek_Hour");
    }

    [Fact]
    public void Model_WhenBuiltForSqlServer_ShouldConfigureEmployeeConstraintsForeignKeyAndIndex()
    {
        // Arrange
        using var context = CreateSqlServerModelContext();
        var entityType = context.Model.FindEntityType(typeof(Employee))!;

        // Assert
        var nameProperty = entityType.FindProperty(nameof(Employee.Name))!;
        nameProperty.IsNullable.Should().BeFalse();
        nameProperty.GetMaxLength().Should().Be(Employee.MaxNameLength);

        AssertStringEnumProperty(entityType, nameof(Employee.PrimaryRole), 50);

        var secondarySkillsProperty = entityType.FindProperty(nameof(Employee.SecondarySkills))!;
        secondarySkillsProperty.IsNullable.Should().BeFalse();
        secondarySkillsProperty.GetMaxLength().Should().Be(2000);

        var preferredDaysOffProperty = entityType.FindProperty(nameof(Employee.PreferredDaysOff))!;
        preferredDaysOffProperty.IsNullable.Should().BeFalse();
        preferredDaysOffProperty.GetMaxLength().Should().Be(200);

        var unavailableDaysProperty = entityType.FindProperty(nameof(Employee.UnavailableDays))!;
        unavailableDaysProperty.IsNullable.Should().BeFalse();
        unavailableDaysProperty.GetMaxLength().Should().Be(200);

        var companyFk = entityType.GetForeignKeys()
            .SingleOrDefault(fk => fk.PrincipalEntityType.ClrType == typeof(Company));
        companyFk.Should().NotBeNull();
        companyFk!.DeleteBehavior.Should().Be(DeleteBehavior.Restrict);

        var employeeIndex = entityType.FindIndex([
            entityType.FindProperty(nameof(Employee.TenantId))!,
            entityType.FindProperty(nameof(Employee.CompanyId))!,
            entityType.FindProperty(nameof(Employee.IsActive))!
        ]);
        employeeIndex.Should().NotBeNull();
        employeeIndex!.IsUnique.Should().BeFalse();
        employeeIndex.GetDatabaseName().Should().Be("IX_Employees_TenantId_CompanyId_IsActive");
    }

    [Fact]
    public void Model_WhenBuiltForSqlServer_ShouldConfigureRosterAndShiftConstraintsForeignKeysAndIndexes()
    {
        // Arrange
        using var context = CreateSqlServerModelContext();
        var rosterType = context.Model.FindEntityType(typeof(Roster))!;
        var shiftType = context.Model.FindEntityType(typeof(Shift))!;

        // Assert - Roster
        AssertStringEnumProperty(rosterType, nameof(Roster.Status), 30);

        var sundayPolicyProperty = rosterType.FindProperty(nameof(Roster.SundayPolicy))!;
        sundayPolicyProperty.IsNullable.Should().BeFalse();
        sundayPolicyProperty.GetValueConverter().Should().NotBeNull();
        sundayPolicyProperty.GetValueConverter()!.ProviderClrType.Should().Be(typeof(int));

        var rosterCompanyFk = rosterType.GetForeignKeys()
            .SingleOrDefault(fk => fk.PrincipalEntityType.ClrType == typeof(Company));
        rosterCompanyFk.Should().NotBeNull();
        rosterCompanyFk!.DeleteBehavior.Should().Be(DeleteBehavior.Restrict);

        var rosterIndex = rosterType.FindIndex([
            rosterType.FindProperty(nameof(Roster.TenantId))!,
            rosterType.FindProperty(nameof(Roster.CompanyId))!,
            rosterType.FindProperty(nameof(Roster.WeekStartDate))!
        ]);
        rosterIndex.Should().NotBeNull();
        rosterIndex!.IsUnique.Should().BeFalse();
        rosterIndex.GetDatabaseName().Should().Be("IX_Rosters_TenantId_CompanyId_WeekStartDate");

        // Assert - Shift
        AssertStringEnumProperty(shiftType, nameof(Shift.AssignedRole), 50);
        AssertStringEnumProperty(shiftType, nameof(Shift.CoverageOrigin), 30);

        var shiftRosterFk = shiftType.GetForeignKeys()
            .SingleOrDefault(fk => fk.PrincipalEntityType.ClrType == typeof(Roster));
        shiftRosterFk.Should().NotBeNull();
        shiftRosterFk!.DeleteBehavior.Should().Be(DeleteBehavior.Cascade);

        var shiftEmployeeFk = shiftType.GetForeignKeys()
            .SingleOrDefault(fk => fk.PrincipalEntityType.ClrType == typeof(Employee));
        shiftEmployeeFk.Should().NotBeNull();
        shiftEmployeeFk!.DeleteBehavior.Should().Be(DeleteBehavior.Restrict);

        var shiftRosterIndex = shiftType.FindIndex([
            shiftType.FindProperty(nameof(Shift.TenantId))!,
            shiftType.FindProperty(nameof(Shift.RosterId))!,
            shiftType.FindProperty(nameof(Shift.EmployeeId))!
        ]);
        shiftRosterIndex.Should().NotBeNull();
        shiftRosterIndex!.GetDatabaseName().Should().Be("IX_Shifts_TenantId_RosterId_EmployeeId");

        var shiftEmployeeDateIndex = shiftType.FindIndex([
            shiftType.FindProperty(nameof(Shift.TenantId))!,
            shiftType.FindProperty(nameof(Shift.EmployeeId))!,
            shiftType.FindProperty(nameof(Shift.Date))!
        ]);
        shiftEmployeeDateIndex.Should().NotBeNull();
        shiftEmployeeDateIndex!.GetDatabaseName().Should().Be("IX_Shifts_TenantId_EmployeeId_Date");
    }

    [Fact]
    public async Task Persistence_WhenSavingFullAggregateGraphWithFourDecimalRelativeVolume_ShouldRoundTripWithoutTruncation()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        var currentUserService = Substitute.For<ICurrentUserService>();
        currentUserService.TenantId.Returns(tenantId);
        currentUserService.IsAuthenticated.Returns(true);

        using (var setupContext = new AppDbContext(options, currentUserService))
        {
            setupContext.Database.EnsureCreated();

            var company = Company.Create(
                tenantId,
                "Bistrô 5x2",
                BusinessType.Restaurant,
                new TimeOnly(11, 30),
                new TimeOnly(23, 30),
                ScheduleModel.FixedClosure,
                [DayOfWeek.Monday]).Value;

            var slot = DemandSlot.Create(tenantId, company.Id, DayOfWeek.Friday, 20, 123.45m).Value;
            slot.EvaluateRelativeDemand(1000m);

            var employee = Employee.Create(
                tenantId,
                company.Id,
                "Maria Clara",
                StationRole.Kitchen,
                [SecondarySkill.Create(StationRole.Expedition, SkillProficiency.Autonomous).Value],
                [DayOfWeek.Tuesday],
                [DayOfWeek.Wednesday]).Value;

            var roster = Roster.Create(
                tenantId,
                company.Id,
                new DateOnly(2026, 11, 9),
                SundayRotationPolicy.BiweeklyCycle).Value;

            roster.AssignShift(
                employee,
                new DateOnly(2026, 11, 13),
                new TimeOnly(16, 0),
                new TimeOnly(0, 0),
                StationRole.Kitchen,
                DemandClassification.Peak);

            setupContext.Companies.Add(company);
            setupContext.DemandSlots.Add(slot);
            setupContext.Employees.Add(employee);
            setupContext.Rosters.Add(roster);
            await setupContext.SaveChangesAsync();
        }

        // Act
        using var readContext = new AppDbContext(options, currentUserService);
        var loadedSlot = await readContext.DemandSlots.SingleAsync();
        var loadedRoster = await readContext.Rosters.Include(r => r.Shifts).SingleAsync();

        // Assert
        loadedSlot.ExpectedVolume.Should().Be(123.45m);
        loadedSlot.RelativeVolume.Should().Be(0.1235m);
        loadedSlot.Classification.Should().Be(DemandClassification.Idle);
        loadedRoster.Status.Should().Be(RosterStatus.Draft);
        loadedRoster.Shifts.Should().ContainSingle();
    }

    [Fact]
    public void DesignTimeFactory_WhenDbConnectionStringNotConfigured_ShouldCreateSqlServerAppDbContextOffline()
    {
        // Arrange
        var originalEnv = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING");
        Environment.SetEnvironmentVariable("DB_CONNECTION_STRING", null);

        try
        {
            var factory = new AppDbContextFactory();

            // Act
            using var context = factory.CreateDbContext([]);

            // Assert
            context.Should().NotBeNull();
            context.Database.ProviderName.Should().Be("Microsoft.EntityFrameworkCore.SqlServer");
            context.Model.FindEntityType(typeof(Company)).Should().NotBeNull();
        }
        finally
        {
            Environment.SetEnvironmentVariable("DB_CONNECTION_STRING", originalEnv);
        }
    }

    [Fact]
    public void DesignTimeFactory_WhenDbConnectionStringEnvVarConfigured_ShouldUseProvidedConnectionString()
    {
        // Arrange
        var originalEnv = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING");
        const string expectedConnectionString = "Server=localhost,1433;Database=Scale5x2TestDb;Trusted_Connection=True;";
        Environment.SetEnvironmentVariable("DB_CONNECTION_STRING", expectedConnectionString);

        try
        {
            var factory = new AppDbContextFactory();

            // Act
            using var context = factory.CreateDbContext([]);

            // Assert
            var configuredConnectionString = context.Database.GetConnectionString();
            configuredConnectionString.Should().NotBeNullOrWhiteSpace();
            configuredConnectionString.Should().Contain("Data Source=localhost,1433");
            configuredConnectionString.Should().Contain("Initial Catalog=Scale5x2TestDb");
        }
        finally
        {
            Environment.SetEnvironmentVariable("DB_CONNECTION_STRING", originalEnv);
        }
    }

    private static AppDbContext CreateSqlServerModelContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer()
            .Options;

        var currentUserService = Substitute.For<ICurrentUserService>();
        currentUserService.TenantId.Returns(Guid.Empty);

        return new AppDbContext(options, currentUserService);
    }

    private static void AssertTableAndKey<TEntity>(IModel model, string expectedTableName)
    {
        var entityType = model.FindEntityType(typeof(TEntity));
        entityType.Should().NotBeNull();
        entityType!.GetTableName().Should().Be(expectedTableName);

        var idProperty = entityType!.FindProperty("Id");
        idProperty.Should().NotBeNull();
        idProperty!.IsPrimaryKey().Should().BeTrue();
        idProperty!.ValueGenerated.Should().Be(ValueGenerated.Never);
    }

    private static void AssertStringEnumProperty(IEntityType entityType, string propertyName, int expectedMaxLength)
    {
        var property = entityType.FindProperty(propertyName);
        property.Should().NotBeNull();
        property!.IsNullable.Should().BeFalse();
        property!.GetMaxLength().Should().Be(expectedMaxLength);
        property!.GetProviderClrType().Should().Be(typeof(string));
        var converter = property!.GetRelationalTypeMapping().Converter;
        converter.Should().NotBeNull();
        converter!.ProviderClrType.Should().Be(typeof(string));
    }
}
