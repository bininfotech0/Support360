using Microsoft.AspNetCore.Http;

namespace Support360.Api.Auth;

public static class SecurityHeaders
{
    private static readonly (string Name, string Value)[] Defaults =
    [
        ("X-Content-Type-Options", "nosniff"),
        ("X-Frame-Options", "DENY"),
        ("Referrer-Policy", "no-referrer"),
        ("Permissions-Policy", "accelerometer=(), camera=(), geolocation=(), microphone=(), payment=(), usb=()"),
        ("Cross-Origin-Opener-Policy", "same-origin"),
        ("Cross-Origin-Resource-Policy", "same-site"),
        ("Content-Security-Policy", "default-src 'none'; frame-ancestors 'none'; base-uri 'none'")
    ];

    public static void Apply(IHeaderDictionary headers)
    {
        foreach (var (name, value) in Defaults)
        {
            headers[name] = value;
        }
    }
}

public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    public Task InvokeAsync(HttpContext context)
    {
        SecurityHeaders.Apply(context.Response.Headers);
        context.Response.OnStarting(() =>
        {
            SecurityHeaders.Apply(context.Response.Headers);
            return Task.CompletedTask;
        });

        return _next(context);
    }
}
