using Domain.Common;

namespace Domain.UnitTests.Common;

public class AggregateRootTests
{
    private sealed class DummyTenantAggregateRoot : AggregateRoot, ITenantEntity
    {
        public Guid TenantId { get; set; }

        public DummyTenantAggregateRoot()
        {
        }

        public DummyTenantAggregateRoot(Guid id, Guid tenantId)
            : base(id)
        {
            TenantId = tenantId;
        }
    }

    [Fact]
    public void Constructor_WhenCalledWithoutParameters_ShouldInitializeNonEmptyId()
    {
        // Arrange & Act
        var aggregate = new DummyTenantAggregateRoot();

        // Assert
        aggregate.Should().BeAssignableTo<BaseEntity>();
        aggregate.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void Constructor_WhenCalledWithIdAndTenantId_ShouldSetBothProperties()
    {
        // Arrange
        var expectedId = Guid.NewGuid();
        var expectedTenantId = Guid.NewGuid();

        // Act
        var aggregate = new DummyTenantAggregateRoot(expectedId, expectedTenantId);

        // Assert
        aggregate.Id.Should().Be(expectedId);
        aggregate.TenantId.Should().Be(expectedTenantId);
    }

    [Fact]
    public void ITenantEntity_WhenTenantIdIsUpdated_ShouldPersistNewValue()
    {
        // Arrange
        ITenantEntity tenantEntity = new DummyTenantAggregateRoot();
        var newTenantId = Guid.NewGuid();

        // Act
        tenantEntity.TenantId = newTenantId;

        // Assert
        tenantEntity.TenantId.Should().Be(newTenantId);
    }
}

