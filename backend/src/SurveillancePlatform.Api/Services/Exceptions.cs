using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace SurveillancePlatform.Api.Services;

public abstract class AppException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public sealed class NotFoundException(string message) : AppException(message, StatusCodes.Status404NotFound);

public sealed class ConflictException(string message) : AppException(message, StatusCodes.Status409Conflict);

public sealed class BadRequestException(string message) : AppException(message, StatusCodes.Status400BadRequest);

public sealed class UnauthorizedException(string message) : AppException(message, StatusCodes.Status401Unauthorized);

/// <summary>Maps <see cref="AppException"/> to RFC 7807 responses. Other exceptions fall through to a 500.</summary>
public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not AppException appException)
        {
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
            return false;
        }

        httpContext.Response.StatusCode = appException.StatusCode;
        await httpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = appException.StatusCode,
            Title = appException.Message,
            Instance = httpContext.Request.Path
        }, cancellationToken);
        return true;
    }
}
