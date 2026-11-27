using Microsoft.EntityFrameworkCore;
using SurveillancePlatform.Api.Contracts;
using SurveillancePlatform.Api.Data;
using SurveillancePlatform.Api.Domain;
using SurveillancePlatform.Api.Security;

namespace SurveillancePlatform.Api.Services;

public sealed class AuthService(AppDbContext db, IJwtTokenService tokens, ICurrentUser currentUser)
{
    private const int BcryptWorkFactor = 12;

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        var email = NormalizeEmail(request.Email);

        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == email, ct))
            throw new ConflictException("An account with this email already exists.");

        var tenant = new Tenant { Name = request.TenantName.Trim() };
        var user = new User
        {
            TenantId = tenant.Id,
            Email = email,
            FullName = request.FullName.Trim(),
            Role = UserRole.Admin,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, BcryptWorkFactor)
        };

        db.Tenants.Add(tenant);
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        return BuildResponse(user, tenant.Name);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var email = NormalizeEmail(request.Email);

        // Login runs before a tenant is known, so the tenant filter is bypassed for the lookup only.
        var user = await db.Users.IgnoreQueryFilters()
            .Include(u => u.Tenant)
            .FirstOrDefaultAsync(u => u.Email == email, ct);

        if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            await RecordAuthEventAsync("LoginFailed", user, ct);
            throw new UnauthorizedException("Invalid email or password.");
        }

        user.LastLoginAt = DateTimeOffset.UtcNow;
        await RecordAuthEventAsync("LoginSucceeded", user, ct);
        return BuildResponse(user, user.Tenant!.Name);
    }

    public async Task<UserDto> GetCurrentUserAsync(CancellationToken ct)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException("Authentication required.");
        var user = await db.Users.Include(u => u.Tenant).FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new UnauthorizedException("Account no longer exists.");
        return ToDto(user, user.Tenant!.Name);
    }

    private async Task RecordAuthEventAsync(string action, User? user, CancellationToken ct)
    {
        db.AuditLogs.Add(new AuditLog
        {
            TenantId = user?.TenantId,
            UserId = user?.Id,
            Actor = user is null ? "anonymous" : $"user:{user.Id}",
            Action = action,
            EntityType = nameof(User),
            EntityId = user?.Id.ToString(),
            IpAddress = currentUser.IpAddress,
            Timestamp = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(ct);
    }

    private AuthResponse BuildResponse(User user, string tenantName)
    {
        var (token, expires) = tokens.Create(user);
        return new AuthResponse(token, expires, ToDto(user, tenantName));
    }

    private static UserDto ToDto(User user, string tenantName) =>
        new(user.Id, user.Email, user.FullName, user.Role.ToString(), user.TenantId, tenantName);

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}
