using Domain.Access;

namespace Application.Abstractions;

public interface ICurrentUser
{
    ExternalUserId Id { get; }

    bool HasPermission(string code);

    bool TryGetEmail(out EmailAddress email);
}
