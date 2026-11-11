using Application.Common.Interfaces;

namespace Application.UnitTests.Common;

public class InterfacesContractTests
{
    [Fact]
    public void CurrentUserService_WhenInspected_ShouldExposeTenantAndUserContextMembers()
    {
        // Arrange
        var contract = typeof(ICurrentUserService);

        // Act
        var userIdProperty = contract.GetProperty(nameof(ICurrentUserService.UserId));
        var tenantIdProperty = contract.GetProperty(nameof(ICurrentUserService.TenantId));
        var isAuthenticatedProperty = contract.GetProperty(nameof(ICurrentUserService.IsAuthenticated));

        // Assert
        contract.IsInterface.Should().BeTrue();
        contract.IsPublic.Should().BeTrue();
        contract.Namespace.Should().Be("Application.Common.Interfaces");
        userIdProperty.Should().NotBeNull();
        userIdProperty!.PropertyType.Should().Be(typeof(Guid?));
        tenantIdProperty.Should().NotBeNull();
        tenantIdProperty!.PropertyType.Should().Be(typeof(Guid));
        isAuthenticatedProperty.Should().NotBeNull();
        isAuthenticatedProperty!.PropertyType.Should().Be(typeof(bool));
    }

    [Fact]
    public void UnitOfWork_WhenInspected_ShouldExposeSaveChangesAsyncContract()
    {
        // Arrange
        var contract = typeof(IUnitOfWork);

        // Act
        var method = contract.GetMethod(nameof(IUnitOfWork.SaveChangesAsync));

        // Assert
        contract.IsInterface.Should().BeTrue();
        contract.IsPublic.Should().BeTrue();
        contract.Namespace.Should().Be("Application.Common.Interfaces");
        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(Task<int>));

        var parameters = method.GetParameters();
        parameters.Should().ContainSingle();
        parameters[0].Name.Should().Be("ct");
        parameters[0].ParameterType.Should().Be(typeof(CancellationToken));
        parameters[0].HasDefaultValue.Should().BeTrue();
    }
}

