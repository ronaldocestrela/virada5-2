using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Infrastructure.Data.Context.Configurations;

internal static class CollectionMappingHelper
{
    public static void ConfigureJsonReadOnlyCollection<TEntity, TElement>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, IReadOnlyCollection<TElement>>> propertyExpression,
        string backingFieldName)
        where TEntity : class
    {
        ConfigureJsonReadOnlyCollection(
            builder,
            propertyExpression,
            backingFieldName,
            element => element,
            dto => dto);
    }

    public static void ConfigureJsonReadOnlyCollection<TEntity, TElement, TDto>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, IReadOnlyCollection<TElement>>> propertyExpression,
        string backingFieldName,
        Func<TElement, TDto> toDto,
        Func<TDto, TElement> fromDto)
        where TEntity : class
    {
        var converter = new ValueConverter<IReadOnlyCollection<TElement>, string>(
            collection => JsonSerializer.Serialize(collection.Select(toDto).ToList(), (JsonSerializerOptions?)null),
            json => string.IsNullOrWhiteSpace(json)
                ? new List<TElement>()
                : (JsonSerializer.Deserialize<List<TDto>>(json, (JsonSerializerOptions?)null) ?? new List<TDto>())
                    .Select(fromDto)
                    .ToList());

        var comparer = new ValueComparer<IReadOnlyCollection<TElement>>(
            (left, right) =>
                ReferenceEquals(left, right) ||
                (left != null && right != null && left.SequenceEqual(right)),
            collection => collection.Aggregate(
                0,
                (hash, item) => HashCode.Combine(hash, item == null ? 0 : item.GetHashCode())),
            collection => collection.ToList());

        var propertyBuilder = builder.Property(propertyExpression)
            .HasField(backingFieldName)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasConversion(converter);

        propertyBuilder.Metadata.SetValueComparer(comparer);
    }
}

