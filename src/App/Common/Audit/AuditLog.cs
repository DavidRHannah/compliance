using System.Text.Json;
using System.Text.Json.Serialization;
using App.Data;
using App.Data.Entities;

namespace App.Common.Audit;

/// <summary>
/// Records who changed what, saved in the same SaveChanges as the change (RV-6).
/// Details hold changed fields only, never document text.
/// </summary>
public static class AuditLog
{
    // The same enum names as the API and the database (DECISIONS.md #14).
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    public static void AddAudit(
        this AppDbContext db,
        Guid actorId,
        Guid? engagementId,
        string action,
        string entityType,
        Guid entityId,
        object? details = null)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            ActorId = actorId,
            EngagementId = engagementId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = details is null ? null : JsonSerializer.SerializeToDocument(details, Json),
        });
    }
}
