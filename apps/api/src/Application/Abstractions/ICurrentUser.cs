using System.Diagnostics.CodeAnalysis;
using Domain.Shared;

namespace Application.Abstractions;

public interface ICurrentUser
{
    ExternalUserId Id { get; }

    bool HasPermission(string code);

    bool TryGetEmail([MaybeNullWhen(false)] out EmailAddress email);
}
