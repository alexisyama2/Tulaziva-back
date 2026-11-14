using FluentValidation;
using Tulaziva.Application.Common;
using Tulaziva.Application.Localization;

namespace Tulaziva.Api.Middleware;

/// <summary>Turns the failures of the use cases into ProblemDetails answers in the language of the request.</summary>
public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ILocalizer localizer)
    {
        try
        {
            await _next(context);
        }
        catch (Exception exception) when (!context.Response.HasStarted)
        {
            await ToResult(exception, localizer).ExecuteAsync(context);
        }
    }

    private IResult ToResult(Exception exception, ILocalizer localizer)
    {
        switch (exception)
        {
            case ValidationException validation:
                var errors = validation.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray());

                return Results.ValidationProblem(errors, title: localizer.Get(MessageKeys.ValidationFailed));

            case ConflictException conflict:
                return Results.Problem(detail: localizer.Get(conflict.MessageKey), statusCode: StatusCodes.Status409Conflict, title: "Conflict");

            case UnauthenticatedException unauthenticated:
                return Results.Problem(detail: localizer.Get(unauthenticated.MessageKey), statusCode: StatusCodes.Status401Unauthorized, title: "Unauthorized");

            case ForbiddenException forbidden:
                return Results.Problem(detail: localizer.Get(forbidden.MessageKey), statusCode: StatusCodes.Status403Forbidden, title: "Forbidden");

            case NotFoundException notFound:
                return Results.Problem(detail: localizer.Get(notFound.MessageKey), statusCode: StatusCodes.Status404NotFound, title: "Not Found");

            // A body that is not valid JSON, a value of the wrong type, a malformed form: the client's mistake, not ours.
            case BadHttpRequestException badRequest:
                _logger.LogWarning("Bad request: {Message} {Detail}", badRequest.Message, badRequest.InnerException?.Message);
                return Results.Problem(detail: localizer.Get(MessageKeys.BadRequest), statusCode: badRequest.StatusCode, title: "Bad Request");

            default:
                _logger.LogError(exception, "Unhandled exception");
                return Results.Problem(detail: localizer.Get(MessageKeys.Unexpected), statusCode: StatusCodes.Status500InternalServerError, title: "Server Error");
        }
    }
}
