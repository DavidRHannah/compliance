namespace App.Features.Assessments;

public static class Endpoints
{
    public static void MapAssessments(this RouteGroupBuilder api)
    {
        ListAssessments.Map(api);
        GetAssessment.Map(api);
        ListControlAssessments.Map(api);
    }
}
