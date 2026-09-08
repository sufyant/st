namespace Api.Domain;

public interface IAuditable
{
    DateTimeOffset CreatedAtUtc { get; }

    DateTimeOffset UpdatedAtUtc { get; }
}
