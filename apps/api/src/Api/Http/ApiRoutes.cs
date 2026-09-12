namespace Api.Http;

public static class ApiRoutes
{
    public const string Version1 = "v1";

    public static string ForTenant(string alias) => $"/{alias}/api/{Version1}";
}
