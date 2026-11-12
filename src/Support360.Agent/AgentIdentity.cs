using System.Text.Json;

namespace Support360.Agent;

public static class AgentIdentity
{
    public static void Write(string path, AgentIdentityValue identity)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("identity file path is required");
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            AtomicIO.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var data = JsonSerializer.Serialize(identity.ToWire(), AgentJson.Options);
        AtomicIO.WriteFileAtomic(path, data, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    public static AgentIdentityValue Read(string path)
    {
        var data = File.ReadAllText(path);
        AgentIdentityWire? wire;
        try
        {
            wire = JsonSerializer.Deserialize<AgentIdentityWire>(data, AgentJson.Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("identity file is invalid JSON", ex);
        }
        if (wire is null
            || string.IsNullOrEmpty(wire.AgentId)
            || string.IsNullOrEmpty(wire.TenantId)
            || string.IsNullOrEmpty(wire.ApiKey))
        {
            throw new InvalidDataException("identity file is incomplete");
        }
        return new AgentIdentityValue(wire.AgentId, wire.TenantId, wire.ApiKey, wire.DeploymentOptIn);
    }
}

public sealed record AgentIdentityValue(string AgentId, string TenantId, string ApiKey, bool DeploymentOptIn)
{
    internal AgentIdentityWire ToWire() => new(AgentId, TenantId, ApiKey, DeploymentOptIn);
}

internal sealed record AgentIdentityWire(string AgentId, string TenantId, string ApiKey, bool DeploymentOptIn);
