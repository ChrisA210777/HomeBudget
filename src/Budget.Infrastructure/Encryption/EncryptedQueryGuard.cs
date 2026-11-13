using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Budget.Infrastructure.Encryption;

/// <summary>
/// Stops a query that would compare, sort or search an encrypted column in the database (BUD-166). The database only
/// sees encrypted text, so such a query wouldn't fail on its own: it would quietly find nothing or sort wrongly. Load
/// the rows and do it in memory instead, or compare name keys (<see cref="FieldEncryption.NameKey"/>).
/// </summary>
public sealed class EncryptedQueryGuard : IQueryExpressionInterceptor
{
    private static readonly HashSet<string> Ordering = new(StringComparer.Ordinal)
    {
        nameof(Queryable.OrderBy), nameof(Queryable.OrderByDescending), nameof(Queryable.ThenBy), nameof(Queryable.ThenByDescending),
        nameof(Queryable.GroupBy), nameof(Queryable.Min), nameof(Queryable.Max), nameof(Queryable.MinBy), nameof(Queryable.MaxBy),
        nameof(Queryable.DistinctBy),
    };

    private static readonly HashSet<string> Searching = new(StringComparer.Ordinal)
    {
        nameof(string.StartsWith), nameof(string.EndsWith), nameof(string.Contains), nameof(string.ToLower), nameof(string.ToUpper),
        nameof(string.Trim), nameof(string.Substring), nameof(string.IndexOf), nameof(string.Replace), "Like",
    };

    public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        var encrypted = eventData.Context?.Model.GetEntityTypes()
            .SelectMany(e => e.GetProperties())
            .Where(p => p.IsEncrypted() && p.PropertyInfo is not null)
            .Select(p => (p.DeclaringType.ClrType, p.Name))
            .ToHashSet();
        if (encrypted is { Count: > 0 })
        {
            new Finder(encrypted).Visit(queryExpression);
        }

        return queryExpression;
    }

    private sealed class Finder(HashSet<(Type Type, string Name)> encrypted) : ExpressionVisitor
    {
        protected override Expression VisitBinary(BinaryExpression node)
        {
            if (node.NodeType is ExpressionType.Equal or ExpressionType.NotEqual or ExpressionType.GreaterThan or ExpressionType.LessThan
                or ExpressionType.GreaterThanOrEqual or ExpressionType.LessThanOrEqual)
            {
                Check(node.Left, node.Right);
                Check(node.Right, node.Left);
            }

            return base.VisitBinary(node);
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (Ordering.Contains(node.Method.Name) && node.Arguments.Count > 1 && Lambda(node.Arguments[1]) is { } key
                && EncryptedMember(key.Body) is { } sorted)
            {
                throw Refuse(sorted, "sort or group by");
            }

            if (Searching.Contains(node.Method.Name) && node.Object is not null && EncryptedMember(node.Object) is { } searched)
            {
                throw Refuse(searched, "search");
            }

            if (node.Method.Name == nameof(string.Equals) && node.Object is not null && EncryptedMember(node.Object) is { } compared)
            {
                throw Refuse(compared, "compare");
            }

            // names.Contains(x.Name): an IN list.
            if (node.Method.Name == nameof(Enumerable.Contains) && node.Arguments.Count > 0 && EncryptedMember(node.Arguments[^1]) is { } listed)
            {
                throw Refuse(listed, "compare");
            }

            return base.VisitMethodCall(node);
        }

        private void Check(Expression side, Expression other)
        {
            if (EncryptedMember(side) is { } member && !IsNull(other))
            {
                throw Refuse(member, "compare");
            }
        }

        private MemberInfo? EncryptedMember(Expression expression)
        {
            while (expression is UnaryExpression { NodeType: ExpressionType.Convert } convert)
            {
                expression = convert.Operand;
            }

            if (expression is MemberExpression { Member: PropertyInfo property, Expression: { } owner }
                && encrypted.Any(e => e.Name == property.Name && e.Type.IsAssignableFrom(owner.Type)))
            {
                return property;
            }

            // EF.Property<string>(entity, "Name")
            if (expression is MethodCallExpression { Method.Name: "Property", Arguments: [var entity, ConstantExpression { Value: string name }] } call
                && call.Method.DeclaringType?.Name == "EF" && encrypted.Any(e => e.Name == name && e.Type.IsAssignableFrom(entity.Type)))
            {
                return entity.Type.GetProperty(name) ?? (MemberInfo)entity.Type;
            }

            return null;
        }

        private static LambdaExpression? Lambda(Expression expression) => expression switch
        {
            UnaryExpression { NodeType: ExpressionType.Quote, Operand: LambdaExpression lambda } => lambda,
            LambdaExpression lambda => lambda,
            _ => null,
        };

        private static bool IsNull(Expression expression) =>
            expression is ConstantExpression { Value: null }
            || expression is UnaryExpression { NodeType: ExpressionType.Convert, Operand: ConstantExpression { Value: null } };

        private static InvalidOperationException Refuse(MemberInfo member, string what) =>
            new($"{member.DeclaringType?.Name}.{member.Name} is encrypted (BUD-166), so the database can't {what} it. " +
                "Load the rows and do it in memory, or compare name keys.");
    }
}
