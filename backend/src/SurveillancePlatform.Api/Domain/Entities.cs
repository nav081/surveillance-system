namespace SurveillancePlatform.Api.Domain;

/// <summary>Marker for rows that belong to exactly one tenant.</summary>
public interface ITenantScoped
{
    Guid TenantId { get; set; }
}

public class Tenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class User : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Admin;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastLoginAt { get; set; }
    public Tenant? Tenant { get; set; }
}

public class Home : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<Camera> Cameras { get; set; } = [];
}

public class Camera : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid HomeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string RtspUrl { get; set; } = string.Empty;
    public string? Location { get; set; }
    public CameraStatus Status { get; set; } = CameraStatus.Active;
    public EntryDirection EntryDirection { get; set; } = EntryDirection.TopToBottom;

    /// <summary>Normalised (0..1) vertical position of the counting line within the frame.</summary>
    public double LinePosition { get; set; } = 0.5;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Set by the monitoring engine; used to derive online/offline state.</summary>
    public DateTimeOffset? LastHeartbeatAt { get; set; }
    public Home? Home { get; set; }
}

/// <summary>An enrolled person whose face embedding (InsightFace, 512-d) is used for recognition.</summary>
public class Person : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public float[] Embedding { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// One continuous presence of a person inside a home. ExitTime is null while the person is inside.
/// </summary>
public class AttendanceRecord : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid HomeId { get; set; }
    public Guid CameraId { get; set; }

    /// <summary>"person:{guid}" for recognised people, "visitor:{track}" for unknown visitors.</summary>
    public string PersonKey { get; set; } = string.Empty;
    public Guid? PersonId { get; set; }
    public string DisplayName { get; set; } = string.Empty;

    public DateTimeOffset EntryTime { get; set; }
    public DateTimeOffset? ExitTime { get; set; }
    public int? DurationSeconds { get; set; }
    public float? EntryConfidence { get; set; }
    public string? EntrySnapshotKey { get; set; }
    public string? ExitSnapshotKey { get; set; }
}

public class AuditLog
{
    public long Id { get; set; }
    public Guid? TenantId { get; set; }
    public Guid? UserId { get; set; }
    public string Actor { get; set; } = "system";
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    public string? Changes { get; set; }
    public string? IpAddress { get; set; }
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}
