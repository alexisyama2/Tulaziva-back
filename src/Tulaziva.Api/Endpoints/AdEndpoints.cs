using System.Security.Claims;
using Tulaziva.Application.Ads;
using Tulaziva.Application.Common;
using Tulaziva.Application.Cqrs;

namespace Tulaziva.Api.Endpoints;

public sealed record AdRequest(
    int CategoryId,
    string Title,
    string Description,
    decimal Price,
    string Currency,
    double? Latitude = null,
    double? Longitude = null);

public sealed record FeatureAdRequest(int Days);

public static class AdEndpoints
{
    public static IEndpointRouteBuilder MapAdEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/categories", async (IDispatcher dispatcher, CancellationToken cancellationToken) =>
                Results.Ok(await dispatcher.QueryAsync(new GetCategoriesQuery(), cancellationToken)))
            .WithTags("Ads")
            .WithSummary("The categories, with their names in the language of the request.")
            .AllowAnonymous()
            .Produces<IReadOnlyList<CategoryDto>>();

        app.MapGet("/api/featuring", async (IDispatcher dispatcher, CancellationToken cancellationToken) =>
                Results.Ok(await dispatcher.QueryAsync(new GetFeaturingOptionsQuery(), cancellationToken)))
            .WithTags("Ads")
            .WithSummary("The durations an ad can be featured for, and whether owners can feature their own ads.")
            .AllowAnonymous()
            .Produces<FeaturingOptionsDto>();

        var group = app.MapGroup("/api/ads").WithTags("Ads");

        // ---- Public ----

        group.MapGet("/", async (
                    IDispatcher dispatcher,
                    CancellationToken cancellationToken,
                    string? text,
                    int? categoryId,
                    decimal? minPrice,
                    decimal? maxPrice,
                    double? latitude,
                    double? longitude,
                    double? radiusKm,
                    int page = 1,
                    int pageSize = 20) =>
                Results.Ok(await dispatcher.QueryAsync(
                    new SearchAdsQuery(text, categoryId, minPrice, maxPrice, latitude, longitude, radiusKm, page, pageSize), cancellationToken)))
            .WithSummary("Searches the public ads. With latitude and longitude the nearest come first; add radiusKm to leave out the farther ones.")
            .AllowAnonymous()
            .Produces<PagedResult<AdSummaryDto>>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal principal, IDispatcher dispatcher, CancellationToken cancellationToken) =>
                Results.Ok(await dispatcher.QueryAsync(new GetAdQuery(id, principal.ToRequester()), cancellationToken)))
            .WithSummary("An ad. Expired or removed ads are only visible to their owner and to admins.")
            .AllowAnonymous()
            .Produces<AdDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}/images/{imageId:guid}", async (Guid id, Guid imageId, ClaimsPrincipal principal, IDispatcher dispatcher, CancellationToken cancellationToken) =>
            {
                var file = await dispatcher.QueryAsync(new GetAdImageQuery(id, imageId, principal.ToRequester()), cancellationToken);
                return Results.Stream(file.Content, file.ContentType);
            })
            .WithSummary("The file of an image of an ad.")
            .AllowAnonymous()
            .Produces(StatusCodes.Status200OK, null, "image/jpeg", "image/png", "image/webp")
            .ProducesProblem(StatusCodes.Status404NotFound);

        // ---- Signed-in users ----

        group.MapGet("/mine", async (ClaimsPrincipal principal, IDispatcher dispatcher, CancellationToken cancellationToken, int page = 1, int pageSize = 20) =>
                Results.Ok(await dispatcher.QueryAsync(new GetMyAdsQuery(principal.GetUserId(), page, pageSize), cancellationToken)))
            .WithSummary("The ads of the signed-in user, whatever their status.")
            .RequireAuthorization()
            .Produces<PagedResult<AdSummaryDto>>();

        group.MapPost("/", async (AdRequest request, ClaimsPrincipal principal, IDispatcher dispatcher, CancellationToken cancellationToken) =>
            {
                var ad = await dispatcher.SendAsync(
                    new CreateAdCommand(principal.GetUserId(), request.CategoryId, request.Title, request.Description, request.Price, request.Currency, request.Latitude, request.Longitude),
                    cancellationToken);

                return Results.Created($"/api/ads/{ad.Id}", ad);
            })
            .WithSummary("Publishes an ad for 30 days. Without latitude and longitude it uses the location where the user registered.")
            .RequireAuthorization()
            .Produces<AdDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPut("/{id:guid}", async (Guid id, AdRequest request, ClaimsPrincipal principal, IDispatcher dispatcher, CancellationToken cancellationToken) =>
                Results.Ok(await dispatcher.SendAsync(
                    new UpdateAdCommand(principal.GetUserId(), id, request.CategoryId, request.Title, request.Description, request.Price, request.Currency, request.Latitude, request.Longitude),
                    cancellationToken)))
            .WithSummary("Edits an ad of the signed-in user. Without latitude and longitude the location stays as it was.")
            .RequireAuthorization()
            .Produces<AdDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal principal, IDispatcher dispatcher, CancellationToken cancellationToken) =>
            {
                await dispatcher.SendAsync(new DeleteAdCommand(principal.GetUserId(), id), cancellationToken);
                return Results.NoContent();
            })
            .WithSummary("Deletes an ad of the signed-in user, with its images.")
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/feature", async (Guid id, FeatureAdRequest request, ClaimsPrincipal principal, IDispatcher dispatcher, CancellationToken cancellationToken) =>
                Results.Ok(await dispatcher.SendAsync(
                    new FeatureAdCommand(principal.GetUserId(), principal.IsInRole(AdminEndpoints.AdminRole), id, request.Days), cancellationToken)))
            .WithSummary("Shows the ad first in the searches for 3, 7 or 30 days, added to what is left. The owner when featuring is open to owners; an admin always.")
            .RequireAuthorization()
            .Produces<AdDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/{id:guid}/feature", async (Guid id, ClaimsPrincipal principal, IDispatcher dispatcher, CancellationToken cancellationToken) =>
                Results.Ok(await dispatcher.SendAsync(
                    new UnfeatureAdCommand(principal.GetUserId(), principal.IsInRole(AdminEndpoints.AdminRole), id), cancellationToken)))
            .WithSummary("Takes the ad out of the featured ones. The owner or an admin.")
            .RequireAuthorization()
            .Produces<AdDto>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/renew", async (Guid id, ClaimsPrincipal principal, IDispatcher dispatcher, CancellationToken cancellationToken) =>
                Results.Ok(await dispatcher.SendAsync(new RenewAdCommand(principal.GetUserId(), id), cancellationToken)))
            .WithSummary("Gives the ad another 30 days from now. Works also when it already expired.")
            .RequireAuthorization()
            .Produces<AdDto>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/images", async (Guid id, IFormFile file, ClaimsPrincipal principal, IDispatcher dispatcher, CancellationToken cancellationToken) =>
            {
                await using var stream = file.OpenReadStream();

                var image = await dispatcher.SendAsync(
                    new AddAdImageCommand(principal.GetUserId(), id, file.FileName, file.Length, stream), cancellationToken);

                return Results.Created(image.Url, image);
            })
            .WithSummary("Uploads an image (JPG, PNG or WebP, up to 5 MB, up to 10 per ad). The first one is the cover.")
            .RequireAuthorization()
            // The API authenticates with a bearer token, not with cookies, so there is no antiforgery token to check.
            .DisableAntiforgery()
            .Accepts<IFormFile>("multipart/form-data")
            .Produces<AdImageDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/{id:guid}/images/{imageId:guid}", async (Guid id, Guid imageId, ClaimsPrincipal principal, IDispatcher dispatcher, CancellationToken cancellationToken) =>
            {
                await dispatcher.SendAsync(new DeleteAdImageCommand(principal.GetUserId(), id, imageId), cancellationToken);
                return Results.NoContent();
            })
            .WithSummary("Deletes an image of an ad of the signed-in user.")
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}
