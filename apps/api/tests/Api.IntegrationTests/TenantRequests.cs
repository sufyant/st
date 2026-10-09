using System.Net.Http.Json;

namespace Api.IntegrationTests;

internal static class TenantRequests
{
    public const string Path = "/v1/system/tenants";

    // POST /v1/system/tenants is safe to repeat only under the key the client gives it; each call here is a new request unless the
    // test gives the key.
    public static Task<HttpResponseMessage> CreateTenantAsync(this HttpClient client, object body, string? idempotencyKey = null) =>
        client.CreateTenantAsync(JsonContent.Create(body), idempotencyKey);

    public static async Task<HttpResponseMessage> CreateTenantAsync(this HttpClient client, HttpContent body, string? idempotencyKey = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Path) { Content = body };
        request.Headers.Add("Idempotency-Key", idempotencyKey ?? Guid.NewGuid().ToString());

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
