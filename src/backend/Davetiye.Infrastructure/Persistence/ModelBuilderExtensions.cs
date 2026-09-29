using System.Linq.Expressions;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Davetiye.Infrastructure.Persistence;

internal static class ModelBuilderExtensions
{
    public static void ApplyDavetiyePersistenceConventions(this ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            ApplyTableAndConstraintNaming(entityType);
            ApplyPropertyConventions(entityType);
            ApplySoftDeleteConvention(entityType);
        }
    }

    private static void ApplyTableAndConstraintNaming(IMutableEntityType entityType)
    {
        var tableName = entityType.GetTableName();
        if (tableName is not null)
        {
            entityType.SetTableName(ToSnakeCase(tableName));
        }

        foreach (var property in entityType.GetProperties())
        {
            property.SetColumnName(ToSnakeCase(property.Name));
        }

        foreach (var key in entityType.GetKeys())
        {
            key.SetName(ToSnakeCase(key.GetName() ?? $"pk_{entityType.DisplayName()}"));
        }

        foreach (var foreignKey in entityType.GetForeignKeys())
        {
            var fallbackName = $"fk_{entityType.DisplayName()}_{foreignKey.PrincipalEntityType.DisplayName()}";
            foreignKey.SetConstraintName(ToSnakeCase(foreignKey.GetConstraintName() ?? fallbackName));
        }

        foreach (var index in entityType.GetIndexes())
        {
            var fallbackName = $"ix_{entityType.DisplayName()}_{string.Join('_', index.Properties.Select(p => p.Name))}";
            index.SetDatabaseName(ToSnakeCase(index.GetDatabaseName() ?? fallbackName));
        }
    }

    private static void ApplyPropertyConventions(IMutableEntityType entityType)
    {
        foreach (var property in entityType.GetProperties())
        {
            var propertyType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;

            if (propertyType == typeof(Guid))
            {
                property.SetColumnType("uuid");
            }

            if (propertyType == typeof(DateTimeOffset))
            {
                property.SetColumnType("timestamp with time zone");
            }

            if (propertyType == typeof(DateTime))
            {
                throw new InvalidOperationException(
                    $"{entityType.DisplayName()}.{property.Name} uses DateTime. Persist UTC instants as DateTimeOffset.");
            }

            if (propertyType == typeof(decimal) &&
                property.Name.EndsWith("Amount", StringComparison.Ordinal))
            {
                property.SetPrecision(PersistenceConstants.MoneyPrecision);
                property.SetScale(PersistenceConstants.MoneyScale);
                property.SetColumnType(
                    $"numeric({PersistenceConstants.MoneyPrecision},{PersistenceConstants.MoneyScale})");
            }

            if (property.Name == "Currency" && propertyType == typeof(string))
            {
                property.SetMaxLength(PersistenceConstants.CurrencyCodeLength);
                property.SetIsFixedLength(true);
            }

            if (property.Name == "Revision")
            {
                if (propertyType != typeof(long))
                {
                    throw new InvalidOperationException(
                        $"{entityType.DisplayName()}.Revision must be Int64.");
                }

                property.IsConcurrencyToken = true;
            }
        }
    }

    private static void ApplySoftDeleteConvention(IMutableEntityType entityType)
    {
        var deletedAt = entityType.FindProperty("DeletedAt");
        var purgeAfter = entityType.FindProperty("PurgeAfter");

        if (deletedAt is null && purgeAfter is null)
        {
            return;
        }

        if (deletedAt is null || purgeAfter is null ||
            deletedAt.ClrType != typeof(DateTimeOffset?) ||
            purgeAfter.ClrType != typeof(DateTimeOffset?))
        {
            throw new InvalidOperationException(
                $"{entityType.DisplayName()} soft deletion requires nullable DateTimeOffset DeletedAt and PurgeAfter properties.");
        }

        var parameter = Expression.Parameter(entityType.ClrType, "entity");
        var property = Expression.Property(parameter, deletedAt.PropertyInfo!);
        var predicate = Expression.Equal(property, Expression.Constant(null, typeof(DateTimeOffset?)));
        entityType.SetQueryFilter(Expression.Lambda(predicate, parameter));
    }

    private static string ToSnakeCase(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var result = new StringBuilder(value.Length + 8);
        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            if (char.IsUpper(current) && index > 0 &&
                (char.IsLower(value[index - 1]) ||
                 (index + 1 < value.Length && char.IsLower(value[index + 1]))))
            {
                result.Append('_');
            }

            result.Append(char.ToLowerInvariant(current));
        }

        return result.ToString();
    }
}
