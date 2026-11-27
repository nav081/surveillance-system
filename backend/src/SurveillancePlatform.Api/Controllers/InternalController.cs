using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SurveillancePlatform.Api.Contracts;
using SurveillancePlatform.Api.Data;
using SurveillancePlatform.Api.Domain;
using SurveillancePlatform.Api.Security;
using SurveillancePlatform.Api.Services;
using SurveillancePlatform.Api.Storage;
using SurveillancePlatform.Api.Utilities;

namespace SurveillancePlatform.Api.Controllers;

/// <summary>
/// Endpoints used only by the Monitoring Engine. Authenticated with the X-Service-Key header.
/// These bypass tenant filters because the engine works across tenants; every query is scoped by camera.
/// </summary>
[ApiController]
[Route("api/internal")]
[ApiExplorerSettings(GroupName = "internal")]
[Authorize(AuthenticationSchemes = ServiceKeyAuthenticationHandler.SchemeName, Roles = "Service")]
public sealed class InternalController(
    AppDbContext db,
    RtspUrlProtector rtsp,
    PersonService persons,
    AttendanceService attendance,
    ISnapshotStorage snapshots) : ControllerBase
{
    private const long MaxSnapshotBytes = 5 * 1024 * 1024;

    /// <summary>Active cameras with decrypted RTSP URLs for the engine to read.</summary>
    [HttpGet("cameras/active")]
    [ProducesResponseType<List<ActiveCameraDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<ActiveCameraDto>>> ActiveCameras(CancellationToken ct)
    {
        var rows = await db.Cameras.IgnoreQueryFilters()
            .Where(c => c.Status == CameraStatus.Active)
            .Select(c => new { c.Id, c.TenantId, c.HomeId, c.Name, c.RtspUrl, c.EntryDirection, c.LinePosition })
            .ToListAsync(ct);

        return Ok(rows.Select(r => new ActiveCameraDto(
            r.Id, r.TenantId, r.HomeId, r.Name, rtsp.Unprotect(r.RtspUrl), r.EntryDirection, r.LinePosition)).ToList());
    }

    /// <summary>Records that the engine is reading a camera. Uses a bulk update so it is not audited.</summary>
    [HttpPost("cameras/{cameraId:guid}/heartbeat")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Heartbeat(Guid cameraId, CancellationToken ct)
    {
        var updated = await db.Cameras.IgnoreQueryFilters()
            .Where(c => c.Id == cameraId && c.Status == CameraStatus.Active)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.LastHeartbeatAt, DateTimeOffset.UtcNow), ct);

        return updated == 0 ? NotFound() : NoContent();
    }

    /// <summary>Enrolled face embeddings for all tenants. The engine partitions matching by TenantId.</summary>
    [HttpGet("persons/embeddings")]
    [ProducesResponseType<List<EnrolledPersonDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<EnrolledPersonDto>>> Embeddings(CancellationToken ct) =>
        Ok(await persons.ListEnrolledForEngineAsync(ct));

    /// <summary>Stores a cropped snapshot and returns its key. The key is only valid for the camera's tenant prefix.</summary>
    [HttpPost("snapshots")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxSnapshotBytes)]
    [ProducesResponseType<SnapshotUploadResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<SnapshotUploadResponse>> UploadSnapshot(
        [FromForm] Guid cameraId,
        [FromForm] IFormFile file,
        CancellationToken ct)
    {
        if (file.Length == 0 || file.Length > MaxSnapshotBytes)
            throw new BadRequestException("Snapshot must be between 1 byte and 5 MB.");
        if (!SnapshotKeys.IsValidContentType(file.ContentType))
            throw new BadRequestException("Snapshot must be image/jpeg or image/png.");

        var tenantId = await db.Cameras.IgnoreQueryFilters()
            .Where(c => c.Id == cameraId && c.Status == CameraStatus.Active)
            .Select(c => (Guid?)c.TenantId)
            .FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Active camera not found.");

        await using var stream = file.OpenReadStream();
        var key = await snapshots.SaveAsync(tenantId, cameraId, stream, file.ContentType, ct);
        return Ok(new SnapshotUploadResponse(key));
    }

    /// <summary>Applies a batch of entry and exit events from one camera. Duplicates are ignored, not rejected.</summary>
    [HttpPost("attendance/events")]
    [ProducesResponseType<IngestResultDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IngestResultDto>> IngestEvents([FromBody] IngestEventsRequest request, CancellationToken ct) =>
        Ok(await attendance.IngestEventsAsync(request, ct));
}
