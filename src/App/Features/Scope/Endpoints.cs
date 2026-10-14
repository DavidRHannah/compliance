namespace App.Features.Scope;

public static class Endpoints
{
    public static void MapScope(this RouteGroupBuilder api)
    {
        ListScopeItems.Map(api);
        CreateScopeItem.Map(api);
        UpdateScopeItem.Map(api);
        DeleteScopeItem.Map(api);
    }
}
