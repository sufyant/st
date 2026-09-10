using System.Security.Cryptography;
using System.Text;

namespace Infrastructure.Access;

public static class InvitationTokens
{
    private const int ByteLength = 32;

    public static string Create() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(ByteLength))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

    public static string Hash(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}
