using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UpscaleLab.Application.Common;

namespace UpscaleLab.Api.Middleware;

public sealed class ExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception exception)
        {
            await WriteProblemAsync(context, exception);
        }
    }

    private async Task WriteProblemAsync(HttpContext context, Exception exception)
    {
        var (status, title, detail) = exception switch
        {
            NotFoundException => (StatusCodes.Status404NotFound, "Not Found", exception.Message),
            ConflictException => (StatusCodes.Status409Conflict, "Conflict", exception.Message),
            ForbiddenException => (StatusCodes.Status403Forbidden, "Forbidden", exception.Message),
            UnauthorizedException => (StatusCodes.Status401Unauthorized, "Unauthorized", exception.Message),
            ConfigurationException => (StatusCodes.Status503ServiceUnavailable, "Service Unavailable", exception.Message),
            ValidationException => (StatusCodes.Status400BadRequest, "Validation Failed", exception.Message),
            DbUpdateException => (StatusCodes.Status409Conflict, "Conflict", "데이터 제약 조건을 확인해 주세요."),
            _ => (StatusCodes.Status500InternalServerError, "Internal Server Error", "요청을 처리하는 중 오류가 발생했습니다.")
        };

        if (status >= 500)
        {
            logger.LogError(exception, "Unhandled API exception. TraceId: {TraceId}", context.TraceIdentifier);
        }
        else
        {
            logger.LogInformation(exception, "Handled API exception. TraceId: {TraceId}", context.TraceIdentifier);
        }

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Instance = context.Request.Path,
            Extensions = { ["traceId"] = context.TraceIdentifier }
        });
    }
}
