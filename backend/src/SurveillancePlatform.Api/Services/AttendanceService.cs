using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using SurveillancePlatform.Api.Contracts;
using SurveillancePlatform.Api.Data;
using SurveillancePlatform.Api.Domain;
using SurveillancePlatform.Api.Options;
using SurveillancePlatform.Api.Security;
using SurveillancePlatform.Api.Storage;

namespace SurveillancePlatform.Api.Services;

public enum SnapshotKind
{
    Entry,
    Exit
}

public sealed class AttendanceService(
    AppDbContext db,
    IDistributedCache cache,
    IOptions<ReportingOptions> reporting)
{
    private const int MaxExportRows = 50_000;
    private const int MaxSummaryRows = 100_000;

    // ---------- Tenant-facing reporting (filtered by tenant) ----------

    public async Task<PagedResult<AttendanceDto>> ListAsync(
        Guid? homeId, DateTimeOffset? from, DateTimeOffset? to, string? personKey,
        int page, int pageSize, CancellationToken ct)
    {
        var query = BuildQuery(homeId, from, to, personKey);
        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(r => r.EntryTime)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(ToDtoExpression)
            .ToListAsync(ct);

        return new PagedResult<AttendanceDto>(items, page, pageSize, total);
    }

    public async Task<List<DailyAttendanceSummaryDto>> SummaryAsync(Guid? homeId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var rows = await BuildQuery(homeId, from, to, null)
            .OrderBy(r => r.EntryTime)
            .Take(MaxSummaryRows)
            .Select(r => new { r.PersonKey, r.EntryTime, r.ExitTime, r.DurationSeconds })
            .ToListAsync(ct);

        var zone = ReportingZone();
        var entriesByDay = rows.GroupBy(r => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(r.EntryTime, zone).DateTime));
        var exitsByDay = rows.Where(r => r.ExitTime != null)
            .GroupBy(r => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(r.ExitTime!.Value, zone).DateTime))
            .ToDictionary(g => g.Key, g => g.ToList());

        var days = entriesByDay.Select(g => g.Key).Concat(exitsByDay.Keys).Distinct().OrderBy(d => d);
        var result = new List<DailyAttendanceSummaryDto>();

        foreach (var day in days)
        {
            var entered = rows.Where(r => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(r.EntryTime, zone).DateTime) == day).ToList();
            var exited = exitsByDay.GetValueOrDefault(day, []);
            var durations = exited.Where(r => r.DurationSeconds is not null).Select(r => (double)r.DurationSeconds!.Value).ToList();

            result.Add(new DailyAttendanceSummaryDto(
                day,
                entered.Count,
                exited.Count,
                entered.Select(r => r.PersonKey).Distinct().Count(),
                durations.Count == 0 ? 0 : Math.Round(durations.Average(), 1)));
        }
        return result;
    }

    public async Task<string> ExportCsvAsync(Guid? homeId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        var rows = await BuildQuery(homeId, from, to, null)
            .OrderBy(r => r.EntryTime)
            .Take(MaxExportRows)
            .Select(ToDtoExpression)
            .ToListAsync(ct);

        var sb = new StringBuilder();
        sb.AppendLine("Id,HomeId,CameraId,PersonKey,DisplayName,EntryTimeUtc,ExitTimeUtc,DurationSeconds");
        foreach (var r in rows)
        {
            sb.Append(r.Id).Append(',').Append(r.HomeId).Append(',').Append(r.CameraId).Append(',')
              .Append(CsvEscape(r.PersonKey)).Append(',').Append(CsvEscape(r.DisplayName)).Append(',')
              .Append(r.EntryTime.UtcDateTime.ToString("o", CultureInfo.InvariantCulture)).Append(',')
              .Append(r.ExitTime?.UtcDateTime.ToString("o", CultureInfo.InvariantCulture)).Append(',')
              .Append(r.DurationSeconds).AppendLine();
        }
        return sb.ToString();
    }

    /// <summary>Returns the storage key of a snapshot belonging to a record visible to the caller, or throws NotFound.</summary>
    public async Task<string> GetSnapshotKeyAsync(Guid recordId, SnapshotKind kind, CancellationToken ct)
    {
        var record = await db.AttendanceRecords.FirstOrDefaultAsync(r => r.Id == recordId, ct)
            ?? throw new NotFoundException("Attendance record not found.");

        var key = kind == SnapshotKind.Entry ? record.EntrySnapshotKey : record.ExitSnapshotKey;
        return key ?? throw new NotFoundException("Snapshot not available.");
    }

    // ---------- Engine ingestion (tenant derived from camera, no user context) ----------

    /// <summary>
    /// Applies entry/exit events. Idempotent: duplicate entries for an already-inside person and exits
    /// without a matching entry are ignored. The unique index on open presences backs this up under concurrency.
    /// </summary>
    public async Task<IngestResultDto> IngestEventsAsync(IngestEventsRequest request, CancellationToken ct)
    {
        var camera = await db.Cameras.IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == request.CameraId && c.Status == CameraStatus.Active, ct)
            ?? throw new NotFoundException("Active camera not found.");

        var snapshotPrefix = $"{camera.TenantId:N}/{camera.Id:N}/";
        var accepted = 0;
        var ignored = 0;

        foreach (var evt in request.Events.OrderBy(e => e.OccurredAt))
        {
            var snapshot = evt.SnapshotKey is not null && evt.SnapshotKey.StartsWith(snapshotPrefix, StringComparison.Ordinal)
                ? evt.SnapshotKey
                : null;

            var applied = evt.EventType == "entry"
                ? await ApplyEntryAsync(camera, evt, snapshot, ct)
                : await ApplyExitAsync(camera, evt, snapshot, ct);

            if (applied) accepted++; else ignored++;
        }

        if (accepted > 0)
            await cache.RemoveAsync(DashboardService.CacheKeyFor(camera.TenantId), ct);

        return new IngestResultDto(accepted, ignored);
    }

    private async Task<bool> ApplyEntryAsync(Camera camera, AttendanceEventDto evt, string? snapshot, CancellationToken ct)
    {
        var alreadyInside = await OpenRecords(camera).AnyAsync(r => r.PersonKey == evt.PersonKey, ct);
        if (alreadyInside) return false;

        var record = new AttendanceRecord
        {
            TenantId = camera.TenantId,
            HomeId = camera.HomeId,
            CameraId = camera.Id,
            PersonKey = evt.PersonKey,
            PersonId = evt.PersonId,
            DisplayName = string.IsNullOrWhiteSpace(evt.DisplayName) ? "Visitor" : evt.DisplayName!,
            EntryTime = evt.OccurredAt,
            EntryConfidence = evt.Confidence,
            EntrySnapshotKey = snapshot
        };

        db.AttendanceRecords.Add(record);
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            db.Entry(record).State = Microsoft.EntityFrameworkCore.EntityState.Detached;
            return false;
        }
    }

    private async Task<bool> ApplyExitAsync(Camera camera, AttendanceEventDto evt, string? snapshot, CancellationToken ct)
    {
        var record = await OpenRecords(camera).FirstOrDefaultAsync(r => r.PersonKey == evt.PersonKey, ct);
        if (record is null || evt.OccurredAt <= record.EntryTime) return false;

        record.ExitTime = evt.OccurredAt;
        record.DurationSeconds = (int)Math.Round((evt.OccurredAt - record.EntryTime).TotalSeconds);
        record.ExitSnapshotKey = snapshot;
        await db.SaveChangesAsync(ct);
        return true;
    }

    private IQueryable<AttendanceRecord> OpenRecords(Camera camera) =>
        db.AttendanceRecords.IgnoreQueryFilters()
            .Where(r => r.TenantId == camera.TenantId && r.HomeId == camera.HomeId && r.ExitTime == null);

    // ---------- Helpers ----------

    private IQueryable<AttendanceRecord> BuildQuery(Guid? homeId, DateTimeOffset? from, DateTimeOffset? to, string? personKey)
    {
        var query = db.AttendanceRecords.AsQueryable();
        if (homeId is { } h) query = query.Where(r => r.HomeId == h);
        if (from is { } f) query = query.Where(r => r.EntryTime >= f);
        if (to is { } t) query = query.Where(r => r.EntryTime < t);
        if (!string.IsNullOrWhiteSpace(personKey)) query = query.Where(r => r.PersonKey == personKey);
        return query;
    }

    private static readonly System.Linq.Expressions.Expression<Func<AttendanceRecord, AttendanceDto>> ToDtoExpression =
        r => new AttendanceDto(
            r.Id, r.HomeId, r.CameraId, r.PersonKey, r.PersonId, r.DisplayName,
            r.EntryTime, r.ExitTime, r.DurationSeconds,
            r.EntrySnapshotKey != null, r.ExitSnapshotKey != null);

    private TimeZoneInfo ReportingZone() => TimeZoneInfo.FindSystemTimeZoneById(reporting.Value.TimeZoneId);

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is Npgsql.PostgresException { SqlState: "23505" };

    private static string CsvEscape(string value) =>
        value.Contains(',') || value.Contains('"') ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
}
