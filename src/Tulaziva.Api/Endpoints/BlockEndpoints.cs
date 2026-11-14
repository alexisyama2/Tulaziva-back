using System.Security.Claims;
using Tulaziva.Application.Blocks;
using Tulaziva.Application.Cqrs;

namespace Tulaziva.Api.Endpoints;

public static class BlockEndpoints
{
    public static IEndpointRouteBuilder MapBlockEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/blocks").WithTags("Blocks").RequireAuthorization();

        group.MapGet("/", async (ClaimsPrincipal principal, IDispatcher dispatcher, CancellationToken cancellationToken) =>
                Results.Ok(await dispatcher.QueryAsync(new GetBlockedUsersQuery(principal.GetUserId()), cancellationToken)))
            .WithSummary("The users the signed-in user blocked, the last blocked first.")
            .Produces<IReadOnlyList<BlockedUserDto>>();

        group.MapPut("/{userId:guid}", async (Guid userId, ClaimsPrincipal principal, IDispatcher dispatcher, CancellationToken cancellationToken) =>
            {
                await dispatcher.SendAsync(new BlockUserCommand(principal.GetUserId(), userId), cancellationToken);
                return Results.NoContent();
            })
            .WithSummary("Blocks a user: neither can start a chat with the other or write in the chats they share. Blocking twice is harmless.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/{userId:guid}", async (Guid userId, ClaimsPrincipal principal, IDispatcher dispatcher, CancellationToken cancellationToken) =>
            {
                await dispatcher.SendAsync(new UnblockUserCommand(principal.GetUserId(), userId), cancellationToken);
                return Results.NoContent();
            })
            .WithSummary("Removes a block. Harmless when there was none.")
            .Produces(StatusCodes.Status204NoContent);

        return app;
    }
}
