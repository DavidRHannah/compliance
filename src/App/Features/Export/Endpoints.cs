namespace App.Features.Export;

public static class Endpoints
{
    public static void MapExport(this RouteGroupBuilder api)
    {
        ExportResults.Map(api);
    }
}
