using Domain.Entities;
using Domain.Enums;
using Infrastructure.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Data.UnitTests.Repositories;

public class CompanyRepositoryTests
{
    [Fact]
    public void Constructor_WhenDbContextIsNull_ShouldThrowArgumentNullException()
    {
        // Act
        var act = () => new CompanyRepository(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("dbContext");
    }

    [Fact]
    public async Task AddAsync_WhenCompanyIsNull_ShouldThrowArgumentNullException()
    {
        // Arrange
        using var fixture = new RepositorySqliteFixture();
        using var context = fixture.CreateContext(Guid.NewGuid());
        var repository = new CompanyRepository(context);

        // Act
        var act = async () => await repository.AddAsync(null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("company");
    }

    [Fact]
    public void Update_WhenCompanyIsNull_ShouldThrowArgumentNullException()
    {
        // Arrange
        using var fixture = new RepositorySqliteFixture();
        using var context = fixture.CreateContext(Guid.NewGuid());
        var repository = new CompanyRepository(context);

        // Act
        var act = () => repository.Update(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("company");
    }

    [Fact]
    public async Task AddAsyncAndGetByIdAsync_WhenCompanyBelongsToCurrentTenant_ShouldPersistAndReturnTrackedEntity()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        using var fixture = new RepositorySqliteFixture();
        var company = Company.Create(
            tenantId,
            "Restaurante Maré",
            BusinessType.Restaurant,
            new TimeOnly(11, 0),
            new TimeOnly(23, 0),
            ScheduleModel.FixedClosure,
            [DayOfWeek.Monday]).Value;

        using (var writeContext = fixture.CreateContext(tenantId))
        {
            var writeRepository = new CompanyRepository(writeContext);
            await writeRepository.AddAsync(company);
            await writeContext.SaveChangesAsync();
        }

        // Act
        using var readContext = fixture.CreateContext(tenantId);
        var readRepository = new CompanyRepository(readContext);
        var loaded = await readRepository.GetByIdAsync(company.Id);

        // Assert
        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be(company.Id);
        loaded.Name.Should().Be("Restaurante Maré");
        loaded.ClosedDays.Should().ContainSingle().Which.Should().Be(DayOfWeek.Monday);
        readContext.Entry(loaded).State.Should().Be(EntityState.Unchanged);
    }

    [Fact]
    public async Task GetByIdAsync_WhenIdIsEmptyOrBelongsToAnotherTenant_ShouldReturnNull()
    {
        // Arrange
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        using var fixture = new RepositorySqliteFixture();

        var companyB = Company.Create(
            tenantB,
            "Bistrô Beta",
            BusinessType.Bar,
            new TimeOnly(17, 0),
            new TimeOnly(23, 30),
            ScheduleModel.RotatingDaysOff).Value;

        using (var seedContext = fixture.CreateContext(tenantB))
        {
            await new CompanyRepository(seedContext).AddAsync(companyB);
            await seedContext.SaveChangesAsync();
        }

        // Act
        using var contextA = fixture.CreateContext(tenantA);
        var repoA = new CompanyRepository(contextA);
        var emptyResult = await repoA.GetByIdAsync(Guid.Empty);
        var crossTenantResult = await repoA.GetByIdAsync(companyB.Id);

        // Assert
        emptyResult.Should().BeNull();
        crossTenantResult.Should().BeNull();
    }

    [Fact]
    public async Task GetCurrentAsync_WhenTenantHasCompany_ShouldReturnCurrentTenantCompanyOnly()
    {
        // Arrange
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        using var fixture = new RepositorySqliteFixture();

        var companyA = Company.Create(
            tenantA,
            "Casa Alfa",
            BusinessType.Restaurant,
            new TimeOnly(11, 0),
            new TimeOnly(22, 0),
            ScheduleModel.RotatingDaysOff).Value;

        var companyB = Company.Create(
            tenantB,
            "Casa Beta",
            BusinessType.Bar,
            new TimeOnly(18, 0),
            new TimeOnly(2, 0),
            ScheduleModel.RotatingDaysOff).Value;

        using (var seedA = fixture.CreateContext(tenantA))
        {
            await new CompanyRepository(seedA).AddAsync(companyA);
            await seedA.SaveChangesAsync();
        }

        using (var seedB = fixture.CreateContext(tenantB))
        {
            await new CompanyRepository(seedB).AddAsync(companyB);
            await seedB.SaveChangesAsync();
        }

        // Act
        using var readContextA = fixture.CreateContext(tenantA);
        var currentA = await new CompanyRepository(readContextA).GetCurrentAsync();

        using var unauthContext = fixture.CreateContext(Guid.Empty);
        var currentUnauth = await new CompanyRepository(unauthContext).GetCurrentAsync();

        // Assert
        currentA.Should().NotBeNull();
        currentA!.Id.Should().Be(companyA.Id);
        currentA.TenantId.Should().Be(tenantA);
        readContextA.Entry(currentA).State.Should().Be(EntityState.Unchanged);
        currentUnauth.Should().BeNull();
    }

    [Fact]
    public async Task Update_WhenCompanyIsTrackedOrDetached_ShouldPersistChanges()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        using var fixture = new RepositorySqliteFixture();
        var company = Company.Create(
            tenantId,
            "Casa Original",
            BusinessType.Restaurant,
            new TimeOnly(11, 0),
            new TimeOnly(23, 0),
            ScheduleModel.RotatingDaysOff).Value;

        using (var seedContext = fixture.CreateContext(tenantId))
        {
            await new CompanyRepository(seedContext).AddAsync(company);
            await seedContext.SaveChangesAsync();
        }

        // Act 1: Tracked update
        using (var trackedContext = fixture.CreateContext(tenantId))
        {
            var repo = new CompanyRepository(trackedContext);
            var loaded = (await repo.GetByIdAsync(company.Id))!;
            loaded.UpdateProfile("Casa Tracked", BusinessType.Steakhouse);
            repo.Update(loaded);
            await trackedContext.SaveChangesAsync();
        }

        // Act 2: Detached update
        Company detachedCopy;
        using (var noTrackContext = fixture.CreateContext(tenantId))
        {
            detachedCopy = await noTrackContext.Companies.AsNoTracking().SingleAsync(c => c.Id == company.Id);
        }

        detachedCopy.UpdateProfile("Casa Detached", BusinessType.Pizzeria);
        using (var detachedContext = fixture.CreateContext(tenantId))
        {
            var repo = new CompanyRepository(detachedContext);
            repo.Update(detachedCopy);
            await detachedContext.SaveChangesAsync();
        }

        // Assert
        using var verifyContext = fixture.CreateContext(tenantId);
        var persisted = await new CompanyRepository(verifyContext).GetByIdAsync(company.Id);
        persisted.Should().NotBeNull();
        persisted!.Name.Should().Be("Casa Detached");
        persisted.BusinessType.Should().Be(BusinessType.Pizzeria);
    }
}

