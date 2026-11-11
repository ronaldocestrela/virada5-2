using Domain.Entities;
using Domain.Enums;
using Domain.ValueObjects;
using Infrastructure.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Data.UnitTests.Repositories;

public class RosterRepositoryTests
{
    [Fact]
    public void Constructor_WhenDbContextIsNull_ShouldThrowArgumentNullException()
    {
        // Act
        var act = () => new RosterRepository(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("dbContext");
    }

    [Fact]
    public async Task NullGuards_WhenArgumentsAreNull_ShouldThrowArgumentNullException()
    {
        // Arrange
        using var fixture = new RepositorySqliteFixture();
        using var context = fixture.CreateContext(Guid.NewGuid());
        var repository = new RosterRepository(context);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>("roster", () => repository.AddAsync(null!));
        Assert.Throws<ArgumentNullException>("roster", () => repository.Update(null!));
    }

    [Fact]
    public async Task AddAsyncAndGetByIdWithShiftsAsync_WhenRosterExistsInCurrentTenant_ShouldReturnTrackedAggregateWithShifts()
    {
        // Arrange
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        using var fixture = new RepositorySqliteFixture();

        var (companyA, empA) = CreateCompanyAndEmployee(tenantA, "Casa A", "Alice");
        var (companyB, empB) = CreateCompanyAndEmployee(tenantB, "Casa B", "Bruno");

        var weekStart = new DateOnly(2026, 11, 9);
        var rosterA = Roster.Create(tenantA, companyA.Id, weekStart, SundayRotationPolicy.BiweeklyCycle).Value;
        rosterA.AssignShift(empA, weekStart, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter, DemandClassification.Moderate);

        var rosterB = Roster.Create(tenantB, companyB.Id, weekStart).Value;
        rosterB.AssignShift(empB, weekStart, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter, DemandClassification.Moderate);

        using (var seedA = fixture.CreateContext(tenantA))
        {
            seedA.Companies.Add(companyA);
            seedA.Employees.Add(empA);
            await new RosterRepository(seedA).AddAsync(rosterA);
            await seedA.SaveChangesAsync();
        }

        using (var seedB = fixture.CreateContext(tenantB))
        {
            seedB.Companies.Add(companyB);
            seedB.Employees.Add(empB);
            await new RosterRepository(seedB).AddAsync(rosterB);
            await seedB.SaveChangesAsync();
        }

        // Act
        using var readContextA = fixture.CreateContext(tenantA);
        var repoA = new RosterRepository(readContextA);

        var loadedA = await repoA.GetByIdWithShiftsAsync(rosterA.Id);
        var crossTenant = await repoA.GetByIdWithShiftsAsync(rosterB.Id);
        var emptyResult = await repoA.GetByIdWithShiftsAsync(Guid.Empty);

        // Assert
        loadedA.Should().NotBeNull();
        loadedA!.SundayPolicy.Should().Be(SundayRotationPolicy.BiweeklyCycle);
        loadedA.Shifts.Should().ContainSingle(s => s.EmployeeId == empA.Id);
        readContextA.Entry(loadedA).State.Should().Be(EntityState.Unchanged);
        crossTenant.Should().BeNull();
        emptyResult.Should().BeNull();
    }

    [Fact]
    public async Task GetByCompanyAndWeekStartAsync_WhenArchivedAndRecreatedRosterExist_ShouldReturnActiveVersionOnly()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        using var fixture = new RepositorySqliteFixture();
        var (company, employee) = CreateCompanyAndEmployee(tenantId, "Casa Recriação", "Carla");
        var weekStart = new DateOnly(2026, 11, 9);

        var archivedRoster = Roster.Create(tenantId, company.Id, weekStart).Value;
        archivedRoster.AssignShift(employee, weekStart, new TimeOnly(12, 0), new TimeOnly(20, 0), StationRole.Waiter, DemandClassification.Low);
        archivedRoster.Archive();

        var activeRoster = Roster.Create(tenantId, company.Id, weekStart).Value;
        activeRoster.AssignShift(employee, weekStart.AddDays(1), new TimeOnly(17, 0), new TimeOnly(23, 0), StationRole.Waiter, DemandClassification.Peak);

        using (var seedContext = fixture.CreateContext(tenantId))
        {
            var repo = new RosterRepository(seedContext);
            seedContext.Companies.Add(company);
            seedContext.Employees.Add(employee);
            await repo.AddAsync(archivedRoster);
            await repo.AddAsync(activeRoster);
            await seedContext.SaveChangesAsync();
        }

        // Act
        using var readContext = fixture.CreateContext(tenantId);
        var readRepo = new RosterRepository(readContext);

        var byWeekStart = await readRepo.GetByCompanyAndWeekStartAsync(company.Id, weekStart);
        var archivedById = await readRepo.GetByIdWithShiftsAsync(archivedRoster.Id);
        var invalidInput = await readRepo.GetByCompanyAndWeekStartAsync(Guid.Empty, weekStart);

        // Assert
        byWeekStart.Should().NotBeNull();
        byWeekStart!.Id.Should().Be(activeRoster.Id);
        byWeekStart.Status.Should().Be(RosterStatus.Draft);
        byWeekStart.Shifts.Should().ContainSingle(s => s.Date == weekStart.AddDays(1));
        readContext.Entry(byWeekStart).State.Should().Be(EntityState.Unchanged);

        archivedById.Should().NotBeNull();
        archivedById!.Status.Should().Be(RosterStatus.Archived);
        invalidInput.Should().BeNull();
    }

    [Fact]
    public async Task GetByWeekRangeAsync_ShouldReturnActiveRostersOverlappingRangeWithShiftsNoTrackingOrderedByWeekStart()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        using var fixture = new RepositorySqliteFixture();
        var (company, employee) = CreateCompanyAndEmployee(tenantId, "Casa Histórico", "Diego");
        var (otherCompany, otherEmp) = CreateCompanyAndEmployee(tenantId, "Outra Casa", "Eva");

        var week1Start = new DateOnly(2026, 11, 2); // 02..08
        var week2Start = new DateOnly(2026, 11, 9); // 09..15
        var week3Start = new DateOnly(2026, 11, 16); // 16..22
        var week4Start = new DateOnly(2026, 11, 23); // 23..29

        var roster1 = Roster.Create(tenantId, company.Id, week1Start).Value;
        roster1.AssignShift(employee, week1Start, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter, DemandClassification.Low);

        var roster2Archived = Roster.Create(tenantId, company.Id, week2Start).Value;
        roster2Archived.Archive();

        var roster2Active = Roster.Create(tenantId, company.Id, week2Start).Value;
        roster2Active.AssignShift(employee, week2Start, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter, DemandClassification.Moderate);

        var roster3 = Roster.Create(tenantId, company.Id, week3Start).Value;
        roster3.AssignShift(employee, week3Start, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter, DemandClassification.Peak);

        var roster4OutOfRange = Roster.Create(tenantId, company.Id, week4Start).Value;
        var otherCompanyRoster = Roster.Create(tenantId, otherCompany.Id, week2Start).Value;
        otherCompanyRoster.AssignShift(otherEmp, week2Start, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter, DemandClassification.Low);

        using (var seedContext = fixture.CreateContext(tenantId))
        {
            var repo = new RosterRepository(seedContext);
            seedContext.Companies.AddRange(company, otherCompany);
            seedContext.Employees.AddRange(employee, otherEmp);
            await repo.AddAsync(roster3);
            await repo.AddAsync(roster1);
            await repo.AddAsync(roster2Archived);
            await repo.AddAsync(roster2Active);
            await repo.AddAsync(roster4OutOfRange);
            await repo.AddAsync(otherCompanyRoster);
            await seedContext.SaveChangesAsync();
        }

        // Act
        using var readContext = fixture.CreateContext(tenantId);
        var readRepo = new RosterRepository(readContext);

        // Intervalo [2026-11-08, 2026-11-18] intercepta week1 (termina 08), week2 (09..15) e week3 (inicia 16)
        var inRange = await readRepo.GetByWeekRangeAsync(company.Id, new DateOnly(2026, 11, 8), new DateOnly(2026, 11, 18));
        var invertedRange = await readRepo.GetByWeekRangeAsync(company.Id, new DateOnly(2026, 11, 20), new DateOnly(2026, 11, 10));

        // Assert
        readContext.ChangeTracker.Entries<Roster>().Should().BeEmpty();
        readContext.ChangeTracker.Entries<Shift>().Should().BeEmpty();
        invertedRange.Should().BeEmpty();

        inRange.Select(r => r.Id).Should().Equal(roster1.Id, roster2Active.Id, roster3.Id);
        inRange.Should().OnlyContain(r => r.Shifts.Count == 1);
    }

    [Fact]
    public async Task Update_WhenTrackedRosterAssignsNewShiftAndSwaps_ShouldPersistWithoutConcurrencyException()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        using var fixture = new RepositorySqliteFixture();
        var (company, emp1) = CreateCompanyAndEmployee(tenantId, "Casa Turnos", "Ana");
        var emp2 = Employee.Create(tenantId, company.Id, "Beto", StationRole.Waiter).Value;
        var weekStart = new DateOnly(2026, 11, 9);

        var roster = Roster.Create(tenantId, company.Id, weekStart).Value;
        var firstShiftResult = roster.AssignShift(
            emp1,
            weekStart,
            new TimeOnly(11, 0),
            new TimeOnly(19, 0),
            StationRole.Waiter,
            DemandClassification.Moderate);
        firstShiftResult.IsSuccess.Should().BeTrue();

        using (var seedContext = fixture.CreateContext(tenantId))
        {
            seedContext.Companies.Add(company);
            seedContext.Employees.AddRange(emp1, emp2);
            await new RosterRepository(seedContext).AddAsync(roster);
            await seedContext.SaveChangesAsync();
        }

        // Act: Load tracked, assign new shift (client-generated Guid with ValueGeneratedNever), swap, and call Update()
        using (var updateContext = fixture.CreateContext(tenantId))
        {
            var repo = new RosterRepository(updateContext);
            var loaded = (await repo.GetByIdWithShiftsAsync(roster.Id))!;

            var secondShiftResult = loaded.AssignShift(
                emp2,
                weekStart.AddDays(1),
                new TimeOnly(12, 0),
                new TimeOnly(20, 0),
                StationRole.Waiter,
                DemandClassification.Moderate);
            secondShiftResult.IsSuccess.Should().BeTrue();

            var swapResult = loaded.SwapShiftEmployees(
                firstShiftResult.Value.Id,
                emp2,
                secondShiftResult.Value.Id,
                emp1);
            swapResult.IsSuccess.Should().BeTrue();

            repo.Update(loaded);
            await updateContext.SaveChangesAsync();
        }

        // Assert
        using var verifyContext = fixture.CreateContext(tenantId);
        var persisted = await new RosterRepository(verifyContext).GetByIdWithShiftsAsync(roster.Id);
        persisted.Should().NotBeNull();
        persisted!.Shifts.Should().HaveCount(2);
        persisted.Shifts.Single(s => s.Date == weekStart).EmployeeId.Should().Be(emp2.Id);
        persisted.Shifts.Single(s => s.Date == weekStart.AddDays(1)).EmployeeId.Should().Be(emp1.Id);
    }

    [Fact]
    public async Task Update_WhenRosterIsDetached_ShouldPersistStatusChange()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        using var fixture = new RepositorySqliteFixture();
        var (company, employee) = CreateCompanyAndEmployee(tenantId, "Casa Detached", "Leo");
        var weekStart = new DateOnly(2026, 11, 9);

        var roster = Roster.Create(tenantId, company.Id, weekStart).Value;
        roster.AssignShift(employee, weekStart, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter, DemandClassification.Moderate);

        using (var seedContext = fixture.CreateContext(tenantId))
        {
            seedContext.Companies.Add(company);
            seedContext.Employees.Add(employee);
            await new RosterRepository(seedContext).AddAsync(roster);
            await seedContext.SaveChangesAsync();
        }

        Roster detached;
        using (var noTrackContext = fixture.CreateContext(tenantId))
        {
            detached = await noTrackContext.Rosters.AsNoTracking().SingleAsync(r => r.Id == roster.Id);
        }

        detached.Archive().IsSuccess.Should().BeTrue();

        // Act
        using (var updateContext = fixture.CreateContext(tenantId))
        {
            var repo = new RosterRepository(updateContext);
            repo.Update(detached);
            await updateContext.SaveChangesAsync();
        }

        // Assert
        using var verifyContext = fixture.CreateContext(tenantId);
        var persisted = await new RosterRepository(verifyContext).GetByIdWithShiftsAsync(roster.Id);
        persisted.Should().NotBeNull();
        persisted!.Status.Should().Be(RosterStatus.Archived);
    }

    private static (Company Company, Employee Employee) CreateCompanyAndEmployee(
        Guid tenantId,
        string companyName,
        string employeeName)
    {
        var company = Company.Create(
            tenantId,
            companyName,
            BusinessType.Restaurant,
            new TimeOnly(11, 0),
            new TimeOnly(23, 0),
            ScheduleModel.RotatingDaysOff).Value;

        var employee = Employee.Create(tenantId, company.Id, employeeName, StationRole.Waiter).Value;
        return (company, employee);
    }
}

