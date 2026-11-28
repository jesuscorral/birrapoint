using System.Reflection;
using System.Text.RegularExpressions;
using BirraPoint.Common.Modules;
using MediatR;

namespace BirraPoint.Api.UnitTests.Architecture;

/// <summary>Command/query separation rules (FR-105, FR-109) as functions over types and source text so each can be shown to fail.</summary>
internal static class CommandQueryRules
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    private static readonly Regex ContextUse = new(
        @"\b(?:_?(?:db|context|ctx)|\w*DbContext)\b", RegexOptions.Compiled, RegexTimeout);

    private static readonly string[] ForbiddenInQueries =
        ["SaveChanges", "FOR UPDATE", ".Find(", "FindAsync", "Attach(", ".Update(", ".UpdateRange("];

    /// <summary>Every MediatR request must be exactly one of a command or a query.</summary>
    public static IReadOnlyList<string> RequestsWithoutExactlyOneMarker(IEnumerable<Type> types) =>
        types
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IBaseRequest).IsAssignableFrom(t))
            .Where(t => Count(t, typeof(ICommand<>)) + Count(t, typeof(IQuery<>)) != 1)
            .Select(t => t.FullName!)
            .Order(StringComparer.Ordinal)
            .ToList();

    /// <summary>A query handler never writes or locks, and tracks nothing when it reads through a context.</summary>
    public static IReadOnlyList<string> QueryHandlerViolations(string source)
    {
        var violations = ForbiddenInQueries
            .Where(token => source.Contains(token, StringComparison.Ordinal))
            .Select(token => $"query handler must not use {token}")
            .ToList();
        if (ContextUse.IsMatch(source) && !source.Contains("AsNoTracking", StringComparison.Ordinal))
        {
            violations.Add("query handler reads through a context without AsNoTracking");
        }

        return violations;
    }

    /// <summary>State-protecting properties of an aggregate have no public setter; only the aggregate's own methods change them.</summary>
    public static IReadOnlyList<string> PublicSetters(Type aggregate) =>
        aggregate.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(p => p.SetMethod is { IsPublic: true } && !IsInitOnly(p))
            .Select(p => $"{aggregate.FullName}.{p.Name}")
            .Order(StringComparer.Ordinal)
            .ToList();

    private static bool IsInitOnly(PropertyInfo property) =>
        property.SetMethod!.ReturnParameter.GetRequiredCustomModifiers()
            .Contains(typeof(System.Runtime.CompilerServices.IsExternalInit));

    private static int Count(Type type, Type marker) =>
        type.GetInterfaces().Count(i => i.IsGenericType && i.GetGenericTypeDefinition() == marker);
}
