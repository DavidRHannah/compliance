namespace App.Features.Users;

public static class Endpoints
{
    public static void MapUsers(this RouteGroupBuilder api)
    {
        InviteUser.Map(api);
        ListUsers.Map(api);
        GetUser.Map(api);
        DisableUser.Map(api);
        EnableUser.Map(api);
        ResendInvite.Map(api);
    }
}
