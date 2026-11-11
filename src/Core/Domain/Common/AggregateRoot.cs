namespace Domain.Common;

public abstract class AggregateRoot : BaseEntity
{
    protected AggregateRoot()
    {
    }

    protected AggregateRoot(Guid id)
        : base(id)
    {
    }
}

