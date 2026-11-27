using System.ComponentModel.DataAnnotations;
using SurveillancePlatform.Api.Domain;

namespace SurveillancePlatform.Api.Contracts;

// ---------- Auth ----------

public sealed record RegisterRequest(
    [param: Required, StringLength(200, MinimumLength = 2)] string TenantName,
    [param: Required, StringLength(200, MinimumLength = 2)] string FullName,
    [param: Required, EmailAddress, StringLength(256)] string Email,
    [param: Required, StringLength(128, MinimumLength = 10)] string Password);

public sealed record LoginRequest(
    [param: Required, EmailAddress] string Email,
    [param: Required] string Password);

public sealed record UserDto(Guid Id, string Email, string FullName, string Role, Guid TenantId, string TenantName);

public sealed record AuthResponse(string AccessToken, DateTimeOffset ExpiresAt, UserDto User);

// ---------- Homes & cameras ----------

public sealed record HomeDto(Guid Id, string Name, string? Address, int CameraCount, int OnlineCameraCount, DateTimeOffset CreatedAt);

public sealed record UpsertHomeRequest(
    [param: Required, StringLength(200, MinimumLength = 1)] string Name,
    [param: StringLength(500)] string? Address);

public sealed record CameraDto(
    Guid Id,
    Guid HomeId,
    string Name,
    string RtspUrlMasked,
    string? Location,
    CameraStatus Status,
    EntryDirection EntryDirection,
    double LinePosition,
    DateTimeOffset? LastHeartbeatAt,
    bool IsOnline);

public sealed record UpsertCameraRequest(
    [param: Required, StringLength(200, MinimumLength = 1)] string Name,
    [param: Required, StringLength(2000)] string RtspUrl,
    [param: StringLength(300)] string? Location,
    CameraStatus Status,
    EntryDirection EntryDirection,
    [param: Range(0.05, 0.95)] double LinePosition);

// ---------- Dashboard & attendance ----------

public sealed record DashboardDto(
    int TotalHomes,
    int TotalCameras,
    int OnlineCameras,
    int PeopleCurrentlyInside,
    int EntriesToday,
    int ExitsToday,
    DateTimeOffset GeneratedAt);

public sealed record AttendanceDto(
    Guid Id,
    Guid HomeId,
    Guid CameraId,
    string PersonKey,
    Guid? PersonId,
    string DisplayName,
    DateTimeOffset EntryTime,
    DateTimeOffset? ExitTime,
    int? DurationSeconds,
    bool HasEntrySnapshot,
    bool HasExitSnapshot);

public sealed record DailyAttendanceSummaryDto(
    DateOnly Date,
    int Entries,
    int Exits,
    int UniquePeople,
    double AverageDurationSeconds);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

// ---------- Persons ----------

public sealed record PersonDto(Guid Id, string DisplayName, DateTimeOffset CreatedAt);

public sealed record CreatePersonRequest(
    [param: Required, StringLength(200, MinimumLength = 1)] string DisplayName,
    [param: Required] float[] Embedding);

// ---------- Audit ----------

public sealed record AuditLogDto(
    long Id,
    Guid? TenantId,
    string Actor,
    string Action,
    string EntityType,
    string? EntityId,
    string? Changes,
    string? IpAddress,
    DateTimeOffset Timestamp);

// ---------- Internal (monitoring engine) ----------

public sealed record ActiveCameraDto(
    Guid CameraId,
    Guid TenantId,
    Guid HomeId,
    string Name,
    string RtspUrl,
    EntryDirection EntryDirection,
    double LinePosition);

public sealed record EnrolledPersonDto(Guid PersonId, Guid TenantId, string DisplayName, float[] Embedding);

public sealed record AttendanceEventDto(
    [param: Required, RegularExpression("^(entry|exit)$")] string EventType,
    [param: Required, StringLength(128)] string PersonKey,
    Guid? PersonId,
    [param: StringLength(200)] string? DisplayName,
    DateTimeOffset OccurredAt,
    float? Confidence,
    [param: StringLength(512)] string? SnapshotKey);

public sealed record IngestEventsRequest(
    [param: Required] Guid CameraId,
    [param: Required, MaxLength(500)] List<AttendanceEventDto> Events);

public sealed record IngestResultDto(int Accepted, int Ignored);

public sealed record SnapshotUploadResponse(string SnapshotKey);
