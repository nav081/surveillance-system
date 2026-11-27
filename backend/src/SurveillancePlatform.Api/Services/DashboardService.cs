using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SurveillancePlatform.Api.Contracts;
using SurveillancePlatform.Api.Data;
using SurveillancePlatform.Api.Domain;
using SurveillancePlatform.Api.Options;
using SurveillancePlatform.Api.Security;

namespace SurveillancePlatform.Api.Services;

/// <summary>Dashboard counters, cached per tenant in Redis (or in-memory when Redis is not configured).</summary>
public sealed class DashboardService(
    AppDbContext db,
    ICurrentUser currentUser,
    IDistributedCache cache,
    IOptions<ReportingOptions> reporting,
    IOptions<CacheOptions> cacheOptions)
{
    public static string CacheKeyFor(Guid tenantId) => $"dashboard:{tenantId:N}";

    public async Task<DashboardDto> GetAsync(CancellationToken ct)
    {
        var tenantId = currentUser.TenantId ?? throw new UnauthorizedException("Tenant context is required.");
        var cacheKey = CacheKeyFor(tenantId);

        var cached = await cache.GetStringAsync(cacheKey, ct);
        if (cached is not null)
            return JsonSerializer.Deserialize<DashboardDto>(cached)!;

        var dashboard = await ComputeAsync(ct);

        await cache.SetStringAsync(
            cacheKey,
            JsonSerializer.Serialize(dashboard),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(cacheOptions.Value.DashboardSeconds)
            },
            ct);

        return dashboard;
    }

    private async Task<DashboardDto> ComputeAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var startOfToday = StartOfLocalDay(now);
        var onlineThreshold = now - HomeService.OnlineWindow;

        var totalHomes = await db.Homes.CountAsync(ct);
        var totalCameras = await db.Cameras.CountAsync(ct);
        var onlineCameras = await db.Cameras.CountAsync(
            c => c.Status == CameraStatus.Active && c.LastHeartbeatAt >= onlineThreshold, ct);
        var inside = await db.AttendanceRecords.CountAsync(r => r.ExitTime == null, ct);
        var entriesToday = await db.AttendanceRecords.CountAsync(r => r.EntryTime >= startOfToday, ct);
        var exitsToday = await db.AttendanceRecords.CountAsync(r => r.ExitTime != null && r.ExitTime >= startOfToday, ct);

        return new DashboardDto(totalHomes, totalCameras, onlineCameras, inside, entriesToday, exitsToday, now);
    }

    private DateTimeOffset StartOfLocalDay(DateTimeOffset utcNow)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(reporting.Value.TimeZoneId);
        var local = TimeZoneInfo.ConvertTime(utcNow, zone);
        var localMidnight = new DateTime(local.Year, local.Month, local.Day, 0, 0, 0, DateTimeKind.Unspecified);
        var offset = zone.GetUtcOffset(localMidnight);
        return new DateTimeOffset(localMidnight, offset);
    }
}
