namespace Domain.Authorization;

public sealed class Role
{
    public Guid Id { get; private set; }
    public string? Code { get; private set; }
    public string Name { get; private set; } = null!;
    public string Description { get; private set; } = null!;
}
