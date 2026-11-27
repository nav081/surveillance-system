using SurveillancePlatform.Api.Storage;
using SurveillancePlatform.Api.Utilities;
using Xunit;

namespace SurveillancePlatform.Tests;

public class RtspUrlValidatorTests
{
    [Theory]
    [InlineData("rtsp://cam.local/stream")]
    [InlineData("rtsps://user:pass@10.0.0.5:554/live")]
    [InlineData("RTSP://cam.local/x")]
    public void Accepts_rtsp_urls_with_host(string url) =>
        Assert.True(RtspUrlValidator.IsValid(url));

    [Theory]
    [InlineData("http://cam.local/stream")]
    [InlineData("rtsp://")]
    [InlineData("not a url")]
    [InlineData("")]
    public void Rejects_non_rtsp_or_hostless_urls(string url) =>
        Assert.False(RtspUrlValidator.IsValid(url));

    [Fact]
    public void Mask_replaces_password_and_keeps_user_and_host()
    {
        var masked = RtspUrlValidator.Mask("rtsp://admin:S3cret!@10.0.0.5:554/live");

        Assert.Equal("rtsp://admin:****@10.0.0.5:554/live", masked);
        Assert.DoesNotContain("S3cret", masked);
    }

    [Fact]
    public void Mask_leaves_urls_without_credentials_unchanged() =>
        Assert.Equal("rtsp://cam.local/live", RtspUrlValidator.Mask("rtsp://cam.local/live"));

    [Fact]
    public void Mask_hides_non_uri_input_entirely() =>
        Assert.Equal("***", RtspUrlValidator.Mask("garbage"));

    [Fact]
    public void Normalize_lowercases_scheme_and_keeps_credentials() =>
        Assert.Equal("rtsp://NY0817:Pw%40x@192.168.1.5/stream1", RtspUrlValidator.Normalize("RTSP://NY0817:Pw%40x@192.168.1.5/stream1"));
}

public class SnapshotKeysTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Build_produces_tenant_scoped_partitioned_key()
    {
        var tenant = Guid.NewGuid();
        var camera = Guid.NewGuid();

        var key = SnapshotKeys.Build(tenant, camera, "image/jpeg", Now);

        Assert.StartsWith($"{tenant:N}/{camera:N}/2026/11/06/", key);
        Assert.EndsWith(".jpg", key);
        Assert.Equal(key, SnapshotKeys.Validate(key));
    }

    [Theory]
    [InlineData("image/png", ".png")]
    [InlineData("IMAGE/JPEG", ".jpg")]
    public void Build_maps_content_type_to_extension(string contentType, string ext)
    {
        var key = SnapshotKeys.Build(Guid.NewGuid(), Guid.NewGuid(), contentType, Now);
        Assert.EndsWith(ext, key);
    }

    [Fact]
    public void Build_rejects_unsupported_content_type() =>
        Assert.Throws<ArgumentException>(() =>
            SnapshotKeys.Build(Guid.NewGuid(), Guid.NewGuid(), "image/gif", Now));

    [Theory]
    [InlineData("../etc/passwd")]
    [InlineData("a/../b.jpg")]
    [InlineData("a\\b.jpg")]
    [InlineData("/absolute.jpg")]
    [InlineData("")]
    public void Validate_rejects_traversal_and_absolute_keys(string key) =>
        Assert.Throws<ArgumentException>(() => SnapshotKeys.Validate(key));

    [Fact]
    public void Validate_rejects_overlong_keys() =>
        Assert.Throws<ArgumentException>(() => SnapshotKeys.Validate(new string('a', 513)));

    [Theory]
    [InlineData("x/y.png", "image/png")]
    [InlineData("x/y.jpg", "image/jpeg")]
    public void ContentTypeFor_follows_extension(string key, string expected) =>
        Assert.Equal(expected, SnapshotKeys.ContentTypeFor(key));
}
