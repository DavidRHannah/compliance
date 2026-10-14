using System.Linq.Expressions;
using App.Data.Entities;

namespace App.Features.Clients;

public sealed record ClientResponse(Guid Id, string Name, DateTimeOffset CreatedAt)
{
    public static readonly Expression<Func<Client, ClientResponse>> Projection =
        c => new ClientResponse(c.Id, c.Name, c.CreatedAt);

    public static ClientResponse From(Client c) => new(c.Id, c.Name, c.CreatedAt);
}
