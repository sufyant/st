using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ControlPlane.Domain.Tenants;

/// <summary>
/// A system admin's request to create a tenant, under the idempotency key they gave it (section 8). It names the tenant it created; a
/// request that comes again under the same key gets that tenant, and nothing new is created. Only a hash of the request is kept.
/// </summary>
internal sealed class TenantCreationRequest
{
    public const int KeyMaxLength = 255;

    private TenantCreationRequest(Guid systemAdminId, string idempotencyKey, string requestHash, Guid tenantId, DateTimeOffset createdAt)
    {
        SystemAdminId = systemAdminId;
        IdempotencyKey = idempotencyKey;
        RequestHash = requestHash;
        TenantId = tenantId;
        CreatedAt = createdAt;
    }

    /// <summary>The catalog user id of the system admin who sent it; a key is unique per system admin.</summary>
    public Guid SystemAdminId { get; private init; }

    public string IdempotencyKey { get; private init; }

    /// <summary>The SHA-256 of the request's fields, in hexadecimal.</summary>
    public string RequestHash { get; private init; }

    public Guid TenantId { get; private init; }

    public DateTimeOffset CreatedAt { get; private init; }

    /// <summary>A key is 1 to 255 visible ASCII characters.</summary>
    public static bool IsKey(string? key) => key is { Length: > 0 and <= KeyMaxLength } && key.All(character => character is >= '!' and <= '~');

    public static TenantCreationRequest Record(
        Guid systemAdminId,
        string idempotencyKey,
        string name,
        string slug,
        string ownerEmail,
        Guid tenantId,
        DateTimeOffset createdAt) =>
        new(systemAdminId, idempotencyKey, HashOf(name, slug, ownerEmail), tenantId, createdAt);

    /// <summary>Whether a request with these fields is this request again.</summary>
    public bool IsFor(string name, string slug, string ownerEmail) => RequestHash == HashOf(name, slug, ownerEmail);

    // The fields as a JSON array, so no two different requests read the same.
    private static string HashOf(string name, string slug, string ownerEmail) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] { name, slug, ownerEmail }))));
}
