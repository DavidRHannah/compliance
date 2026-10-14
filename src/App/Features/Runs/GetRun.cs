using App.Common.Access;
using App.Common.Errors;
using App.Data;
using App.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace App.Features.Runs;

public static class GetRun
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/engagements/{engagementId:guid}/runs/{runId:guid}", Handle).AllowRoles(UserRole.Admin, UserRole.Consultant);
    }

    private static async Task<IResult> Handle(Guid runId, AccessContext access, AppDbContext db, CancellationToken ct)
    {
        var run = await RunResponse.ForRun(db, access.EngagementId, runId).SingleOrDefaultAsync(ct);
        return run is null ? Problems.NotFound() : Results.Ok(run);
    }
}
