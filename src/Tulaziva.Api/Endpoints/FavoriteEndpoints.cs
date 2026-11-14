using System.Security.Claims;
using Tulaziva.Application.Ads;
using Tulaziva.Application.Common;
using Tulaziva.Application.Cqrs;
using Tulaziva.Application.Favorites;

namespace Tulaziva.Api.Endpoints;

public static class FavoriteEndpoints
{
    public static IEndpointRouteBuilder MapFavoriteEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/favorites").WithTags("Favorites").RequireAuthorization();

        group.MapGet("/", async (ClaimsPrincipal principal, IDispatcher dispatcher, CancellationToken cancellationToken, int page = 1, int pageSize = 20) =>
                Results.Ok(await dispatcher.QueryAsync(new GetFavoritesQuery(principal.GetUserId(), page, pageSize), cancellationToken)))
            .WithSummary("The ads the signed-in user saved that are still public, the last saved first.")
            .Produces<PagedResult<AdSummaryDto>>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/ids", async (ClaimsPrincipal principal, IDispatcher dispatcher, CancellationToken cancellationToken) =>
                Results.Ok(await dispatcher.QueryAsync(new GetFavoriteIdsQuery(principal.GetUserId()), cancellationToken)))
            .WithSummary("The ids of every ad the signed-in user saved, to paint the hearts in the lists.")
            .Produces<IReadOnlyList<Guid>>();

        group.MapPut("/{adId:guid}", async (Guid adId, ClaimsPrincipal principal, IDispatcher dispatcher, CancellationToken cancellationToken) =>
            {
                await dispatcher.SendAsync(new AddFavoriteCommand(principal.GetUserId(), adId), cancellationToken);
                return Results.NoContent();
            })
            .WithSummary("Saves a public ad. Saving it twice is harmless.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{adId:guid}", async (Guid adId, ClaimsPrincipal principal, IDispatcher dispatcher, CancellationToken cancellationToken) =>
            {
                await dispatcher.SendAsync(new RemoveFavoriteCommand(principal.GetUserId(), adId), cancellationToken);
                return Results.NoContent();
            })
            .WithSummary("Removes an ad from the favorites. Harmless when it was not saved.")
            .Produces(StatusCodes.Status204NoContent);

        return app;
    }
}
