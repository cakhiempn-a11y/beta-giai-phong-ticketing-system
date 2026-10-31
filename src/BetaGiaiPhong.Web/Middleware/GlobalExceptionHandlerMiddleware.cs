using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using BetaGiaiPhong.Data.Services;

namespace BetaGiaiPhong.Web.Middleware;

public sealed class GlobalExceptionHandlerMiddleware(RequestDelegate next, ILogger<GlobalExceptionHandlerMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            var correlationId = context.Items[CorrelationIdMiddleware.HeaderKey]?.ToString()
                ?? context.TraceIdentifier;

            logger.LogError(ex, "Unhandled exception occurred. CorrelationId: {CorrelationId}", correlationId);

            if (context.Response.HasStarted)
            {
                logger.LogWarning("The response has already started, exception handler cannot write body.");
                throw;
            }

            if (context.Request.Path.StartsWithSegments("/api"))
            {
                await HandleApiExceptionAsync(context, ex, correlationId);
            }
            else
            {
                // For web UI, let ASP.NET Core UseExceptionHandler redirect to /error or rethrow
                throw;
            }
        }
    }

    private static async Task HandleApiExceptionAsync(HttpContext context, Exception ex, string correlationId)
    {
        context.Response.ContentType = "application/problem+json";

        var (statusCode, title, detail, errorCode) = ex switch
        {
            BusinessRuleException be when be.Code is "SHOWTIME_NOT_FOUND" or "MOVIE_NOT_FOUND" or "ORDER_NOT_FOUND" or "NOT_FOUND"
                => (StatusCodes.Status404NotFound, "Resource Not Found", be.Message, be.Code),

            BusinessRuleException be when be.Code is "FORBIDDEN"
                => (StatusCodes.Status403Forbidden, "Forbidden", be.Message, be.Code),

            BusinessRuleException be when be.Code is "INVALID_SELECTION" or "INVALID_CALLBACK" or "INVALID_QR"
                => (StatusCodes.Status400BadRequest, "Bad Request", be.Message, be.Code),

            BusinessRuleException be when be.Code is "SEAT_NOT_AVAILABLE" or "NOT_ON_SALE" or "SEAT_CONFLICT" or "TICKET_ALREADY_USED" or "ORDER_EXPIRED"
                => (StatusCodes.Status409Conflict, "Business Conflict", be.Message, be.Code),

            BusinessRuleException be
                => (StatusCodes.Status422UnprocessableEntity, "Unprocessable Entity", be.Message, be.Code),

            UnauthorizedAccessException ue
                => (StatusCodes.Status401Unauthorized, "Unauthorized", ue.Message, "UNAUTHORIZED"),

            KeyNotFoundException knfe
                => (StatusCodes.Status404NotFound, "Not Found", knfe.Message, "NOT_FOUND"),

            _ => (StatusCodes.Status500InternalServerError, "Internal Server Error",
                  "An unexpected error occurred while processing your request. Please quote the correlation ID when contacting support.",
                  "INTERNAL_ERROR")
        };

        context.Response.StatusCode = statusCode;

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = context.Request.Path
        };

        problem.Extensions["correlationId"] = correlationId;
        problem.Extensions["errorCode"] = errorCode;

        var json = JsonSerializer.Serialize(problem, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        });

        await context.Response.WriteAsync(json);
    }
}
