using Domain.Entities;
using Domain.Enums;
using Infrastructure.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Data.UnitTests.Repositories;

public class DemandSlotRepositoryTests
{
    [Fact]
    public void Constructor_WhenDbContextIsNull_ShouldThrowArgumentNullException()
    {
        // Act
        var act = () => new DemandSlotRepository(null!);

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
        var repository = new DemandSlotRepository(context);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>("demandSlot", () => repository.AddAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>("demandSlots", () => repository.AddRangeAsync(null!));
        Assert.Throws<ArgumentNullException>("demandSlot", () => repository.Update(null!));
        Assert.Throws<ArgumentNullException>("demandSlots", () => repository.RemoveRange(null!));
    }

    [Fact]
    public async Task GetByIdAndGetByCompanyDayAndHourAsync_WhenValid_ShouldReturnTrackedSlotWithinTenant()
    {
        // Arrange
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        using var fixture = new RepositorySqliteFixture();

        var companyA = CreateCompany(tenantA, "Casa A");
        var companyB = CreateCompany(tenantB, "Casa B");
        var slotA = DemandSlot.Create(tenantA, companyA.Id, DayOfWeek.Friday, 20, 120m).Value;
        var slotB = DemandSlot.Create(tenantB, companyB.Id, DayOfWeek.Friday, 20, 90m).Value;

        using (var seedA = fixture.CreateContext(tenantA))
        {
            seedA.Companies.Add(companyA);
            await new DemandSlotRepository(seedA).AddAsync(slotA);
            await seedA.SaveChangesAsync();
        }

        using (var seedB = fixture.CreateContext(tenantB))
        {
            seedB.Companies.Add(companyB);
            await new DemandSlotRepository(seedB).AddAsync(slotB);
            await seedB.SaveChangesAsync();
        }

        // Act
        using var readContextA = fixture.CreateContext(tenantA);
        var repoA = new DemandSlotRepository(readContextA);

        var byId = await repoA.GetByIdAsync(slotA.Id);
        var byKey = await repoA.GetByCompanyDayAndHourAsync(companyA.Id, DayOfWeek.Friday, 20);
        var crossTenantById = await repoA.GetByIdAsync(slotB.Id);
        var crossTenantByKey = await repoA.GetByCompanyDayAndHourAsync(companyB.Id, DayOfWeek.Friday, 20);
        var emptyId = await repoA.GetByIdAsync(Guid.Empty);
        var invalidHour = await repoA.GetByCompanyDayAndHourAsync(companyA.Id, DayOfWeek.Friday, 25);

        // Assert
        byId.Should().NotBeNull();
        byId!.Id.Should().Be(slotA.Id);
        readContextA.Entry(byId).State.Should().Be(EntityState.Unchanged);

        byKey.Should().NotBeNull();
        byKey!.Id.Should().Be(slotA.Id);
        crossTenantById.Should().BeNull();
        crossTenantByKey.Should().BeNull();
        emptyId.Should().BeNull();
        invalidHour.Should().BeNull();
    }

    [Fact]
    public async Task GetByCompanyAsync_WhenSlotsExist_ShouldReturnNoTrackingOrderedByDayOfWeekAndHour()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        using var fixture = new RepositorySqliteFixture();

        var company = CreateCompany(tenantId, "Casa Ordenação");
        var otherCompany = CreateCompany(otherTenantId, "Outra Casa");

        // Ordem alfabética em string seria Friday < Monday < Saturday < Sunday < Wednesday
        // Ordem natural DayOfWeek: Sunday(0) < Monday(1) < Wednesday(3) < Friday(5) < Saturday(6)
        var slots = new[]
        {
            DemandSlot.Create(tenantId, company.Id, DayOfWeek.Friday, 21, 100m).Value,
            DemandSlot.Create(tenantId, company.Id, DayOfWeek.Monday, 19, 40m).Value,
            DemandSlot.Create(tenantId, company.Id, DayOfWeek.Sunday, 12, 80m).Value,
            DemandSlot.Create(tenantId, company.Id, DayOfWeek.Sunday, 20, 110m).Value,
            DemandSlot.Create(tenantId, company.Id, DayOfWeek.Saturday, 22, 130m).Value,
            DemandSlot.Create(tenantId, company.Id, DayOfWeek.Wednesday, 18, 60m).Value
        };
        var foreignSlot = DemandSlot.Create(otherTenantId, otherCompany.Id, DayOfWeek.Tuesday, 18, 50m).Value;

        using (var seedContext = fixture.CreateContext(tenantId))
        {
            seedContext.Companies.Add(company);
            await new DemandSlotRepository(seedContext).AddRangeAsync(slots);
            await seedContext.SaveChangesAsync();
        }

        using (var foreignSeedContext = fixture.CreateContext(otherTenantId))
        {
            foreignSeedContext.Companies.Add(otherCompany);
            await new DemandSlotRepository(foreignSeedContext).AddAsync(foreignSlot);
            await foreignSeedContext.SaveChangesAsync();
        }

        // Act
        using var readContext = fixture.CreateContext(tenantId);
        var repo = new DemandSlotRepository(readContext);
        var result = await repo.GetByCompanyAsync(company.Id);
        var nonMatchingCompanyResult = await repo.GetByCompanyAsync(otherCompany.Id);
        var emptyCompanyResult = await repo.GetByCompanyAsync(Guid.Empty);

        // Assert
        readContext.ChangeTracker.Entries<DemandSlot>().Should().BeEmpty();
        nonMatchingCompanyResult.Should().BeEmpty();
        emptyCompanyResult.Should().BeEmpty();
        result.Should().HaveCount(6);
        result.Select(s => (s.DayOfWeek, s.Hour)).Should().Equal(
            (DayOfWeek.Sunday, 12),
            (DayOfWeek.Sunday, 20),
            (DayOfWeek.Monday, 19),
            (DayOfWeek.Wednesday, 18),
            (DayOfWeek.Friday, 21),
            (DayOfWeek.Saturday, 22));
    }

    [Fact]
    public async Task AddRangeUpdateAndRemoveRange_WhenExecuted_ShouldPersistAllChanges()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        using var fixture = new RepositorySqliteFixture();
        var company = CreateCompany(tenantId, "Casa Mutações");

        var slot1 = DemandSlot.Create(tenantId, company.Id, DayOfWeek.Monday, 12, 25m).Value;
        var slot2 = DemandSlot.Create(tenantId, company.Id, DayOfWeek.Friday, 20, 100m).Value;

        using (var seedContext = fixture.CreateContext(tenantId))
        {
            var repo = new DemandSlotRepository(seedContext);
            seedContext.Companies.Add(company);
            await repo.AddRangeAsync([]);
            await repo.AddRangeAsync([slot1, slot2]);
            await seedContext.SaveChangesAsync();
        }

        // Act 1: Update untracked slot obtained from GetByCompanyAsync
        using (var updateContext = fixture.CreateContext(tenantId))
        {
            var repo = new DemandSlotRepository(updateContext);
            var existing = await repo.GetByCompanyAsync(company.Id);
            var target = existing.Single(s => s.Id == slot1.Id);
            target.EvaluateRelativeDemand(100m);
            repo.Update(target);
            await updateContext.SaveChangesAsync();
        }

        // Assert update persisted
        using (var verifyUpdateContext = fixture.CreateContext(tenantId))
        {
            var updated = await new DemandSlotRepository(verifyUpdateContext).GetByIdAsync(slot1.Id);
            updated.Should().NotBeNull();
            updated!.RelativeVolume.Should().Be(0.2500m);
            updated.Classification.Should().Be(DemandClassification.Idle);
        }

        // Act 2: RemoveRange (including empty no-op)
        using (var deleteContext = fixture.CreateContext(tenantId))
        {
            var repo = new DemandSlotRepository(deleteContext);
            repo.RemoveRange([]);
            var all = await repo.GetByCompanyAsync(company.Id);
            repo.RemoveRange(all);
            await deleteContext.SaveChangesAsync();
        }

        // Assert removed
        using (var verifyDeleteContext = fixture.CreateContext(tenantId))
        {
            var remaining = await new DemandSlotRepository(verifyDeleteContext).GetByCompanyAsync(company.Id);
            remaining.Should().BeEmpty();
        }
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

