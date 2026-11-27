using System.Security.Claims;

namespace SurveillancePlatform.Api.Security;

public static class AppClaims
{
    public const string TenantId = "tenant_id";
    public const string Role = "role";
}

/// <summary>Identity of the caller for the current request. Null tenant means "no tenant" and fails closed in queries.</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
    Guid? TenantId { get; }
    string? Email { get; }
    string? IpAddress { get; }
    bool IsAuthenticated { get; }
}

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public Guid? UserId => Parse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier));

    public Guid? TenantId => Parse(Principal?.FindFirstValue(AppClaims.TenantId));

    public string? Email => Principal?.FindFirstValue(ClaimTypes.Email);

    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    private static Guid? Parse(string? value) => Guid.TryParse(value, out var g) ? g : null;
}
