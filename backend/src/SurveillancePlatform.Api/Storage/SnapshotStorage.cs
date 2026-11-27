using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using SurveillancePlatform.Api.Options;

namespace SurveillancePlatform.Api.Storage;

public sealed record SnapshotFile(Stream Content, string ContentType);

/// <summary>
/// Stores attendance snapshots. Keys are provider-independent relative paths
/// ("{tenantId}/{cameraId}/{yyyy}/{MM}/{dd}/{guid}.jpg") so the database stores the same value for local disk and S3.
/// </summary>
public interface ISnapshotStorage
{
    Task<string> SaveAsync(Guid tenantId, Guid cameraId, Stream content, string contentType, CancellationToken ct = default);
    Task<SnapshotFile?> OpenAsync(string key, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);
    Task<int> DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default);
}

internal static class SnapshotKeys
{
    private static readonly Dictionary<string, string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png"
    };

    public static string Build(Guid tenantId, Guid cameraId, string contentType, DateTimeOffset now)
    {
        if (!Extensions.TryGetValue(contentType, out var ext))
            throw new ArgumentException($"Unsupported snapshot content type '{contentType}'.", nameof(contentType));

        return $"{tenantId:N}/{cameraId:N}/{now:yyyy}/{now:MM}/{now:dd}/{Guid.NewGuid():N}{ext}";
    }

    public static string Validate(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || Path.IsPathRooted(key) || key.Contains("..", StringComparison.Ordinal)
            || key.Contains('\\') || key.Length > 512)
            throw new ArgumentException("Invalid snapshot key.", nameof(key));
        return key;
    }

    public static string ContentTypeFor(string key) =>
        key.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg";

    public static bool IsValidContentType(string contentType) => Extensions.ContainsKey(contentType);
}

public sealed class LocalSnapshotStorage(IOptions<StorageOptions> options, ILogger<LocalSnapshotStorage> logger) : ISnapshotStorage
{
    private readonly string _root = Path.GetFullPath(options.Value.LocalRootPath);

    public async Task<string> SaveAsync(Guid tenantId, Guid cameraId, Stream content, string contentType, CancellationToken ct = default)
    {
        var key = SnapshotKeys.Build(tenantId, cameraId, contentType, DateTimeOffset.UtcNow);
        var path = ResolvePath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var file = File.Create(path);
        await content.CopyToAsync(file, ct);
        return key;
    }

    public Task<SnapshotFile?> OpenAsync(string key, CancellationToken ct = default)
    {
        var path = ResolvePath(SnapshotKeys.Validate(key));
        if (!File.Exists(path)) return Task.FromResult<SnapshotFile?>(null);
        Stream stream = File.OpenRead(path);
        return Task.FromResult<SnapshotFile?>(new SnapshotFile(stream, SnapshotKeys.ContentTypeFor(key)));
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        var path = ResolvePath(SnapshotKeys.Validate(key));
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    public Task<int> DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default)
    {
        if (!Directory.Exists(_root)) return Task.FromResult(0);

        var deleted = 0;
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff.UtcDateTime)
                {
                    File.Delete(file);
                    deleted++;
                }
            }
            catch (IOException ex)
            {
                logger.LogWarning(ex, "Could not delete expired snapshot {File}", file);
            }
        }
        return Task.FromResult(deleted);
    }

    private string ResolvePath(string key)
    {
        var full = Path.GetFullPath(Path.Combine(_root, key));
        if (!full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Snapshot key escapes the storage root.", nameof(key));
        return full;
    }
}

public sealed class S3SnapshotStorage(IAmazonS3 s3, IOptions<StorageOptions> options) : ISnapshotStorage
{
    private readonly StorageOptions _options = options.Value;

    private string Bucket => _options.S3.Bucket;

    private string FullKey(string key) => _options.S3.Prefix + key;

    public async Task<string> SaveAsync(Guid tenantId, Guid cameraId, Stream content, string contentType, CancellationToken ct = default)
    {
        var key = SnapshotKeys.Build(tenantId, cameraId, contentType, DateTimeOffset.UtcNow);
        await s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = Bucket,
            Key = FullKey(key),
            InputStream = content,
            ContentType = contentType,
            ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256
        }, ct);
        return key;
    }

    public async Task<SnapshotFile?> OpenAsync(string key, CancellationToken ct = default)
    {
        try
        {
            var response = await s3.GetObjectAsync(Bucket, FullKey(SnapshotKeys.Validate(key)), ct);
            return new SnapshotFile(response.ResponseStream, response.Headers.ContentType ?? SnapshotKeys.ContentTypeFor(key));
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public Task DeleteAsync(string key, CancellationToken ct = default) =>
        s3.DeleteObjectAsync(Bucket, FullKey(SnapshotKeys.Validate(key)), ct);

    public async Task<int> DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default)
    {
        var deleted = 0;
        string? token = null;
        do
        {
            var list = await s3.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = Bucket,
                Prefix = _options.S3.Prefix,
                ContinuationToken = token
            }, ct);

            var expired = list.S3Objects
                .Where(o => o.LastModified is { } modified && modified.ToUniversalTime() < cutoff.UtcDateTime)
                .Select(o => new KeyVersion { Key = o.Key })
                .ToList();

            if (expired.Count > 0)
            {
                await s3.DeleteObjectsAsync(new DeleteObjectsRequest { BucketName = Bucket, Objects = expired }, ct);
                deleted += expired.Count;
            }
            token = list.IsTruncated == true ? list.NextContinuationToken : null;
        } while (token is not null);

        return deleted;
    }
}
