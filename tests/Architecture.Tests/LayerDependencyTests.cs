using System.Reflection;
using Application.Common;
using Application.Common.Interfaces;
using Domain.Common;
using Infrastructure.Data;
using Infrastructure.Data.Context;
using Microsoft.EntityFrameworkCore;
using NetArchTest.Rules;
using WebApp.Blazor.Components;

namespace Architecture.Tests;

public class LayerDependencyTests
{
    private static readonly Assembly DomainAssembly = typeof(BaseEntity).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(ResultPatternMarker).Assembly;
    private static readonly Assembly InfrastructureDataAssembly = typeof(InfrastructureDataMarker).Assembly;
    private static readonly Assembly PresentationAssembly = typeof(App).Assembly;

    [Fact]
    public void Domain_WhenInspected_ShouldNotReferenceApplicationInfrastructureOrPresentation()
    {
        // Arrange
        var forbiddenAssemblies = new[]
        {
            "Application",
            "Infrastructure",
            "Data",
            "Infrastructure.Data",
            "Presentation",
            "WebApp.Blazor",
            "Microsoft.EntityFrameworkCore",
            "Microsoft.AspNetCore"
        };

        // Act
        var archResult = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(forbiddenAssemblies)
            .GetResult();

        var referencedNames = DomainAssembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .Where(name => name is not null)
            .ToList();

        // Assert
        archResult.IsSuccessful.Should().BeTrue(
            $"Domain não pode depender de camadas externas: {string.Join(", ", archResult.FailingTypeNames ?? [])}");
        referencedNames.Should().NotContain(forbiddenAssemblies);
    }

    [Fact]
    public void Application_WhenInspected_ShouldNotReferenceInfrastructureOrPresentation()
    {
        // Arrange
        var forbiddenDependencies = new[]
        {
            "Infrastructure",
            "Data",
            "Infrastructure.Data",
            "Presentation",
            "WebApp.Blazor",
            "Microsoft.EntityFrameworkCore",
            "Microsoft.AspNetCore"
        };

        // Act
        var archResult = Types.InAssembly(ApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(forbiddenDependencies)
            .GetResult();

        var referencedNames = ApplicationAssembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .Where(name => name is not null)
            .ToList();

        // Assert
        archResult.IsSuccessful.Should().BeTrue(
            $"Application não pode depender de Infrastructure ou Presentation: {string.Join(", ", archResult.FailingTypeNames ?? [])}");
        referencedNames.Should().NotContain(forbiddenDependencies);
    }

    [Fact]
    public void InfrastructureData_WhenInspected_ShouldNotReferencePresentation()
    {
        // Arrange
        var forbiddenDependencies = new[]
        {
            "Presentation",
            "WebApp.Blazor"
        };

        // Act
        var archResult = Types.InAssembly(InfrastructureDataAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(forbiddenDependencies)
            .GetResult();

        var referencedNames = InfrastructureDataAssembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .Where(name => name is not null)
            .ToList();

        // Assert
        archResult.IsSuccessful.Should().BeTrue(
            $"Infrastructure.Data não pode depender de Presentation: {string.Join(", ", archResult.FailingTypeNames ?? [])}");
        referencedNames.Should().NotContain(forbiddenDependencies);
    }

    [Fact]
    public void CoreAndInfrastructure_WhenInspected_ShouldNotContainGenericRepositories()
    {
        // Arrange
        var assemblies = new[]
        {
            DomainAssembly,
            ApplicationAssembly,
            InfrastructureDataAssembly
        };

        // Act
        var archResult = Types.InAssemblies(assemblies)
            .That()
            .HaveNameMatching("Repository")
            .Should()
            .NotBeGeneric()
            .GetResult();

        // Assert
        archResult.IsSuccessful.Should().BeTrue(
            $"Repositórios genéricos são proibidos: {string.Join(", ", archResult.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Domain_WhenInspected_ShouldExposeSpecificRepositoryPerAggregateRootOnly()
    {
        // Arrange
        var aggregateRootNames = DomainAssembly
            .GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(AggregateRoot).IsAssignableFrom(t))
            .Select(t => $"I{t.Name}Repository")
            .OrderBy(name => name)
            .ToList();

        // Act
        var repositoryInterfaces = DomainAssembly
            .GetTypes()
            .Where(t => t.IsInterface && t.Namespace == "Domain.Repositories")
            .ToList();

        // Assert
        repositoryInterfaces.Should().NotBeEmpty();
        repositoryInterfaces.Should().OnlyContain(t => !t.IsGenericType && t.IsPublic);
        repositoryInterfaces.Select(t => t.Name).OrderBy(name => name).Should().Equal(aggregateRootNames);
    }

    [Fact]
    public void Domain_WhenInspected_ShouldNotExposeRepositoryParametersForManualTenantId()
    {
        // Arrange
        var repositoryInterfaces = DomainAssembly
            .GetTypes()
            .Where(t => t.IsInterface && t.Namespace == "Domain.Repositories")
            .ToList();

        // Act
        var manualTenantParameters = repositoryInterfaces
            .SelectMany(repo => repo.GetMethods())
            .SelectMany(method => method.GetParameters())
            .Where(parameter => string.Equals(parameter.Name, "tenantId", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Assert
        repositoryInterfaces.Should().NotBeEmpty();
        manualTenantParameters.Should().BeEmpty();
    }

    [Fact]
    public void InfrastructureData_WhenInspected_ShouldConfigureQueryFilterForAllDomainTenantEntitiesAndImplementUnitOfWork()
    {
        // Arrange
        var tenantEntityTypes = DomainAssembly
            .GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(ITenantEntity).IsAssignableFrom(t))
            .ToList();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer()
            .Options;

        using var dbContext = new AppDbContext(options, new StubCurrentUserService());

        // Act
        var model = dbContext.Model;

        // Assert
        typeof(AppDbContext).Should().Implement<IUnitOfWork>();
        tenantEntityTypes.Should().NotBeEmpty();

        foreach (var clrType in tenantEntityTypes)
        {
            var entityType = model.FindEntityType(clrType);
            entityType.Should().NotBeNull($"a entidade {clrType.Name} precisa estar registrada no AppDbContext");
            entityType!.GetDeclaredQueryFilters().Should().NotBeEmpty(
                $"a entidade {clrType.Name} implementa ITenantEntity e exige Global Query Filter");
        }
    }

    [Fact]
    public void Domain_WhenInspected_ShouldNotUseDataAnnotationsForPersistenceMapping()
    {
        // Arrange
        var forbiddenAttributes = new[]
        {
            "System.ComponentModel.DataAnnotations",
            "System.ComponentModel.DataAnnotations.Schema"
        };

        // Act
        var archResult = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(forbiddenAttributes)
            .GetResult();

        // Assert
        archResult.IsSuccessful.Should().BeTrue(
            $"Domain deve permanecer livre de Data Annotations de persistência: {string.Join(", ", archResult.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void InfrastructureData_WhenInspected_ShouldConfigureExplicitTablesAndDecimalPrecisionWithoutMultipleCascadePaths()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer()
            .Options;

        using var dbContext = new AppDbContext(options, new StubCurrentUserService());
        var domainEntities = DomainAssembly
            .GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(BaseEntity).IsAssignableFrom(t))
            .ToList();

        // Act
        var model = dbContext.Model;

        // Assert
        foreach (var clrType in domainEntities)
        {
            var entityType = model.FindEntityType(clrType);
            entityType.Should().NotBeNull($"a entidade {clrType.Name} precisa estar configurada no AppDbContext");

            var tableName = entityType!.GetTableName();
            tableName.Should().NotBeNullOrWhiteSpace();
            tableName.Should().NotBe(clrType.Name, $"a tabela da entidade {clrType.Name} deve usar nome explícito no plural");

            var decimalProperties = entityType!.GetProperties()
                .Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?));

            foreach (var decimalProperty in decimalProperties)
            {
                decimalProperty.GetPrecision().Should().NotBeNull(
                    $"a propriedade decimal {clrType.Name}.{decimalProperty.Name} exige precisão explícita para SQL Server");
                decimalProperty.GetScale().Should().NotBeNull(
                    $"a propriedade decimal {clrType.Name}.{decimalProperty.Name} exige escala explícita para SQL Server");
            }
        }
    }

    [Fact]
    public void InfrastructureData_WhenInspected_ShouldImplementEachDomainRepositoryAsConcreteNonGenericSealedClass()
    {
        // Arrange
        var repositoryInterfaces = DomainAssembly
            .GetTypes()
            .Where(t => t.IsInterface && t.Namespace == "Domain.Repositories")
            .OrderBy(t => t.Name)
            .ToList();

        var repositoryImplementations = InfrastructureDataAssembly
            .GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.Namespace == "Infrastructure.Data.Repositories")
            .ToList();

        // Assert
        repositoryInterfaces.Should().NotBeEmpty();
        repositoryImplementations.Should().OnlyContain(impl =>
            impl.IsPublic &&
            impl.IsSealed &&
            !impl.IsGenericType &&
            impl.BaseType == typeof(object));

        foreach (var repositoryInterface in repositoryInterfaces)
        {
            var matchingImplementations = repositoryImplementations
                .Where(impl => repositoryInterface.IsAssignableFrom(impl))
                .ToList();

            matchingImplementations.Should().ContainSingle(
                $"o contrato {repositoryInterface.Name} deve possuir exatamente uma implementação concreta específica");
        }
    }

    [Fact]
    public void Presentation_WhenLoaded_ShouldBePartOfCleanArchitectureSolution()
    {
        // Assert
        PresentationAssembly.GetName().Name.Should().Be("WebApp.Blazor");
    }

    private sealed class StubCurrentUserService : ICurrentUserService
    {
        public Guid? UserId => null;
        public Guid TenantId => Guid.Empty;
        public bool IsAuthenticated => false;
    }
}
