using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ControlPlane.Application.Ports;

namespace ControlPlane.Infrastructure.Clerk;

// Clerk's Backend API, as far as invitations need it (0028, 0029). A call that fails or runs out of time fails the command.
internal sealed class ClerkIdentityProvider(HttpClient http) : IIdentityProvider
{
    // Clerk matches some of its email filters partially, so only a user who owns exactly this address counts as an account.
    public async Task<bool> HasAccountAsync(string email, CancellationToken cancellationToken)
    {
        var users = await http.GetFromJsonAsync<UserResponse[]>($"users?email_address={Uri.EscapeDataString(email)}", cancellationToken);
        return users!.Any(user => user.EmailAddresses.Any(address => string.Equals(address.EmailAddress, email, StringComparison.OrdinalIgnoreCase)));
    }

    // Clerk does not send its own email (notify: false): we send the link ourselves (0029). ignore_existing lets a person be
    // invited again while an earlier Clerk invitation is still pending.
    public async Task<Uri> InviteAsync(string email, Guid invitationId, Uri acceptLink, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync(
            "invitations",
            new CreateInvitationRequest(email, acceptLink.ToString(), new InvitationMetadata(invitationId), Notify: false, IgnoreExisting: true),
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var invitation = await response.Content.ReadFromJsonAsync<InvitationResponse>(cancellationToken);
        return new Uri(invitation!.Url);
    }

    public async Task<IReadOnlyList<string>> FindVerifiedEmailsAsync(string externalUserId, CancellationToken cancellationToken)
    {
        var user = await http.GetFromJsonAsync<UserResponse>($"users/{Uri.EscapeDataString(externalUserId)}", cancellationToken);
        return [.. user!.EmailAddresses.Where(address => address.Verification?.Status == "verified").Select(address => address.EmailAddress)];
    }

    private sealed record CreateInvitationRequest(
        [property: JsonPropertyName("email_address")] string EmailAddress,
        [property: JsonPropertyName("redirect_url")] string RedirectUrl,
        [property: JsonPropertyName("public_metadata")] InvitationMetadata PublicMetadata,
        [property: JsonPropertyName("notify")] bool Notify,
        [property: JsonPropertyName("ignore_existing")] bool IgnoreExisting);

    private sealed record InvitationMetadata([property: JsonPropertyName("invitation_id")] Guid InvitationId);

    private sealed record InvitationResponse([property: JsonPropertyName("url")] string Url);

    private sealed record UserResponse([property: JsonPropertyName("email_addresses")] IReadOnlyList<EmailAddressResponse> EmailAddresses);

    private sealed record EmailAddressResponse(
        [property: JsonPropertyName("email_address")] string EmailAddress,
        [property: JsonPropertyName("verification")] VerificationResponse? Verification);

    private sealed record VerificationResponse([property: JsonPropertyName("status")] string Status);
}
