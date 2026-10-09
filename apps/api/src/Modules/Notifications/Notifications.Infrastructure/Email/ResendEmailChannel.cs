using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using Notifications.Application.Ports;
using Notifications.Application;

namespace Notifications.Infrastructure.Email;

// Sends email through Resend's API, under the email's idempotency key: Resend sends one email per key.
internal sealed class ResendEmailChannel(HttpClient http, IOptions<ResendOptions> options) : IEmailChannel
{
    public async Task SendAsync(EmailMessage email, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "emails")
        {
            Content = JsonContent.Create(new { from = options.Value.From, to = new[] { email.To }, subject = email.Subject, text = email.Text }),
        };
        request.Headers.Add("Idempotency-Key", email.IdempotencyKey);

        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
