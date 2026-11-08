using System.Diagnostics.CodeAnalysis;

namespace BuildingBlocks.Identity;

/// <summary>Claims accessor for the authenticated caller of the current request.</summary>
public interface ICurrentUser
{
    /// <summary>Identity-provider subject (JWT `sub`).</summary>
    [SuppressMessage("Naming", "CA1716:Identifiers should not match keywords", Justification = "Named after the OIDC `sub` claim it exposes.")]
    string Sub { get; }

    string? Email { get; }

    string? Name { get; }

    /// <summary>JWT `given_name` claim; lets callers populate a first name without splitting
    /// <see cref="Name"/> (which may be a display name in any word order/script).</summary>
    string? GivenName { get; }

    /// <summary>JWT `family_name` claim.</summary>
    string? FamilyName { get; }

    IReadOnlyList<string> Roles { get; }
}
