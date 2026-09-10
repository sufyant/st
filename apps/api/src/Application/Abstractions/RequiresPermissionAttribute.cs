namespace Application.Abstractions;

[AttributeUsage(AttributeTargets.Class)]
public sealed class RequiresPermissionAttribute(string permission) : Attribute
{
    public string Permission { get; } = permission;
}
