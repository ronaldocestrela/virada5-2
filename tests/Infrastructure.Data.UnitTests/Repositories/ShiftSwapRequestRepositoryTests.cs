using Domain.Entities;
using Domain.Enums;
using Infrastructure.Data.Repositories;

namespace Infrastructure.Data.UnitTests.Repositories;

public class ShiftSwapRequestRepositoryTests
{
    [Fact]
    public void Constructor_WhenDbContextIsNull_ShouldThrowArgumentNullException()
    {
        // Act
        var act = () => new ShiftSwapRequestRepository(null!);

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
        var repository = new ShiftSwapRequestRepository(context);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>("request", () => repository.AddAsync(null!));
        Assert.Throws<ArgumentNullException>("request", () => repository.Update(null!));
    }

    [Fact]
    public async Task AddAsyncAndGetByIdAsync_WhenTenantMatches_ShouldReturnTrackedEntityAndIsolateOtherTenant()
    {
        // Arrange
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        using var fixture = new RepositorySqliteFixture();

        var (companyA, empA1, empA2, rosterA, shiftA1, shiftA2) = CreateSeedGraph(tenantA, "Casa A");
        var (companyB, empB1, empB2, rosterB, shiftB1, shiftB2) = CreateSeedGraph(tenantB, "Casa B");

        var swapA = ShiftSwapRequest.Create(
            tenantA, companyA.Id, rosterA.Id, empA1.Id, shiftA1.Id, empA2.Id, shiftA2.Id, "Troca A").Value;
        var swapB = ShiftSwapRequest.Create(
            tenantB, companyB.Id, rosterB.Id, empB1.Id, shiftB1.Id, empB2.Id, shiftB2.Id, "Troca B").Value;

        using (var seedA = fixture.CreateContext(tenantA))
        {
            seedA.Companies.Add(companyA);
            seedA.Employees.AddRange(empA1, empA2);
            seedA.Rosters.Add(rosterA);
            await new ShiftSwapRequestRepository(seedA).AddAsync(swapA);
            await seedA.SaveChangesAsync();
        }

        using (var seedB = fixture.CreateContext(tenantB))
        {
            seedB.Companies.Add(companyB);
            seedB.Employees.AddRange(empB1, empB2);
            seedB.Rosters.Add(rosterB);
            await new ShiftSwapRequestRepository(seedB).AddAsync(swapB);
            await seedB.SaveChangesAsync();
        }

        // Act
        using var readA = fixture.CreateContext(tenantA);
        var repoA = new ShiftSwapRequestRepository(readA);

        var loadedA = await repoA.GetByIdAsync(swapA.Id);
        var crossTenant = await repoA.GetByIdAsync(swapB.Id);
        var emptyLookup = await repoA.GetByIdAsync(Guid.Empty);

        // Assert
        loadedA.Should().NotBeNull();
        loadedA!.Reason.Should().Be("Troca A");
        loadedA.Status.Should().Be(ShiftSwapStatus.PendingPeerApproval);
        crossTenant.Should().BeNull();
        emptyLookup.Should().BeNull();
    }

    [Fact]
    public async Task Queries_WhenFilteringByRosterCompanyPendingAndShift_ShouldReturnExpectedResults()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        using var fixture = new RepositorySqliteFixture();
        var (company, emp1, emp2, roster, shift1, shift2) = CreateSeedGraph(tenantId, "Bistrô Central");

        var pendingPeer = ShiftSwapRequest.Create(
            tenantId, company.Id, roster.Id, emp1.Id, shift1.Id, emp2.Id, shift2.Id,
            requirePeerApproval: true,
            utcNow: new DateTime(2026, 11, 11, 10, 0, 0, DateTimeKind.Utc)).Value;

        var emp3 = Employee.Create(tenantId, company.Id, "Carlos", StationRole.Waiter).Value;
        var shift3 = roster.AssignShift(emp3, roster.WeekStartDate.AddDays(3), new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;

        var rejectedSwap = ShiftSwapRequest.Create(
            tenantId, company.Id, roster.Id, emp1.Id, shift1.Id, emp3.Id, shift3.Id,
            requirePeerApproval: false,
            utcNow: new DateTime(2026, 11, 11, 9, 0, 0, DateTimeKind.Utc)).Value;
        rejectedSwap.RejectByManager(Guid.NewGuid(), "Recusada").IsSuccess.Should().BeTrue();

        using (var seed = fixture.CreateContext(tenantId))
        {
            seed.Companies.Add(company);
            seed.Employees.AddRange(emp1, emp2, emp3);
            seed.Rosters.Add(roster);
            var repo = new ShiftSwapRequestRepository(seed);
            await repo.AddAsync(pendingPeer);
            await repo.AddAsync(rejectedSwap);
            await seed.SaveChangesAsync();
        }

        // Act
        using var readContext = fixture.CreateContext(tenantId);
        var readRepo = new ShiftSwapRequestRepository(readContext);

        var byRoster = await readRepo.GetByRosterAsync(roster.Id);
        var pendingByCompany = await readRepo.GetPendingByCompanyAsync(company.Id);
        var shift1HasActive = await readRepo.HasActiveRequestForShiftAsync(shift1.Id);
        var shift2HasActive = await readRepo.HasActiveRequestForShiftAsync(shift2.Id);
        var shift3HasActive = await readRepo.HasActiveRequestForShiftAsync(shift3.Id);

        // Assert
        byRoster.Should().HaveCount(2);
        pendingByCompany.Should().ContainSingle(r => r.Id == pendingPeer.Id);
        shift1HasActive.Should().BeTrue();
        shift2HasActive.Should().BeTrue();
        shift3HasActive.Should().BeFalse();
    }

    [Fact]
    public async Task Update_WhenApprovingDetachedEntity_ShouldPersistNewStatus()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        using var fixture = new RepositorySqliteFixture();
        var (company, emp1, emp2, roster, shift1, shift2) = CreateSeedGraph(tenantId, "Casa Update");

        var swap = ShiftSwapRequest.Create(
            tenantId, company.Id, roster.Id, emp1.Id, shift1.Id, emp2.Id, shift2.Id,
            requirePeerApproval: false).Value;

        using (var seed = fixture.CreateContext(tenantId))
        {
            seed.Companies.Add(company);
            seed.Employees.AddRange(emp1, emp2);
            seed.Rosters.Add(roster);
            await new ShiftSwapRequestRepository(seed).AddAsync(swap);
            await seed.SaveChangesAsync();
        }

        swap.Approve(Guid.NewGuid(), "Tudo certo").IsSuccess.Should().BeTrue();

        // Act
        using (var updateContext = fixture.CreateContext(tenantId))
        {
            var repo = new ShiftSwapRequestRepository(updateContext);
            repo.Update(swap);
            await updateContext.SaveChangesAsync();
        }

        // Assert
        using var verifyContext = fixture.CreateContext(tenantId);
        var loaded = await new ShiftSwapRequestRepository(verifyContext).GetByIdAsync(swap.Id);
        loaded.Should().NotBeNull();
        loaded!.Status.Should().Be(ShiftSwapStatus.Approved);
        loaded.DecisionNotes.Should().Be("Tudo certo");
    }

    private static (
        Company Company,
        Employee Emp1,
        Employee Emp2,
        Roster Roster,
        Shift Shift1,
        Shift Shift2) CreateSeedGraph(Guid tenantId, string companyName)
    {
        var company = Company.Create(
            tenantId,
            companyName,
            BusinessType.Restaurant,
            new TimeOnly(11, 0),
            new TimeOnly(23, 0),
            ScheduleModel.RotatingDaysOff).Value;

        var emp1 = Employee.Create(tenantId, company.Id, "Ana", StationRole.Waiter).Value;
        var emp2 = Employee.Create(tenantId, company.Id, "Bruno", StationRole.Waiter).Value;

        var weekStart = new DateOnly(2026, 11, 9);
        var roster = Roster.Create(tenantId, company.Id, weekStart).Value;
        var shift1 = roster.AssignShift(emp1, weekStart, new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;
        var shift2 = roster.AssignShift(emp2, weekStart.AddDays(2), new TimeOnly(11, 0), new TimeOnly(19, 0), StationRole.Waiter).Value;

        return (company, emp1, emp2, roster, shift1, shift2);
    }
}

