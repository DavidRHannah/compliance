namespace App.Features.Clients;

public static class Endpoints
{
    public static void MapClients(this RouteGroupBuilder api)
    {
        CreateClient.Map(api);
        ListClients.Map(api);
        GetClient.Map(api);
    }
}
