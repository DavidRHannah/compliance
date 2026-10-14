using System.Linq.Expressions;
using App.Data.Entities;

namespace App.Features.Users;

public sealed record UserResponse(Guid Id, string Email, string DisplayName, UserRole Role, Guid? ClientId, bool Disabled)
{
    public static readonly Expression<Func<User, UserResponse>> Projection =
        u => new UserResponse(u.Id, u.Email, u.DisplayName, u.Role, u.ClientId, u.DisabledAt != null);

    public static UserResponse From(User u) => new(u.Id, u.Email, u.DisplayName, u.Role, u.ClientId, u.DisabledAt != null);
}
