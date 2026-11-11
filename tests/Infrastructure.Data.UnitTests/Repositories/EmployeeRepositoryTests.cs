using Domain.Entities;
using Domain.Enums;
using Domain.ValueObjects;
using Infrastructure.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Data.UnitTests.Repositories;

public class EmployeeRepositoryTests
{
    [Fact]
    public void Constructor_WhenDbContextIsNull_ShouldThrowArgumentNullException()
    {
        // Act
        var act = () => new EmployeeRepository(null!);

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
        var repository = new EmployeeRepository(context);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>("ids", () => repository.GetByIdsAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>("employee", () => repository.AddAsync(null!));
        Assert.Throws<ArgumentNullException>("employee", () => repository.Update(null!));
    }

    [Fact]
    public async Task GetByIdAsync_WhenEmployeeExistsInCurrentTenant_ShouldReturnTrackedEntityWithCollections()
    {
        // Arrange
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        using var fixture = new RepositorySqliteFixture();

        var companyA = CreateCompany(tenantA, "Casa A");
        var companyB = CreateCompany(tenantB, "Casa B");
        var employeeA = Employee.Create(
            tenantA,
            companyA.Id,
            "Marina",
            StationRole.Waiter,
            [SecondarySkill.Create(StationRole.Bar, SkillProficiency.Autonomous).Value],
            [DayOfWeek.Monday],
            [DayOfWeek.Tuesday]).Value;
        var employeeB = Employee.Create(tenantB, companyB.Id, "Pedro", StationRole.Kitchen).Value;

        using (var seedA = fixture.CreateContext(tenantA))
        {
            seedA.Companies.Add(companyA);
            await new EmployeeRepository(seedA).AddAsync(employeeA);
            await seedA.SaveChangesAsync();
        }

        using (var seedB = fixture.CreateContext(tenantB))
        {
            seedB.Companies.Add(companyB);
            await new EmployeeRepository(seedB).AddAsync(employeeB);
            await seedB.SaveChangesAsync();
        }

        // Act
        using var readContextA = fixture.CreateContext(tenantA);
        var repoA = new EmployeeRepository(readContextA);

        var loaded = await repoA.GetByIdAsync(employeeA.Id);
        var crossTenant = await repoA.GetByIdAsync(employeeB.Id);
        var emptyResult = await repoA.GetByIdAsync(Guid.Empty);

        // Assert
        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be("Marina");
        loaded.SecondarySkills.Should().ContainSingle(s => s.Role == StationRole.Bar && s.Proficiency == SkillProficiency.Autonomous);
        loaded.PreferredDaysOff.Should().ContainSingle().Which.Should().Be(DayOfWeek.Monday);
        loaded.UnavailableDays.Should().ContainSingle().Which.Should().Be(DayOfWeek.Tuesday);
        readContextA.Entry(loaded).State.Should().Be(EntityState.Unchanged);

        crossTenant.Should().BeNull();
        emptyResult.Should().BeNull();
    }

    [Fact]
    public async Task GetByCompanyAndGetActiveByCompanyAsync_ShouldFilterActiveAndReturnNoTrackingOrderedByName()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        using var fixture = new RepositorySqliteFixture();
        var company = CreateCompany(tenantId, "Casa Equipe");
        var otherCompany = CreateCompany(tenantId, "Outra Equipe");

        var bruno = Employee.Create(tenantId, company.Id, "Bruno", StationRole.Bar).Value;
        var ana = Employee.Create(tenantId, company.Id, "Ana", StationRole.Waiter).Value;
        var carlosInactive = Employee.Create(tenantId, company.Id, "Carlos", StationRole.Cashier).Value;
        carlosInactive.Deactivate();
        var outsider = Employee.Create(tenantId, otherCompany.Id, "Daniel", StationRole.Kitchen).Value;

        using (var seedContext = fixture.CreateContext(tenantId))
        {
            var repo = new EmployeeRepository(seedContext);
            seedContext.Companies.AddRange(company, otherCompany);
            await repo.AddAsync(bruno);
            await repo.AddAsync(ana);
            await repo.AddAsync(carlosInactive);
            await repo.AddAsync(outsider);
            await seedContext.SaveChangesAsync();
        }

        // Act
        using var readContext = fixture.CreateContext(tenantId);
        var readRepo = new EmployeeRepository(readContext);

        var allCompany = await readRepo.GetByCompanyAsync(company.Id);
        var activeCompany = await readRepo.GetActiveByCompanyAsync(company.Id);
        var emptyAll = await readRepo.GetByCompanyAsync(Guid.Empty);
        var emptyActive = await readRepo.GetActiveByCompanyAsync(Guid.Empty);

        // Assert
        readContext.ChangeTracker.Entries<Employee>().Should().BeEmpty();
        emptyAll.Should().BeEmpty();
        emptyActive.Should().BeEmpty();

        allCompany.Select(e => e.Name).Should().Equal("Ana", "Bruno", "Carlos");
        activeCompany.Select(e => e.Name).Should().Equal("Ana", "Bruno");
    }

    [Fact]
    public async Task GetByIdsAsync_WhenIdsContainDuplicatesEmptyOrForeignIds_ShouldReturnCurrentTenantMatchesNoTracking()
    {
        // Arrange
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        using var fixture = new RepositorySqliteFixture();

        var companyA = CreateCompany(tenantA, "Casa A");
        var companyB = CreateCompany(tenantB, "Casa B");
        var emp1 = Employee.Create(tenantA, companyA.Id, "Zeca", StationRole.Waiter).Value;
        var emp2 = Employee.Create(tenantA, companyA.Id, "Alice", StationRole.Bar).Value;
        var empForeign = Employee.Create(tenantB, companyB.Id, "Estrangeiro", StationRole.Kitchen).Value;

        using (var seedA = fixture.CreateContext(tenantA))
        {
            seedA.Companies.Add(companyA);
            await new EmployeeRepository(seedA).AddAsync(emp1);
            await new EmployeeRepository(seedA).AddAsync(emp2);
            await seedA.SaveChangesAsync();
        }

        using (var seedB = fixture.CreateContext(tenantB))
        {
            seedB.Companies.Add(companyB);
            await new EmployeeRepository(seedB).AddAsync(empForeign);
            await seedB.SaveChangesAsync();
        }

        // Act
        using var readContextA = fixture.CreateContext(tenantA);
        var repoA = new EmployeeRepository(readContextA);

        var found = await repoA.GetByIdsAsync([emp1.Id, emp1.Id, Guid.Empty, emp2.Id, empForeign.Id]);
        var emptyQuery = await repoA.GetByIdsAsync([Guid.Empty]);

        // Assert
        readContextA.ChangeTracker.Entries<Employee>().Should().BeEmpty();
        emptyQuery.Should().BeEmpty();
        found.Select(e => e.Name).Should().Equal("Alice", "Zeca");
    }

    [Fact]
    public async Task Update_WhenEmployeeIsTrackedOrDetached_ShouldPersistStateAndSkills()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        using var fixture = new RepositorySqliteFixture();
        var company = CreateCompany(tenantId, "Casa Update");
        var employee = Employee.Create(tenantId, company.Id, "Lucas", StationRole.Waiter).Value;

        using (var seedContext = fixture.CreateContext(tenantId))
        {
            seedContext.Companies.Add(company);
            await new EmployeeRepository(seedContext).AddAsync(employee);
            await seedContext.SaveChangesAsync();
        }

        // Act 1: Tracked update
        using (var trackedContext = fixture.CreateContext(tenantId))
        {
            var repo = new EmployeeRepository(trackedContext);
            var loaded = (await repo.GetByIdAsync(employee.Id))!;
            loaded.AddOrUpdateSecondarySkill(StationRole.Cashier, SkillProficiency.Support);
            repo.Update(loaded);
            await trackedContext.SaveChangesAsync();
        }

        // Act 2: Detached update
        Employee detached;
        using (var noTrackContext = fixture.CreateContext(tenantId))
        {
            detached = await noTrackContext.Employees.AsNoTracking().SingleAsync(e => e.Id == employee.Id);
        }

        detached.Deactivate();
        using (var detachedContext = fixture.CreateContext(tenantId))
        {
            var repo = new EmployeeRepository(detachedContext);
            repo.Update(detached);
            await detachedContext.SaveChangesAsync();
        }

        // Assert
        using var verifyContext = fixture.CreateContext(tenantId);
        var persisted = await new EmployeeRepository(verifyContext).GetByIdAsync(employee.Id);
        persisted.Should().NotBeNull();
        persisted!.IsActive.Should().BeFalse();
        persisted.SecondarySkills.Should().ContainSingle(s => s.Role == StationRole.Cashier && s.Proficiency == SkillProficiency.Support);
    }

    private static Company CreateCompany(Guid tenantId, string name) =>
        Company.Create(
            tenantId,
            name,
            BusinessType.Restaurant,
            new TimeOnly(11, 0),
            new TimeOnly(23, 0),
            ScheduleModel.RotatingDaysOff).Value;
}

