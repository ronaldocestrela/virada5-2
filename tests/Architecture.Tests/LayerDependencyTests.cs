using Domain.Common;

namespace Architecture.Tests;

public class LayerDependencyTests
{
    [Fact]
    public void Domain_WhenInspected_ShouldNotReferenceApplicationInfrastructureOrPresentation()
    {
        // Arrange
        var domainAssembly = typeof(BaseEntity).Assembly;
        var forbiddenAssemblies = new[] { "Application", "Infrastructure", "Data", "Presentation", "WebApp.Blazor" };

        // Act
        var referencedNames = domainAssembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .Where(name => name is not null)
            .ToList();

        // Assert
        referencedNames.Should().NotContain(forbiddenAssemblies);
    }

    [Fact]
    public void Domain_WhenInspected_ShouldNotContainGenericRepositoryInterfaces()
    {
        // Arrange
        var domainAssembly = typeof(BaseEntity).Assembly;

        // Act
        var genericRepositories = domainAssembly
            .GetTypes()
            .Where(t => t.IsInterface && t.IsGenericType && t.Name.Contains("Repository", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Assert
        genericRepositories.Should().BeEmpty();
    }

    [Fact]
    public void Domain_WhenInspected_ShouldExposeSpecificRepositoryPerAggregateRootOnly()
    {
        // Arrange
        var domainAssembly = typeof(BaseEntity).Assembly;
        var aggregateRootNames = domainAssembly
            .GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(AggregateRoot).IsAssignableFrom(t))
            .Select(t => $"I{t.Name}Repository")
            .OrderBy(name => name)
            .ToList();

        // Act
        var repositoryInterfaces = domainAssembly
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
        var domainAssembly = typeof(BaseEntity).Assembly;
        var repositoryInterfaces = domainAssembly
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
}

