namespace Support360.Application.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public interface ISecretGenerator
{
    string Generate(int byteLength = 32);
}

public interface ISecretHasher
{
    string Hash(string secret);
}

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}
