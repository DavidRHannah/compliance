namespace App.Features.Runs;

public static class Endpoints
{
    public static void MapRuns(this RouteGroupBuilder api)
    {
        StartRun.Map(api);
        ListRuns.Map(api);
        GetRun.Map(api);
        ResumeRun.Map(api);
    }
}
