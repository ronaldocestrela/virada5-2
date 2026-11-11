using Domain.Common;

namespace Domain.UnitTests.Common;

public class BaseEntityTests
{
    private sealed class DummyEntity : BaseEntity
    {
        public DummyEntity()
        {
        }

        public DummyEntity(Guid id)
            : base(id)
        {
        }
    }

    private sealed class AnotherDummyEntity : BaseEntity
    {
        public AnotherDummyEntity(Guid id)
            : base(id)
        {
        }
    }

    [Fact]
    public void Constructor_WhenCalledWithoutParameters_ShouldGenerateNonEmptyGuidId()
    {
        // Arrange & Act
        var entity = new DummyEntity();

        // Assert
        entity.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void Constructor_WhenCalledWithSpecificId_ShouldSetIdProperty()
    {
        // Arrange
        var expectedId = Guid.NewGuid();

        // Act
        var entity = new DummyEntity(expectedId);

        // Assert
        entity.Id.Should().Be(expectedId);
    }

    [Fact]
    public void Equals_WhenSameReference_ShouldReturnTrue()
    {
        // Arrange
        var entity = new DummyEntity(Guid.Empty);

        // Act
        var isEqual = entity.Equals(entity);

        // Assert
        isEqual.Should().BeTrue();
    }

    [Fact]
    public void Equals_WhenSameIdAndSameType_ShouldReturnTrue()
    {
        // Arrange
        var id = Guid.NewGuid();
        var entity1 = new DummyEntity(id);
        var entity2 = new DummyEntity(id);

        // Act & Assert
        entity1.Equals(entity2).Should().BeTrue();
        entity1.Equals((object)entity2).Should().BeTrue();
    }

    [Fact]
    public void Equals_WhenDifferentId_ShouldReturnFalse()
    {
        // Arrange
        var entity1 = new DummyEntity(Guid.NewGuid());
        var entity2 = new DummyEntity(Guid.NewGuid());

        // Act & Assert
        entity1.Equals(entity2).Should().BeFalse();
    }

    [Fact]
    public void Equals_WhenDifferentDerivedTypesWithSameId_ShouldReturnFalse()
    {
        // Arrange
        var id = Guid.NewGuid();
        var entity1 = new DummyEntity(id);
        var entity2 = new AnotherDummyEntity(id);

        // Act & Assert
        entity1.Equals(entity2).Should().BeFalse();
    }

    [Fact]
    public void Equals_WhenComparedWithNull_ShouldReturnFalse()
    {
        // Arrange
        var entity = new DummyEntity(Guid.NewGuid());
        BaseEntity? nullBaseEntity = null;
        object? nullObject = null;

        // Act
        var equalsNullEntity = entity.Equals(nullBaseEntity);
        var equalsNullObject = entity.Equals(nullObject);

        // Assert
        equalsNullEntity.Should().BeFalse();
        equalsNullObject.Should().BeFalse();
    }

    [Fact]
    public void Equals_WhenComparedWithNonEntityObject_ShouldReturnFalse()
    {
        // Arrange
        var entity = new DummyEntity(Guid.NewGuid());
        var nonEntity = new object();

        // Act & Assert
        entity.Equals(nonEntity).Should().BeFalse();
    }

    [Fact]
    public void Equals_WhenEitherIdIsEmpty_ShouldReturnFalseForDistinctInstances()
    {
        // Arrange
        var entity1 = new DummyEntity(Guid.Empty);
        var entity2 = new DummyEntity(Guid.Empty);

        // Act & Assert
        entity1.Equals(entity2).Should().BeFalse();
    }

    [Fact]
    public void EqualityOperators_WhenBothAreNull_ShouldReturnTrueForEqualsAndFalseForNotEquals()
    {
        // Arrange
        DummyEntity? left = null;
        DummyEntity? right = null;

        // Act & Assert
        (left == right).Should().BeTrue();
        (left != right).Should().BeFalse();
    }

    [Fact]
    public void EqualityOperators_WhenOneIsNull_ShouldReturnFalseForEqualsAndTrueForNotEquals()
    {
        // Arrange
        var entity = new DummyEntity(Guid.NewGuid());
        DummyEntity? nullEntity = null;

        // Act & Assert
        (entity == nullEntity).Should().BeFalse();
        (nullEntity == entity).Should().BeFalse();
        (entity != nullEntity).Should().BeTrue();
        (nullEntity != entity).Should().BeTrue();
    }

    [Fact]
    public void EqualityOperators_WhenSameIdAndSameType_ShouldReturnTrueForEqualsAndFalseForNotEquals()
    {
        // Arrange
        var id = Guid.NewGuid();
        var left = new DummyEntity(id);
        var right = new DummyEntity(id);

        // Act & Assert
        (left == right).Should().BeTrue();
        (left != right).Should().BeFalse();
    }

    [Fact]
    public void GetHashCode_WhenSameIdAndSameType_ShouldReturnSameHashCode()
    {
        // Arrange
        var id = Guid.NewGuid();
        var entity1 = new DummyEntity(id);
        var entity2 = new DummyEntity(id);

        // Act & Assert
        entity1.GetHashCode().Should().Be(entity2.GetHashCode());
    }
}
