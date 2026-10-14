using System.Linq.Expressions;
using App.Data;
using App.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace App.Features.Engagements;

public sealed record EngagementResponse(
    Guid Id, Guid ClientId, Guid LeadConsultantId, string Name, EngagementStatus Status, DateTimeOffset CreatedAt)
{
    public static readonly Expression<Func<Engagement, EngagementResponse>> Projection =
        e => new EngagementResponse(e.Id, e.ClientId, e.LeadConsultantId, e.Name, e.Status, e.CreatedAt);

    public static EngagementResponse From(Engagement e) =>
        new(e.Id, e.ClientId, e.LeadConsultantId, e.Name, e.Status, e.CreatedAt);
}

public static class LeadRules
{
    /// <summary>The lead must be an enabled consultant or admin (EN-9). Leading gives no access.</summary>
    public static Task<bool> IsValidLeadAsync(AppDbContext db, Guid userId, CancellationToken ct) =>
        db.Users.AnyAsync(u => u.Id == userId
            && u.DisabledAt == null
            && (u.Role == UserRole.Consultant || u.Role == UserRole.Admin), ct);
}
