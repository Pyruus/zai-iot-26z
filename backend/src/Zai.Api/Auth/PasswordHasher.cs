using System.Security.Cryptography;
using System.Text;
using Isopoh.Cryptography.Argon2;

namespace Zai.Api.Auth;

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string? hash, string password);
}

public sealed class Argon2PasswordHasher : IPasswordHasher
{
    private readonly Lazy<string> _dummyHash;

    public Argon2PasswordHasher()
    {
        _dummyHash = new Lazy<string>(() => Hash(Secrets.GenerateToken()));
    }

    public string Hash(string password) => Argon2.Hash(new Argon2Config
    {
        Type = Argon2Type.HybridAddressing,
        Version = Argon2Version.Nineteen,
        TimeCost = 2,
        MemoryCost = 19456,
        Lanes = 1,
        Threads = 1,
        HashLength = 32,
        Salt = RandomNumberGenerator.GetBytes(16),
        Password = Encoding.UTF8.GetBytes(password),
    });

    public bool Verify(string? hash, string password)
    {
        // Unknown users are verified against a dummy hash so response time does not reveal which usernames exist.
        var valid = Argon2.Verify(hash ?? _dummyHash.Value, password);
        return hash is not null && valid;
    }
}
