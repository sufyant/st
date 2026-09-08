namespace Api.Domain;

public sealed record TenantRenamedDomainEvent(Guid TenantId, string NewName) : DomainEvent;
