namespace Infrastructure.Messaging;

// The drainer owns this port so that handlers can live in the application layer without the
// infrastructure project referencing it.
public interface IOutboxMessageHandler
{
    string MessageType { get; }

    Task HandleAsync(string payload, CancellationToken cancellationToken);
}
