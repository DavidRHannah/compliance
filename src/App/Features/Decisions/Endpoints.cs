namespace App.Features.Decisions;

public static class Endpoints
{
    public static void MapDecisions(this RouteGroupBuilder api)
    {
        RecordDecision.Map(api);
        ListReviewFlags.Map(api);
        ClearReviewFlag.Map(api);
    }
}
