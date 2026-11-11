namespace Domain.Common;

public abstract class BaseEntity : IEquatable<BaseEntity>
{
    public Guid Id { get; protected init; }

    protected BaseEntity()
    {
        Id = Guid.NewGuid();
    }

    protected BaseEntity(Guid id)
    {
        Id = id;
    }

    public bool Equals(BaseEntity? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (GetType() != other.GetType())
        {
            return false;
        }

        if (Id == Guid.Empty || other.Id == Guid.Empty)
        {
            return false;
        }

        return Id == other.Id;
    }

    public override bool Equals(object? obj) =>
        obj is BaseEntity other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(GetType(), Id);

    public static bool operator ==(BaseEntity? left, BaseEntity? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(BaseEntity? left, BaseEntity? right) =>
        !(left == right);
}

