namespace App.Features.EngagementResults;

public static class Endpoints
{
    public static void MapResults(this RouteGroupBuilder api)
    {
        GetResults.Map(api);
    }
}
