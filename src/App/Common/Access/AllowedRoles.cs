using App.Data.Entities;

namespace App.Common.Access;

/// <summary>Endpoint metadata: the roles a route allows. The access filter reads it.</summary>
public sealed class AllowedRoles(params UserRole[] roles)
{
    public bool Contains(UserRole role) => roles.Contains(role);
}

public static class AllowedRolesExtensions
{
    public static TBuilder AllowRoles<TBuilder>(this TBuilder builder, params UserRole[] roles)
        where TBuilder : IEndpointConventionBuilder
    {
        return builder.WithMetadata(new AllowedRoles(roles));
    }
}
