using App.Data;
using App.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace App.Common.Access;

public enum EngagementLock { Share, Update }

/// <summary>
/// Every engagement write locks the engagement row in its transaction: FOR SHARE for a write,
/// FOR UPDATE for close. So a write and a close cannot both succeed.
/// </summary>
public static class EngagementWrite
{
    /// <summary>Starts the transaction, locks the engagement and returns its status.</summary>
    public static async Task<(IDbContextTransaction Transaction, EngagementStatus Status)> BeginAsync(
        AppDbContext db, Guid engagementId, EngagementLock mode, CancellationToken ct)
    {
        var transaction = await db.Database.BeginTransactionAsync(ct);

        var query = mode == EngagementLock.Update
            ? db.Database.SqlQuery<EngagementStatus>($"SELECT status AS \"Value\" FROM engagements WHERE id = {engagementId} FOR UPDATE")
            : db.Database.SqlQuery<EngagementStatus>($"SELECT status AS \"Value\" FROM engagements WHERE id = {engagementId} FOR SHARE");

        // The access filter has already checked the engagement exists, and engagements are never deleted.
        var status = await query.SingleAsync(ct);
        return (transaction, status);
    }
}
