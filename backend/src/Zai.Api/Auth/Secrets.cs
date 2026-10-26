using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Zai.Api.Auth;

public static class Secrets
{
    public static string GenerateToken() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
