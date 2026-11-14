using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Tulaziva.Application.Ads;
using Tulaziva.Application.Auth;
using Tulaziva.Application.Cqrs;
using Tulaziva.Application.Users;

namespace Tulaziva.Api.Endpoints;

public sealed record UpdateProfileRequest(string DisplayName, string? Language = null, double? Latitude = null, double? Longitude = null);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record ChangeEmailRequest(string NewEmail, string CurrentPassword);

public sealed record DeleteAccountRequest(string Password);

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users").WithTags("Users").RequireAuthorization();

        group.MapGet("/me", async (ClaimsPrincipal principal, IDispatcher dispatcher, CancellationToken cancellationToken) =>
                Results.Ok(await dispatcher.QueryAsync(new GetCurrentUserQuery(principal.GetUserId()), cancellationToken)))
            .WithSummary("The signed-in user.")
            .Produces<UserDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/me", async (UpdateProfileRequest request, ClaimsPrincipal principal, IDispatcher dispatcher, CancellationToken cancellationToken) =>
                Results.Ok(await dispatcher.SendAsync(
                    new UpdateProfileCommand(principal.GetUserId(), request.DisplayName, request.Language, request.Latitude, request.Longitude), cancellationToken)))
            .WithSummary("Changes the name, the language and the location. A language or location that is not sent stays as it was.")
            .Produces<UserDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/me/password", async (ChangePasswordRequest request, ClaimsPrincipal principal, IDispatcher dispatcher, CancellationToken cancellationToken) =>
            {
                await dispatcher.SendAsync(new ChangePasswordCommand(principal.GetUserId(), request.CurrentPassword, request.NewPassword), cancellationToken);
                return Results.NoContent();
            })
            .WithSummary("Changes the password. The current one is required.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/me/email", async (ChangeEmailRequest request, ClaimsPrincipal principal, IDispatcher dispatcher, CancellationToken cancellationToken) =>
            {
                await dispatcher.SendAsync(new RequestEmailChangeCommand(principal.GetUserId(), request.NewEmail, request.CurrentPassword), cancellationToken);
                return Results.NoContent();
            })
            .WithSummary("Asks to change the address. Needs the password; a link goes to the new address and nothing changes until it is opened.")
            .RequireRateLimiting(RateLimitPolicies.AccountEmails)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapDelete("/me", async ([FromBody] DeleteAccountRequest request, ClaimsPrincipal principal, IDispatcher dispatcher, CancellationToken cancellationToken) =>
            {
                await dispatcher.SendAsync(new DeleteAccountCommand(principal.GetUserId(), request.Password), cancellationToken);
                return Results.NoContent();
            })
            .WithSummary("Deletes the account. Needs the password. Removes the ads, favorites and photo; the chats stay with the user shown as deleted.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/me/resend-verification", async (ClaimsPrincipal principal, IDispatcher dispatcher, CancellationToken cancellationToken) =>
            {
                await dispatcher.SendAsync(new ResendVerificationCommand(principal.GetUserId()), cancellationToken);
                return Results.NoContent();
            })
            .WithSummary("Emails a new verification link. Does nothing when the address is already verified.")
            .RequireRateLimiting(RateLimitPolicies.AccountEmails)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapPut("/me/photo", async (IFormFile file, ClaimsPrincipal principal, IDispatcher dispatcher, CancellationToken cancellationToken) =>
            {
                await using var stream = file.OpenReadStream();

                return Results.Ok(await dispatcher.SendAsync(
                    new SetProfilePhotoCommand(principal.GetUserId(), file.FileName, file.Length, stream), cancellationToken));
            })
            .WithSummary("Sets the profile photo, replacing the one it had. Only .jpg, .jpeg or .png, up to 5 MB. Send the image as the form field 'file'.")
            // The API authenticates with a bearer token, not with cookies, so there is no antiforgery token to check.
            .DisableAntiforgery()
            .Produces<UserDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapDelete("/me/photo", async (ClaimsPrincipal principal, IDispatcher dispatcher, CancellationToken cancellationToken) =>
                Results.Ok(await dispatcher.SendAsync(new RemoveProfilePhotoCommand(principal.GetUserId()), cancellationToken)))
            .WithSummary("Removes the profile photo.")
            .Produces<UserDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapGet("/{id:guid}/photo", async (Guid id, HttpContext context, IDispatcher dispatcher, CancellationToken cancellationToken) =>
            {
                var photo = await dispatcher.QueryAsync(new GetProfilePhotoQuery(id), cancellationToken);

                // The URL carries a version that changes with each new photo, so it can be cached.
                context.Response.Headers.CacheControl = "public, max-age=3600";

                return Results.Stream(photo.Content, photo.ContentType);
            })
            .WithSummary("The profile photo of a user. Public: it is shown next to the ads and in the chats.")
            .AllowAnonymous()
            .Produces(StatusCodes.Status200OK, null, "image/jpeg", "image/png")
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}

public static class ClaimsPrincipalExtensions
{
    /// <summary>The user id of the token (its "sub" claim).</summary>
    public static Guid GetUserId(this ClaimsPrincipal principal) =>
        Guid.Parse(principal.FindFirstValue("sub") ?? throw new InvalidOperationException("The token has no sub claim."));

    /// <summary>Who is calling: anonymous when there is no valid token, otherwise the user and whether it is an admin.</summary>
    public static Requester ToRequester(this ClaimsPrincipal principal) =>
        principal.Identity?.IsAuthenticated == true
            ? new Requester(principal.GetUserId(), principal.IsInRole(AdminEndpoints.AdminRole))
            : Requester.Anonymous;
}
