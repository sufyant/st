using System.Collections.Concurrent;
using Notifications.Application.Ports;

namespace Notifications.IntegrationTests;

public sealed class FakeChannel : INotificationChannel
{
    public ConcurrentBag<(string RecipientId, Notification Notification)> Sent { get; } = [];

    public Task SendAsync(string recipientId, Notification notification, CancellationToken cancellationToken)
    {
        Sent.Add((recipientId, notification));
        return Task.CompletedTask;
    }
}
