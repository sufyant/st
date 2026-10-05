using Microsoft.AspNetCore.SignalR;

namespace Notifications.Infrastructure.Channels;

// The real-time in-app channel's endpoint (0037). Clients only listen; a signed-in user's connections are addressed by the
// identity provider's user id, which SignalR reads from the NameIdentifier claim.
internal sealed class NotificationsHub : Hub
{
    public const string Method = "notification";
}
