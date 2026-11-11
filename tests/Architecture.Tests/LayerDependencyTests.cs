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
}

