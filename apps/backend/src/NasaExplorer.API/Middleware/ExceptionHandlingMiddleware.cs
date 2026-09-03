using NasaExplorer.Application.Common.Exceptions;

namespace NasaExplorer.API.Middleware;

public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (ValidationException exception)
        {
            await WriteResponseAsync(context, StatusCodes.Status400BadRequest, new
            {
                message = exception.Message,
                errors = exception.Errors
            });
        }
        catch (UnauthorizedException exception)
        {
            await WriteResponseAsync(context, StatusCodes.Status401Unauthorized, new
            {
                message = exception.Message
            });
        }
        catch (NotFoundException exception)
        {
            await WriteResponseAsync(context, StatusCodes.Status404NotFound, new
            {
                message = exception.Message
            });
        }
        catch (ExpiredCursorException exception)
        {
            await WriteResponseAsync(context, StatusCodes.Status410Gone, new
            {
                code = "search_session_expired",
                message = exception.Message
            });
        }
        catch (UpstreamServiceUnavailableException exception)
        {
            await WriteResponseAsync(context, StatusCodes.Status503ServiceUnavailable, new
            {
                code = "nasa_upstream_unavailable",
                message = exception.Message
            });
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unhandled request exception.");

            await WriteResponseAsync(context, StatusCodes.Status500InternalServerError, new
            {
                message = "An unexpected error occurred."
            });
        }
    }

    private static async Task WriteResponseAsync(HttpContext context, int statusCode, object response)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(response, context.RequestAborted);
    }
}
