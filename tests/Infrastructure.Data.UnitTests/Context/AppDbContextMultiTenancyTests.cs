using Application.Common.Interfaces;
using Domain.Common;
using Domain.Entities;
using Domain.Enums;
using Domain.ValueObjects;
using Infrastructure.Data.Context;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Infrastructure.Data.UnitTests.Context;

public class AppDbContextMultiTenancyTests
{
    [Fact]
    public void Constructor_WhenCurrentUserServiceIsNull_ShouldThrowArgumentNullException()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AppDbContext>().Options;

        // Act
        var act = () => new AppDbContext(options, null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("currentUserService");
    }

    [Fact]
    public void AppDbContext_WhenInspected_ShouldImplementIUnitOfWork()
    {
        // Assert
        typeof(AppDbContext).Should().Implement<IUnitOfWork>();
    }

    [Fact]
    public void Model_WhenBuilt_ShouldConfigureQueryFilterForEveryTenantEntity()
    {
        // Arrange
        using var fixture = SqliteContextFixture.Create(Guid.NewGuid());
        var tenantEntityClrTypes = typeof(BaseEntity).Assembly
            .GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(ITenantEntity).IsAssignableFrom(t))
            .ToList();

        // Act
        var model = fixture.DbContext.Model;

        // Assert
        tenantEntityClrTypes.Should().NotBeEmpty();
        foreach (var clrType in tenantEntityClrTypes)
        {
            var entityType = model.FindEntityType(clrType);
            entityType.Should().NotBeNull($"a entidade {clrType.Name} deve estar registrada no AppDbContext");
            entityType!.GetDeclaredQueryFilters().Should().NotBeEmpty(
                $"a entidade {clrType.Name} deve possuir Global Query Filter configurado");
        }
    }

    [Fact]
    public async Task Query_WhenTenantAContextActive_ShouldReturnOnlyTenantADataAcrossAllTenantEntities()
    {
        // Arrange
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        using var sharedDb = new SharedSqliteDatabase();
        await SeedTenantAsync(sharedDb, tenantA, "Restaurante Alfa", "Alice");
        await SeedTenantAsync(sharedDb, tenantB, "Bistrô Beta", "Bruno");

        // Act
        using var readContextA = sharedDb.CreateContext(tenantA);
        var companies = await readContextA.Companies.ToListAsync();
        var demandSlots = await readContextA.DemandSlots.ToListAsync();
        var employees = await readContextA.Employees.ToListAsync();
        var rosters = await readContextA.Rosters.Include(r => r.Shifts).ToListAsync();
        var shifts = await readContextA.Shifts.ToListAsync();
        var swaps = await readContextA.ShiftSwapRequests.ToListAsync();

        // Assert
        companies.Should().ContainSingle(c => c.TenantId == tenantA && c.Name == "Restaurante Alfa");
        companies.Should().OnlyContain(c => c.TenantId == tenantA);
        companies[0].ClosedDays.Should().ContainSingle().Which.Should().Be(DayOfWeek.Monday);

        demandSlots.Should().ContainSingle(d => d.TenantId == tenantA);
        employees.Should().HaveCount(2).And.OnlyContain(e => e.TenantId == tenantA);
        var mainEmployee = employees.Single(e => e.Name == "Alice");
        mainEmployee.SecondarySkills.Should().ContainSingle(s => s.Role == StationRole.Bar && s.Proficiency == SkillProficiency.Autonomous);
        mainEmployee.PreferredDaysOff.Should().ContainSingle().Which.Should().Be(DayOfWeek.Tuesday);
        mainEmployee.UnavailableDays.Should().ContainSingle().Which.Should().Be(DayOfWeek.Wednesday);

        rosters.Should().ContainSingle(r => r.TenantId == tenantA);
        rosters[0].SundayPolicy.Should().Be(SundayRotationPolicy.BiweeklyCycle);
        rosters[0].Shifts.Should().HaveCount(2).And.OnlyContain(s => s.TenantId == tenantA);
        shifts.Should().HaveCount(2).And.OnlyContain(s => s.TenantId == tenantA);
        swaps.Should().ContainSingle(s => s.TenantId == tenantA);
    }

    [Fact]
    public async Task Query_WhenSwitchingTenantOnSameDatabase_ShouldEvaluateCurrentTenantDynamicallyWithoutModelCacheLeak()
    {
        // Arrange
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        using var sharedDb = new SharedSqliteDatabase();
        await SeedTenantAsync(sharedDb, tenantA, "Casa A", "Ana");
        await SeedTenantAsync(sharedDb, tenantB, "Casa B", "Beto");

        // Act: consulta primeiro como Tenant A (compila e aquece o cache de modelo)
        using var contextA = sharedDb.CreateContext(tenantA);
        var resultA = await contextA.Companies.ToListAsync();

        // Act: consulta em nova instância de contexto como Tenant B
        using var contextB = sharedDb.CreateContext(tenantB);
        var resultB = await contextB.Companies.ToListAsync();

        // Assert
        resultA.Should().ContainSingle().Which.TenantId.Should().Be(tenantA);
        resultB.Should().ContainSingle().Which.TenantId.Should().Be(tenantB);
    }

    [Fact]
    public async Task Query_WhenCurrentTenantIdIsEmpty_ShouldReturnNoRowsFailClosed()
    {
        // Arrange
        var tenantA = Guid.NewGuid();
        using var sharedDb = new SharedSqliteDatabase();
        await SeedTenantAsync(sharedDb, tenantA, "Casa Segura", "Carlos");

        // Act
        using var unauthenticatedContext = sharedDb.CreateContext(Guid.Empty);
        var companies = await unauthenticatedContext.Companies.ToListAsync();
        var employees = await unauthenticatedContext.Employees.ToListAsync();
        var rosters = await unauthenticatedContext.Rosters.ToListAsync();

        // Assert
        companies.Should().BeEmpty();
        employees.Should().BeEmpty();
        rosters.Should().BeEmpty();
    }

    [Fact]
    public async Task Query_WhenIgnoreQueryFiltersIsExplicitlyUsed_ShouldReturnAllTenantsData()
    {
        // Arrange
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        using var sharedDb = new SharedSqliteDatabase();
        await SeedTenantAsync(sharedDb, tenantA, "Casa A", "Ana");
        await SeedTenantAsync(sharedDb, tenantB, "Casa B", "Beto");

        // Act
        using var adminContext = sharedDb.CreateContext(Guid.Empty);
        var allCompanies = await adminContext.Companies.IgnoreQueryFilters().ToListAsync();

        // Assert
        allCompanies.Should().HaveCount(2);
        allCompanies.Select(c => c.TenantId).Should().BeEquivalentTo([tenantA, tenantB]);
    }

    [Fact]
    public async Task SaveChangesAsync_WhenAddedEntityHasEmptyTenantId_ShouldInjectCurrentTenantId()
    {
        // Arrange
        var tenantA = Guid.NewGuid();
        using var sharedDb = new SharedSqliteDatabase();
        using var context = sharedDb.CreateContext(tenantA);

        var company = Company.Create(
            tenantA,
            "Casa Alfa",
            BusinessType.Restaurant,
            new TimeOnly(11, 0),
            new TimeOnly(23, 0),
            ScheduleModel.RotatingDaysOff).Value;
        context.Companies.Add(company);
        await context.SaveChangesAsync();

        var slot = DemandSlot.Create(tenantA, company.Id, DayOfWeek.Friday, 20, 120m).Value;
        slot.TenantId = Guid.Empty;

        context.DemandSlots.Add(slot);

        // Act
        await context.SaveChangesAsync();

        // Assert
        slot.TenantId.Should().Be(tenantA);
        using var verificationContext = sharedDb.CreateContext(tenantA);
        var persisted = await verificationContext.DemandSlots.SingleAsync(d => d.Id == slot.Id);
        persisted.TenantId.Should().Be(tenantA);
    }

    [Fact]
    public async Task SaveChangesAsync_WhenAddedEntityMatchesCurrentTenantId_ShouldPersistSuccessfully()
    {
        // Arrange
        var tenantA = Guid.NewGuid();
        using var sharedDb = new SharedSqliteDatabase();
        using var context = sharedDb.CreateContext(tenantA);

        var company = Company.Create(
            tenantA,
            "Cantina Central",
            BusinessType.Restaurant,
            new TimeOnly(11, 0),
            new TimeOnly(23, 0),
            ScheduleModel.RotatingDaysOff).Value;

        context.Companies.Add(company);

        // Act
        var affected = await context.SaveChangesAsync();

        // Assert
        affected.Should().Be(1);
        using var verificationContext = sharedDb.CreateContext(tenantA);
        (await verificationContext.Companies.SingleAsync()).Id.Should().Be(company.Id);
    }

    [Fact]
    public async Task SaveChangesAsync_WhenCurrentTenantIdIsEmptyAndSavingTenantEntity_ShouldThrowInvalidOperationException()
    {
        // Arrange
        using var sharedDb = new SharedSqliteDatabase();
        using var context = sharedDb.CreateContext(Guid.Empty);

        var slot = DemandSlot.Create(Guid.NewGuid(), Guid.NewGuid(), DayOfWeek.Saturday, 21, 150m).Value;
        context.DemandSlots.Add(slot);

        // Act
        var act = async () => await context.SaveChangesAsync();

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SaveChangesAsync_WhenAddedEntityHasDifferentTenantId_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        using var sharedDb = new SharedSqliteDatabase();
        using var context = sharedDb.CreateContext(tenantA);

        var slot = DemandSlot.Create(tenantB, Guid.NewGuid(), DayOfWeek.Saturday, 21, 150m).Value;
        context.DemandSlots.Add(slot);

        // Act
        var act = async () => await context.SaveChangesAsync();

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SaveChangesAsync_WhenModifyingEntityFromAnotherTenant_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        using var sharedDb = new SharedSqliteDatabase();
        await SeedTenantAsync(sharedDb, tenantB, "Casa B", "Beto");

        using var contextA = sharedDb.CreateContext(tenantA);
        var foreignCompany = await contextA.Companies.IgnoreQueryFilters().SingleAsync(c => c.TenantId == tenantB);
        foreignCompany.UpdateProfile("Nome Adulterado", BusinessType.Bar);

        // Act
        var act = async () => await contextA.SaveChangesAsync();

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SaveChangesAsync_WhenDeletingEntityFromAnotherTenant_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        using var sharedDb = new SharedSqliteDatabase();
        await SeedTenantAsync(sharedDb, tenantB, "Casa B", "Beto");

        using var contextA = sharedDb.CreateContext(tenantA);
        var foreignCompany = await contextA.Companies.IgnoreQueryFilters().SingleAsync(c => c.TenantId == tenantB);
        contextA.Companies.Remove(foreignCompany);

        // Act
        var act = async () => await contextA.SaveChangesAsync();

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SaveChangesAsync_WhenModifyingEntityOfCurrentTenantWithTamperedTenantId_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        using var sharedDb = new SharedSqliteDatabase();
        await SeedTenantAsync(sharedDb, tenantA, "Casa A", "Ana");

        using var contextA = sharedDb.CreateContext(tenantA);
        var company = await contextA.Companies.SingleAsync();
        company.UpdateProfile("Casa A Atualizada", BusinessType.Restaurant);
        company.TenantId = tenantB;

        // Act
        var act = async () => await contextA.SaveChangesAsync();

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public void SaveChanges_WhenCalledSynchronouslyWithEmptyTenantIdOnAddedEntity_ShouldInjectCurrentTenantId()
    {
        // Arrange
        var tenantA = Guid.NewGuid();
        using var sharedDb = new SharedSqliteDatabase();
        using var context = sharedDb.CreateContext(tenantA);

        var company = Company.Create(
            tenantA,
            "Casa Síncrona",
            BusinessType.Restaurant,
            new TimeOnly(11, 0),
            new TimeOnly(23, 0),
            ScheduleModel.RotatingDaysOff).Value;
        context.Companies.Add(company);
        context.SaveChanges();

        var slot = DemandSlot.Create(tenantA, company.Id, DayOfWeek.Thursday, 19, 80m).Value;
        slot.TenantId = Guid.Empty;
        context.DemandSlots.Add(slot);

        // Act
        var affected = context.SaveChanges();

        // Assert
        affected.Should().Be(1);
        slot.TenantId.Should().Be(tenantA);
    }

    [Fact]
    public async Task SaveChangesAsync_WhenNoTenantEntitiesArePendingAndTenantIdIsEmpty_ShouldSucceedWithoutThrowing()
    {
        // Arrange
        using var sharedDb = new SharedSqliteDatabase();
        using var context = sharedDb.CreateContext(Guid.Empty);

        // Act
        var affected = await context.SaveChangesAsync();

        // Assert
        affected.Should().Be(0);
    }

    private static async Task SeedTenantAsync(
        SharedSqliteDatabase sharedDb,
        Guid tenantId,
        string companyName,
        string employeeName)
    {
        using var context = sharedDb.CreateContext(tenantId);

        var company = Company.Create(
            tenantId,
            companyName,
            BusinessType.Restaurant,
            new TimeOnly(11, 0),
            new TimeOnly(23, 0),
            ScheduleModel.FixedClosure,
            [DayOfWeek.Monday]).Value;

        var demandSlot = DemandSlot.Create(
            tenantId,
            company.Id,
            DayOfWeek.Friday,
            20,
            100m).Value;

        var secondarySkill = SecondarySkill.Create(StationRole.Bar, SkillProficiency.Autonomous).Value;
        var employee = Employee.Create(
            tenantId,
            company.Id,
            employeeName,
            StationRole.Waiter,
            [secondarySkill],
            [DayOfWeek.Tuesday],
            [DayOfWeek.Wednesday]).Value;

        var secondEmployee = Employee.Create(
            tenantId,
            company.Id,
            $"{employeeName} Par",
            StationRole.Waiter).Value;

        var weekStart = new DateOnly(2026, 11, 9);
        var roster = Roster.Create(
            tenantId,
            company.Id,
            weekStart,
            SundayRotationPolicy.BiweeklyCycle).Value;

        var firstShift = roster.AssignShift(
            employee,
            weekStart.AddDays(4),
            new TimeOnly(17, 0),
            new TimeOnly(23, 30),
            StationRole.Waiter,
            DemandClassification.Peak).Value;

        var secondShift = roster.AssignShift(
            secondEmployee,
            weekStart.AddDays(5),
            new TimeOnly(17, 0),
            new TimeOnly(23, 30),
            StationRole.Waiter,
            DemandClassification.Peak).Value;

        var swapRequest = ShiftSwapRequest.Create(
            tenantId,
            company.Id,
            roster.Id,
            employee.Id,
            firstShift.Id,
            secondEmployee.Id,
            secondShift.Id,
            "Troca teste tenant").Value;

        context.Companies.Add(company);
        context.DemandSlots.Add(demandSlot);
        context.Employees.AddRange(employee, secondEmployee);
        context.Rosters.Add(roster);
        context.ShiftSwapRequests.Add(swapRequest);

        await context.SaveChangesAsync();
    }

    private sealed class SqliteContextFixture : IDisposable
    {
        private readonly SharedSqliteDatabase _database;
        public AppDbContext DbContext { get; }

        private SqliteContextFixture(SharedSqliteDatabase database, AppDbContext dbContext)
        {
            _database = database;
            DbContext = dbContext;
        }

        public static SqliteContextFixture Create(Guid tenantId)
        {
            var db = new SharedSqliteDatabase();
            return new SqliteContextFixture(db, db.CreateContext(tenantId));
        }

        public void Dispose()
        {
            DbContext.Dispose();
            _database.Dispose();
        }
    }

    private sealed class SharedSqliteDatabase : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<AppDbContext> _options;
        private bool _initialized;

        public SharedSqliteDatabase()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(_connection)
                .Options;
        }

        public AppDbContext CreateContext(Guid tenantId)
        {
            var currentUserService = Substitute.For<ICurrentUserService>();
            currentUserService.TenantId.Returns(tenantId);
            currentUserService.UserId.Returns(tenantId == Guid.Empty ? null : Guid.NewGuid());
            currentUserService.IsAuthenticated.Returns(tenantId != Guid.Empty);

            var context = new AppDbContext(_options, currentUserService);
            if (!_initialized)
            {
                context.Database.EnsureCreated();
                _initialized = true;
            }

            return context;
        }

        public void Dispose() => _connection.Dispose();
    }
}

