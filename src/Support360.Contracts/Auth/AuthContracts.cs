namespace Support360.Contracts.Auth;

public sealed record LoginRequest(string Email, string Password);

public sealed record UserDto(
    Guid Id,
    Guid TenantId,
    string TenantName,
    bool IsDemoTenant,
    string Email,
    string DisplayName,
    string Role);

public sealed record LoginResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    UserDto User);

public sealed record MeResponse(UserDto User);
