using Tulaziva.Application.Admin;
using Tulaziva.Application.Ads;
using Tulaziva.Application.Chat;
using Tulaziva.Application.Common;
using Tulaziva.Application.Cqrs;
using Tulaziva.Domain.Enums;
using Tulaziva.Application.Users;

namespace Tulaziva.Api.Endpoints;

public sealed record SendTestEmailRequest(string To);

public static class AdminEndpoints
{
    public const string AdminPolicy = "Admin";
    public const string AdminRole = "Admin";

    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin").WithTags("Admin").RequireAuthorization(AdminPolicy);

        group.MapPost("/users", async (CreateAdminUserCommand command, IDispatcher dispatcher, CancellationToken cancellationToken) =>
                Results.Created("/api/admin/users", await dispatcher.SendAsync(command, cancellationToken)))
            .WithSummary("Creates an administrator.")
            .Produces<UserDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/users", async (IDispatcher dispatcher, CancellationToken cancellationToken, int page = 1, int pageSize = 20) =>
                Results.Ok(await dispatcher.QueryAsync(new GetUsersQuery(page, pageSize), cancellationToken)))
            .WithSummary("Lists the users, newest first.")
            .Produces<PagedResult<UserDto>>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/email-logs", async (IDispatcher dispatcher, CancellationToken cancellationToken, EmailStatus? status = null, int page = 1, int pageSize = 20) =>
                Results.Ok(await dispatcher.QueryAsync(new GetEmailLogsQuery(status, page, pageSize), cancellationToken)))
            .WithSummary("The emails the application tried to send and what happened to each (Queued, Sent, Failed, Logged), newest first. Never their text.")
            .Produces<PagedResult<EmailLogDto>>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/email-test", async (SendTestEmailRequest request, IDispatcher dispatcher, CancellationToken cancellationToken) =>
                Results.Ok(await dispatcher.SendAsync(new SendTestEmailCommand(request.To ?? string.Empty), cancellationToken)))
            .WithSummary("Sends a test email straight to the mail server and answers with what it said (ok, or the exact error). For setting up the mail account.")
            .RequireRateLimiting(RateLimitPolicies.AccountEmails)
            .Produces<EmailTestResult>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapGet("/attachments", async (IDispatcher dispatcher, CancellationToken cancellationToken, int page = 1, int pageSize = 20) =>
                Results.Ok(await dispatcher.QueryAsync(new ListAttachmentsQuery(page, pageSize), cancellationToken)))
            .WithSummary("The files sent in the chats, newest first, with who sent them. Never the text of the conversations: those are private.")
            .Produces<PagedResult<AdminAttachmentDto>>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/ads/{id:guid}/remove", async (Guid id, IDispatcher dispatcher, CancellationToken cancellationToken) =>
            {
                await dispatcher.SendAsync(new RemoveAdCommand(id), cancellationToken);
                return Results.NoContent();
            })
            .WithSummary("Takes an ad down. Its owner can still see it, but the public cannot.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}
