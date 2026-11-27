using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;

namespace SurveillancePlatform.Api.Utilities;

/// <summary>Encrypts RTSP URLs at rest (they commonly embed camera credentials).</summary>
public sealed class RtspUrlProtector(IDataProtectionProvider provider)
{
    private readonly IDataProtector _protector = provider.CreateProtector("surveillance.camera.rtsp.v1");

    public string Protect(string plainUrl) => _protector.Protect(plainUrl);

    public string Unprotect(string protectedUrl) => _protector.Unprotect(protectedUrl);
}

public static class RtspUrlValidator
{
    private static readonly Regex Scheme = new("^rtsps?://", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool IsValid(string url) =>
        Scheme.IsMatch(url) && Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host);

    /// <summary>Lowercases the scheme. FFmpeg (used by the engine) only recognizes lowercase "rtsp://".</summary>
    public static string Normalize(string url)
    {
        var schemeEnd = url.IndexOf("://", StringComparison.Ordinal);
        return schemeEnd < 0 ? url : url[..schemeEnd].ToLowerInvariant() + url[schemeEnd..];
    }

    /// <summary>Returns the URL with any password replaced, so it can be shown in the UI and logs safely.</summary>
    public static string Mask(string url)
    {
        var schemeEnd = url.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd < 0) return "***";

        var authorityStart = schemeEnd + 3;
        var at = url.IndexOf('@', authorityStart);
        if (at < 0) return url;

        var userInfo = url[authorityStart..at];
        var user = userInfo.Split(':')[0];
        return $"{url[..authorityStart]}{user}:****{url[at..]}";
    }
}
