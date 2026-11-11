using Infrastructure.Data.Identity;

namespace Infrastructure.Data.UnitTests.Identity;

public class ApplicationRoleTests
{
    [Fact]
    public void Constructor_WhenRoleNameAndDescriptionProvided_ShouldInitializeProperties()
    {
        // Act
        var role = new ApplicationRole(ApplicationRoles.Owner, "Proprietário do estabelecimento");

        // Assert
        role.Id.Should().NotBe(Guid.Empty);
        role.Name.Should().Be(ApplicationRoles.Owner);
        role.Description.Should().Be("Proprietário do estabelecimento");
    }

    [Fact]
    public void ApplicationRoles_WhenInspected_ShouldExposeExpectedStandardRoles()
    {
        // Assert
        ApplicationRoles.Owner.Should().Be("Owner");
        ApplicationRoles.Manager.Should().Be("Manager");
        ApplicationRoles.Employee.Should().Be("Employee");
        ApplicationRoles.All.Should().BeEquivalentTo(["Owner", "Manager", "Employee"]);
    }
}

