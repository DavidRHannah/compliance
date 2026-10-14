namespace App.Features.Engagements;

public static class Endpoints
{
    public static void MapEngagements(this RouteGroupBuilder api)
    {
        CreateEngagement.Map(api);
        ListEngagements.Map(api);
        GetEngagement.Map(api);
        UpdateEngagement.Map(api);
        ChangeEngagementStatus.Map(api);
        ListEngagementControls.Map(api);
        SetApplicability.Map(api);
    }
}
