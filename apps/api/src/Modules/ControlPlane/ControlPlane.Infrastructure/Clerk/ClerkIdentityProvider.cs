using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ControlPlane.Application.Ports;

namespace ControlPlane.Infrastructure.Clerk;

// Clerk's Backend API, as far as accepting an invitation needs it. A call that fails or runs out of time fails the command.
internal sealed class ClerkIdentityProvider(HttpClient http) : IIdentityProvider
{
    public async Task<IReadOnlyList<string>> FindVerifiedEmailsAsync(string externalUserId, CancellationToken cancellationToken)
    {
        var user = await http.GetFromJsonAsync<UserResponse>($"users/{Uri.EscapeDataString(externalUserId)}", cancellationToken);
        return [.. user!.EmailAddresses.Where(address => address.Verification?.Status == "verified").Select(address => address.EmailAddress)];
    }

    private sealed record UserResponse([property: JsonPropertyName("email_addresses")] IReadOnlyList<EmailAddressResponse> EmailAddresses);

    private sealed record EmailAddressResponse(
        [property: JsonPropertyName("email_address")] string EmailAddress,
        [property: JsonPropertyName("verification")] VerificationResponse? Verification);

    private sealed record VerificationResponse([property: JsonPropertyName("status")] string Status);
}
