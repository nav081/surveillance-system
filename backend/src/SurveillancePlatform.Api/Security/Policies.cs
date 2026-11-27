using Microsoft.AspNetCore.Authorization;

namespace SurveillancePlatform.Api.Security;

public static class AuthPolicies
{
    public const string Admin = "AdminOnly";
}

public static class RateLimitPolicies
{
    public const string Auth = "auth";
}
