namespace SurveillancePlatform.Api.Options;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "surveillance-platform";
    public string Audience { get; set; } = "surveillance-platform-clients";

    /// <summary>HMAC signing key. Must be at least 32 characters; supply via environment variable in production.</summary>
    public string Key { get; set; } = string.Empty;

    public int ExpiryMinutes { get; set; } = 60;
}

public sealed class ServiceAuthOptions
{
    public const string SectionName = "ServiceAuth";

    /// <summary>Shared secret used by the monitoring engine. Must be supplied via environment in production.</summary>
    public string ApiKey { get; set; } = string.Empty;
}

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>"Local" or "S3".</summary>
    public string Provider { get; set; } = "Local";

    public string LocalRootPath { get; set; } = "data/snapshots";

    public S3StorageOptions S3 { get; set; } = new();
}

public sealed class S3StorageOptions
{
    public string Bucket { get; set; } = string.Empty;
    public string Region { get; set; } = "us-east-1";
    public string Prefix { get; set; } = "snapshots/";

    /// <summary>Optional custom endpoint (e.g. MinIO). Leave empty for AWS.</summary>
    public string? ServiceUrl { get; set; }
}

public sealed class RetentionOptions
{
    public const string SectionName = "Retention";

    /// <summary>Attendance records and snapshots older than this many days are deleted.</summary>
    public int Days { get; set; } = 7;

    public int IntervalMinutes { get; set; } = 60;
}

public sealed class ReportingOptions
{
    public const string SectionName = "Reporting";

    /// <summary>IANA time zone used to decide what "today" means for dashboard counters.</summary>
    public string TimeZoneId { get; set; } = "UTC";
}

public sealed class CacheOptions
{
    public const string SectionName = "Cache";

    public int DashboardSeconds { get; set; } = 30;
}
