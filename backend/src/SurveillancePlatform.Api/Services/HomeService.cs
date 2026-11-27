using Microsoft.EntityFrameworkCore;
using SurveillancePlatform.Api.Contracts;
using SurveillancePlatform.Api.Data;
using SurveillancePlatform.Api.Domain;
using SurveillancePlatform.Api.Security;
using SurveillancePlatform.Api.Utilities;

namespace SurveillancePlatform.Api.Services;

/// <summary>Homes and cameras. All reads are tenant-filtered by the DbContext.</summary>
public sealed class HomeService(AppDbContext db, ICurrentUser currentUser, RtspUrlProtector rtspProtector)
{
    /// <summary>A camera is online if the engine reported a heartbeat within this window.</summary>
    public static readonly TimeSpan OnlineWindow = TimeSpan.FromMinutes(2);

    public async Task<List<HomeDto>> ListHomesAsync(CancellationToken ct)
    {
        var threshold = DateTimeOffset.UtcNow - OnlineWindow;
        return await db.Homes
            .OrderBy(h => h.Name)
            .Select(h => new HomeDto(
                h.Id,
                h.Name,
                h.Address,
                h.Cameras.Count(),
                h.Cameras.Count(c => c.Status == CameraStatus.Active && c.LastHeartbeatAt >= threshold),
                h.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<HomeDto> GetHomeAsync(Guid homeId, CancellationToken ct)
    {
        var home = await FindHomeAsync(homeId, ct);
        var threshold = DateTimeOffset.UtcNow - OnlineWindow;
        return new HomeDto(
            home.Id,
            home.Name,
            home.Address,
            home.Cameras.Count,
            home.Cameras.Count(c => c.Status == CameraStatus.Active && c.LastHeartbeatAt >= threshold),
            home.CreatedAt);
    }

    public async Task<HomeDto> CreateHomeAsync(UpsertHomeRequest request, CancellationToken ct)
    {
        var home = new Home
        {
            TenantId = RequireTenant(),
            Name = request.Name.Trim(),
            Address = request.Address?.Trim()
        };
        db.Homes.Add(home);
        await db.SaveChangesAsync(ct);
        return new HomeDto(home.Id, home.Name, home.Address, 0, 0, home.CreatedAt);
    }

    public async Task<HomeDto> UpdateHomeAsync(Guid homeId, UpsertHomeRequest request, CancellationToken ct)
    {
        var home = await FindHomeAsync(homeId, ct);
        home.Name = request.Name.Trim();
        home.Address = request.Address?.Trim();
        home.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return await GetHomeAsync(homeId, ct);
    }

    public async Task DeleteHomeAsync(Guid homeId, CancellationToken ct)
    {
        var home = await FindHomeAsync(homeId, ct);
        db.Homes.Remove(home);
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<CameraDto>> ListCamerasAsync(Guid homeId, CancellationToken ct)
    {
        await FindHomeAsync(homeId, ct);
        var cameras = await db.Cameras.Where(c => c.HomeId == homeId).OrderBy(c => c.Name).ToListAsync(ct);
        return cameras.Select(ToDto).ToList();
    }

    public async Task<CameraDto> GetCameraAsync(Guid cameraId, CancellationToken ct) =>
        ToDto(await FindCameraAsync(cameraId, ct));

    public async Task<CameraDto> CreateCameraAsync(Guid homeId, UpsertCameraRequest request, CancellationToken ct)
    {
        await FindHomeAsync(homeId, ct);
        var camera = new Camera { TenantId = RequireTenant(), HomeId = homeId };
        ApplyCameraRequest(camera, request);
        db.Cameras.Add(camera);
        await db.SaveChangesAsync(ct);
        return ToDto(camera);
    }

    public async Task<CameraDto> UpdateCameraAsync(Guid cameraId, UpsertCameraRequest request, CancellationToken ct)
    {
        var camera = await FindCameraAsync(cameraId, ct);
        ApplyCameraRequest(camera, request);
        await db.SaveChangesAsync(ct);
        return ToDto(camera);
    }

    public async Task DeleteCameraAsync(Guid cameraId, CancellationToken ct)
    {
        var camera = await FindCameraAsync(cameraId, ct);
        db.Cameras.Remove(camera);
        await db.SaveChangesAsync(ct);
    }

    private void ApplyCameraRequest(Camera camera, UpsertCameraRequest request)
    {
        if (!RtspUrlValidator.IsValid(request.RtspUrl))
            throw new BadRequestException("RTSP URL must be an absolute rtsp:// or rtsps:// address.");

        camera.Name = request.Name.Trim();
        camera.RtspUrl = rtspProtector.Protect(RtspUrlValidator.Normalize(request.RtspUrl.Trim()));
        camera.Location = request.Location?.Trim();
        camera.Status = request.Status;
        camera.EntryDirection = request.EntryDirection;
        camera.LinePosition = request.LinePosition;
        camera.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private CameraDto ToDto(Camera camera)
    {
        var plain = rtspProtector.Unprotect(camera.RtspUrl);
        var online = camera.Status == CameraStatus.Active
                     && camera.LastHeartbeatAt is { } hb
                     && DateTimeOffset.UtcNow - hb <= OnlineWindow;

        return new CameraDto(
            camera.Id,
            camera.HomeId,
            camera.Name,
            RtspUrlValidator.Mask(plain),
            camera.Location,
            camera.Status,
            camera.EntryDirection,
            camera.LinePosition,
            camera.LastHeartbeatAt,
            online);
    }

    private async Task<Home> FindHomeAsync(Guid homeId, CancellationToken ct) =>
        await db.Homes.Include(h => h.Cameras).FirstOrDefaultAsync(h => h.Id == homeId, ct)
        ?? throw new NotFoundException("Home not found.");

    private async Task<Camera> FindCameraAsync(Guid cameraId, CancellationToken ct) =>
        await db.Cameras.FirstOrDefaultAsync(c => c.Id == cameraId, ct)
        ?? throw new NotFoundException("Camera not found.");

    private Guid RequireTenant() =>
        currentUser.TenantId ?? throw new UnauthorizedException("Tenant context is required.");
}
