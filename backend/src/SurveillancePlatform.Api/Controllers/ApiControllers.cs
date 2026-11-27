using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SurveillancePlatform.Api.Contracts;
using SurveillancePlatform.Api.Security;
using SurveillancePlatform.Api.Services;
using SurveillancePlatform.Api.Storage;

namespace SurveillancePlatform.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(AuthService auth) : ControllerBase
{
    /// <summary>Creates a new tenant and its first administrator account.</summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AuthResponse>> Register([FromBody] RegisterRequest request, CancellationToken ct) =>
        Ok(await auth.RegisterAsync(request, ct));

    /// <summary>Exchanges email and password for a JWT.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest request, CancellationToken ct) =>
        Ok(await auth.LoginAsync(request, ct));

    /// <summary>Returns the authenticated user.</summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<UserDto>> Me(CancellationToken ct) => Ok(await auth.GetCurrentUserAsync(ct));
}

[ApiController]
[Route("api")]
[Authorize]
public sealed class HomesController(HomeService homes) : ControllerBase
{
    [HttpGet("homes")]
    [ProducesResponseType<List<HomeDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<HomeDto>>> ListHomes(CancellationToken ct) =>
        Ok(await homes.ListHomesAsync(ct));

    [HttpGet("homes/{homeId:guid}")]
    [ProducesResponseType<HomeDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HomeDto>> GetHome(Guid homeId, CancellationToken ct) =>
        Ok(await homes.GetHomeAsync(homeId, ct));

    [HttpPost("homes")]
    [Authorize(Policy = AuthPolicies.Admin)]
    [ProducesResponseType<HomeDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<HomeDto>> CreateHome([FromBody] UpsertHomeRequest request, CancellationToken ct)
    {
        var home = await homes.CreateHomeAsync(request, ct);
        return CreatedAtAction(nameof(GetHome), new { homeId = home.Id }, home);
    }

    [HttpPut("homes/{homeId:guid}")]
    [Authorize(Policy = AuthPolicies.Admin)]
    [ProducesResponseType<HomeDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<HomeDto>> UpdateHome(Guid homeId, [FromBody] UpsertHomeRequest request, CancellationToken ct) =>
        Ok(await homes.UpdateHomeAsync(homeId, request, ct));

    [HttpDelete("homes/{homeId:guid}")]
    [Authorize(Policy = AuthPolicies.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteHome(Guid homeId, CancellationToken ct)
    {
        await homes.DeleteHomeAsync(homeId, ct);
        return NoContent();
    }

    [HttpGet("homes/{homeId:guid}/cameras")]
    [ProducesResponseType<List<CameraDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<CameraDto>>> ListCameras(Guid homeId, CancellationToken ct) =>
        Ok(await homes.ListCamerasAsync(homeId, ct));

    [HttpPost("homes/{homeId:guid}/cameras")]
    [Authorize(Policy = AuthPolicies.Admin)]
    [ProducesResponseType<CameraDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<CameraDto>> CreateCamera(Guid homeId, [FromBody] UpsertCameraRequest request, CancellationToken ct)
    {
        var camera = await homes.CreateCameraAsync(homeId, request, ct);
        return CreatedAtAction(nameof(GetCamera), new { cameraId = camera.Id }, camera);
    }

    [HttpGet("cameras/{cameraId:guid}")]
    [ProducesResponseType<CameraDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CameraDto>> GetCamera(Guid cameraId, CancellationToken ct) =>
        Ok(await homes.GetCameraAsync(cameraId, ct));

    [HttpPut("cameras/{cameraId:guid}")]
    [Authorize(Policy = AuthPolicies.Admin)]
    [ProducesResponseType<CameraDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CameraDto>> UpdateCamera(Guid cameraId, [FromBody] UpsertCameraRequest request, CancellationToken ct) =>
        Ok(await homes.UpdateCameraAsync(cameraId, request, ct));

    [HttpDelete("cameras/{cameraId:guid}")]
    [Authorize(Policy = AuthPolicies.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteCamera(Guid cameraId, CancellationToken ct)
    {
        await homes.DeleteCameraAsync(cameraId, ct);
        return NoContent();
    }
}

[ApiController]
[Route("api/dashboard")]
[Authorize]
public sealed class DashboardController(DashboardService dashboard) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<DashboardDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<DashboardDto>> Get(CancellationToken ct) => Ok(await dashboard.GetAsync(ct));
}

[ApiController]
[Route("api/attendance")]
[Authorize]
public sealed class AttendanceController(AttendanceService attendance) : ControllerBase
{
    /// <summary>Paged attendance records, newest first.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<AttendanceDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<AttendanceDto>>> List(
        [FromQuery] Guid? homeId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] string? personKey,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        if (page < 1) throw new Services.BadRequestException("page must be >= 1.");
        if (pageSize is < 1 or > 200) throw new Services.BadRequestException("pageSize must be between 1 and 200.");
        return Ok(await attendance.ListAsync(homeId, from, to, personKey, page, pageSize, ct));
    }

    /// <summary>Per-day entry and exit counts for the given range.</summary>
    [HttpGet("summary")]
    [ProducesResponseType<List<DailyAttendanceSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<DailyAttendanceSummaryDto>>> Summary(
        [FromQuery] Guid? homeId,
        [FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to,
        CancellationToken ct)
    {
        if (to <= from) throw new Services.BadRequestException("'to' must be after 'from'.");
        return Ok(await attendance.SummaryAsync(homeId, from, to, ct));
    }

    /// <summary>CSV export of attendance records (capped at 50,000 rows).</summary>
    [HttpGet("export")]
    [Produces("text/csv")]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK, "text/csv")]
    public async Task<IActionResult> Export(
        [FromQuery] Guid? homeId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        CancellationToken ct)
    {
        var csv = await attendance.ExportCsvAsync(homeId, from, to, ct);
        return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", $"attendance-{DateTime.UtcNow:yyyyMMddHHmmss}.csv");
    }

    /// <summary>Streams the entry or exit snapshot for a record.</summary>
    [HttpGet("{recordId:guid}/snapshots/{kind}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Snapshot(
        Guid recordId,
        SnapshotKind kind,
        [FromServices] ISnapshotStorage storage,
        CancellationToken ct)
    {
        var key = await attendance.GetSnapshotKeyAsync(recordId, kind, ct);
        var file = await storage.OpenAsync(key, ct) ?? throw new Services.NotFoundException("Snapshot file is missing.");
        return File(file.Content, file.ContentType);
    }
}

[ApiController]
[Route("api/persons")]
[Authorize]
public sealed class PersonsController(PersonService persons) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<List<PersonDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<PersonDto>>> List(CancellationToken ct) => Ok(await persons.ListAsync(ct));

    /// <summary>Enrols a person from a 512-d InsightFace embedding.</summary>
    [HttpPost]
    [Authorize(Policy = AuthPolicies.Admin)]
    [ProducesResponseType<PersonDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<PersonDto>> Create([FromBody] CreatePersonRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await persons.CreateAsync(request, ct));

    [HttpDelete("{personId:guid}")]
    [Authorize(Policy = AuthPolicies.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid personId, CancellationToken ct)
    {
        await persons.DeleteAsync(personId, ct);
        return NoContent();
    }
}

[ApiController]
[Route("api/audit-logs")]
[Authorize(Policy = AuthPolicies.Admin)]
public sealed class AuditLogsController(AuditLogService audit) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<AuditLogDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<AuditLogDto>>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        if (page < 1 || pageSize is < 1 or > 200)
            throw new Services.BadRequestException("Invalid paging parameters.");
        return Ok(await audit.ListAsync(page, pageSize, ct));
    }
}
